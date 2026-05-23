using System.Text;
using System.Threading.RateLimiting;
using FlowBoard.Api.Auth;
using FlowBoard.Api.Email;
using FlowBoard.Api.Middleware;
using FlowBoard.Core.Data;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

// Bootstrap Serilog from appsettings + environment. Console sink in dev for
// readability; JSON in non-dev so log aggregators (Railway, Loki, Datadog)
// can parse fields.
builder.Host.UseSerilog((ctx, services, lc) =>
{
    lc.ReadFrom.Configuration(ctx.Configuration)
      .ReadFrom.Services(services)
      .Enrich.FromLogContext()
      .Enrich.WithProperty("App", "FlowBoard.Api")
      .WriteTo.Console(
          outputTemplate: ctx.HostingEnvironment.IsDevelopment()
              ? "[{Timestamp:HH:mm:ss} {Level:u3}] {CorrelationId} {Message:lj} {Properties:j}{NewLine}{Exception}"
              : "{\"ts\":\"{Timestamp:o}\",\"level\":\"{Level:u3}\",\"corr\":\"{CorrelationId}\",\"msg\":{Message:lj},\"props\":{Properties:j}}{NewLine}{Exception}");
});

static string ResolveSecret(WebApplicationBuilder b, string configKey, string envKey, string devFallback)
{
    var envValue = Environment.GetEnvironmentVariable(envKey);
    if (!string.IsNullOrWhiteSpace(envValue)) return envValue;

    var configValue = b.Configuration[configKey];
    if (!string.IsNullOrWhiteSpace(configValue)) return configValue;

    if (b.Environment.IsDevelopment()) return devFallback;

    throw new InvalidOperationException($"{configKey} is missing or empty.");
}

DapperSetup.Initialize();

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo { Title = "FlowBoard API", Version = "v1" });
    var jwtScheme = new OpenApiSecurityScheme
    {
        Name = "Authorization",
        Description = "JWT Bearer token. Format: \"Bearer {token}\".",
        In = ParameterLocation.Header,
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        Reference = new OpenApiReference { Id = "Bearer", Type = ReferenceType.SecurityScheme }
    };
    c.AddSecurityDefinition("Bearer", jwtScheme);
    c.AddSecurityRequirement(new OpenApiSecurityRequirement { [jwtScheme] = Array.Empty<string>() });
});

builder.Services.AddSingleton<DbConnectionFactory>();
builder.Services.AddSingleton<JwtService>();
builder.Services.AddSingleton<ProjectAuthorizer>();
builder.Services.AddSingleton<FlowBoard.Api.Activity.ActivityLogger>();
builder.Services.AddFlowBoardEmail(builder.Configuration);
builder.Services.AddHostedService<PasswordResetCleanupService>();

// JWT Auth
var jwtSecret = ResolveSecret(
    builder,
    "Jwt:Secret",
    "Jwt__Secret",
    "dev-access-secret-change-before-prod-1234567890");

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSecret)),
            ValidateIssuer = false,
            ValidateAudience = false,
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromMinutes(1)
        };
    });
builder.Services.AddAuthorization();

builder.Services.AddCors(options =>
{
    options.AddPolicy("Frontend", policy =>
    {
        policy.WithOrigins(
                "https://flowboard.vercel.app",
                "http://localhost:5173"
            )
            .WithHeaders("Content-Type", "Authorization", "X-Requested-With", "X-Correlation-Id")
            .WithMethods("GET", "POST", "PATCH", "PUT", "DELETE", "OPTIONS");
    });
});

// Surface the request correlation id on every framework-generated ProblemDetails
// response (e.g. [ApiController] model-validation 400s, empty-body Unauthorized /
// NotFound / BadRequest). Controllers that return the custom `{ error: code }`
// envelope keep their existing shape — the response header X-Correlation-Id
// remains the source of truth for those.
builder.Services.AddProblemDetails(options =>
{
    options.CustomizeProblemDetails = ctx =>
    {
        if (ctx.HttpContext.Items.TryGetValue(CorrelationIdMiddleware.ItemKey, out var id)
            && id is string s)
        {
            ctx.ProblemDetails.Extensions["correlationId"] = s;
        }
    };
});

// Rate limiting — protects /auth/* against brute-force and credential stuffing.
// Partition by client IP so one abusive caller doesn't lock out everyone behind
// the same proxy. Two policies:
//   "auth-strict" — 5 req / minute per IP    (login, refresh)
//   "auth-loose"  — 10 req / 5 min per IP    (register)
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

    static string PartitionKey(HttpContext ctx) =>
        ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown";

    options.AddPolicy("auth-strict", ctx =>
        RateLimitPartition.GetFixedWindowLimiter(PartitionKey(ctx), _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 5,
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0,
        }));

    options.AddPolicy("auth-loose", ctx =>
        RateLimitPartition.GetFixedWindowLimiter(PartitionKey(ctx), _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 10,
            Window = TimeSpan.FromMinutes(5),
            QueueLimit = 0,
        }));
});

var app = builder.Build();

// Correlation ID must run before other middleware so every log line and the
// global exception handler can attach the same id. Also adds the
// `X-Correlation-Id` response header.
app.UseMiddleware<CorrelationIdMiddleware>();

// Security headers on every response (X-Frame-Options, X-Content-Type-Options, etc.).
app.UseMiddleware<SecurityHeadersMiddleware>();

// CSRF defense-in-depth: require X-Requested-With: XMLHttpRequest on mutating requests.
app.UseMiddleware<CsrfProtectionMiddleware>();

// Structured request logging (method, path, status, ms, correlation id).
app.UseSerilogRequestLogging(o =>
{
    o.EnrichDiagnosticContext = (diag, http) =>
    {
        diag.Set("CorrelationId", http.Items[CorrelationIdMiddleware.ItemKey] ?? "-");
        diag.Set("User", http.User?.Identity?.Name ?? "-");
    };
});

// Global exception handler — logs the full exception server-side and returns a
// sanitized envelope to the client. Stack traces / inner messages NEVER leak
// across the wire (coding-standard A8); the correlation id is the only thing
// the user can quote when reporting an incident, and the operator looks the
// rest up in the structured logs.
app.UseExceptionHandler(errorApp =>
{
    errorApp.Run(async ctx =>
    {
        var feature = ctx.Features.Get<Microsoft.AspNetCore.Diagnostics.IExceptionHandlerFeature>();
        var logger  = ctx.RequestServices.GetRequiredService<ILoggerFactory>()
                          .CreateLogger("GlobalExceptionHandler");
        if (feature is not null)
            logger.LogError(feature.Error, "Unhandled request exception {Path}", ctx.Request.Path);

        ctx.Response.StatusCode  = StatusCodes.Status500InternalServerError;
        ctx.Response.ContentType = "application/json";

        var correlationId = ctx.Items[FlowBoard.Api.Middleware.CorrelationIdMiddleware.ItemKey] as string;
        await ctx.Response.WriteAsJsonAsync(new
        {
            error = "internal_error",
            correlationId,
        });
    });
});

// Railway: bind to 0.0.0.0:$PORT. Locally: default to http://localhost:8080
// so the Vite client can call the API without extra setup.
var port = Environment.GetEnvironmentVariable("PORT");
if (!string.IsNullOrEmpty(port))
{
    app.Urls.Add($"http://0.0.0.0:{port}");
}
else if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("ASPNETCORE_URLS")))
{
    app.Urls.Add("http://localhost:8080");
}

// Swagger in all envs — it's part of the portfolio.
app.UseSwagger();
app.UseSwaggerUI();

// HTTPS hardening in non-dev. Locally we keep plain HTTP so Vite can hit
// us without certificate fuss; in prod Railway terminates TLS at the edge.
if (!app.Environment.IsDevelopment())
{
    app.UseHsts();
    app.UseHttpsRedirection();
}

app.UseRateLimiter();
app.UseCors("Frontend");
app.UseAuthentication();
app.UseAuthorization();

app.MapGet("/", () => Results.Redirect("/swagger"));
app.MapGet("/api/health", () => Results.Ok(new { ok = true }));
app.MapControllers();

app.Run();

// Exposed so the test project's WebApplicationFactory<Program> can find an
// entry point. With top-level statements the generated Program is internal.
public partial class Program { }
