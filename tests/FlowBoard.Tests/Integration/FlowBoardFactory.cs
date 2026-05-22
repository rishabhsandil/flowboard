using FlowBoard.Api.Email;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace FlowBoard.Tests.Integration;

/// <summary>
/// Boots the real <see cref="Program"/> ASP.NET Core pipeline against the
/// fixture's Postgres database. The factory is per-test (cheap; the host
/// build is fast and no listening socket is opened).
/// </summary>
public sealed class FlowBoardFactory : WebApplicationFactory<Program>
{
    private readonly string _connectionString;
    private readonly Dictionary<string, string?>? _overrides;
    private readonly IEmailSender? _emailSender;

    public FlowBoardFactory(string connectionString) : this(connectionString, null, null) { }

    /// <summary>
    /// Test factory variant that lets a specific test override config keys
    /// (e.g. to enable the per-email cooldown for a throttle-behaviour test)
    /// or swap in a recording IEmailSender to assert on sent messages.
    /// </summary>
    public FlowBoardFactory(
        string connectionString,
        Dictionary<string, string?>? overrides,
        IEmailSender? emailSender)
    {
        _connectionString = connectionString;
        _overrides = overrides;
        _emailSender = emailSender;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // Test environment skips HTTPS redirection + HSTS, which would
        // otherwise reject our HttpClient calls.
        builder.UseEnvironment("Development");

        // The DbConnectionFactory checks DATABASE_URL FIRST. If the dev has
        // it set globally we'd hit the real DB — clear it for this process.
        Environment.SetEnvironmentVariable("DATABASE_URL", null);

        builder.ConfigureAppConfiguration((_, config) =>
        {
            var settings = new Dictionary<string, string?>
            {
                ["ConnectionStrings:Neon"] = _connectionString,

                // Tests need the plaintext reset token in the response so
                // the flow completes without a real mailer. The runtime
                // requires both ASPNETCORE_ENVIRONMENT=Development AND this
                // flag, so flipping it on in tests is safe.
                ["Forgot:EchoTokenInResponse"] = "true",

                // Disable the per-email cooldown by default so tests that
                // call /forgot multiple times for the same user don't get
                // silently throttled. The dedicated cooldown test passes
                // its own override.
                ["Forgot:PerEmailCooldownSeconds"] = "0",

                // The cleanup loop would otherwise spin a background DB
                // task that fights the per-test TRUNCATE.
                ["Forgot:Cleanup:Enabled"] = "false",

                // Default IEmailSender is the log sender; pin it explicitly
                // so a stray env var can't flip a test to Resend.
                ["Email:Provider"]        = "log",
                ["Email:FromAddress"]     = "no-reply@flowboard.test",
                ["Email:FromName"]        = "FlowBoard (test)",
                ["Email:FrontendBaseUrl"] = "http://localhost:5173",

                // NOTE: do NOT override Jwt:Secret / Jwt:RefreshSecret here.
                // Program.cs resolves the JWT secret synchronously while
                // building the builder (before this callback fires), so the
                // bearer middleware would get the dev fallback while
                // JwtService — which reads IConfiguration after host build —
                // would see our override. The mismatch produces a silent
                // 401 on every request. Letting both paths share the dev
                // fallback (Development environment) keeps them in sync.
            };

            if (_overrides is not null)
            {
                foreach (var (k, v) in _overrides)
                    settings[k] = v;
            }

            config.AddInMemoryCollection(settings);
        });

        // Swap in a test-supplied IEmailSender (e.g. a RecordingEmailSender)
        // so the test can assert which mails were dispatched. Runs AFTER the
        // app's own AddFlowBoardEmail, so the replacement wins.
        if (_emailSender is not null)
        {
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IEmailSender>();
                services.AddSingleton(_emailSender);
            });
        }

        // Belt-and-braces: ensure the per-developer Jwt__Secret env var
        // can't leak into the test process and override the dev fallback.
        Environment.SetEnvironmentVariable("Jwt__Secret",        null);
        Environment.SetEnvironmentVariable("Jwt__RefreshSecret", null);
    }

    /// <summary>
    /// All test clients send <c>X-Requested-With: XMLHttpRequest</c> so the
    /// <see cref="FlowBoard.Api.Middleware.CsrfProtectionMiddleware"/> does not
    /// reject mutating requests in integration tests.
    /// </summary>
    protected override void ConfigureClient(HttpClient client)
    {
        base.ConfigureClient(client);
        client.DefaultRequestHeaders.Add("X-Requested-With", "XMLHttpRequest");
    }
}
