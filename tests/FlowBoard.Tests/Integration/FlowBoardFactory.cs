using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

namespace FlowBoard.Tests.Integration;

/// <summary>
/// Boots the real <see cref="Program"/> ASP.NET Core pipeline against the
/// fixture's Postgres database. The factory is per-test (cheap; the host
/// build is fast and no listening socket is opened).
/// </summary>
public sealed class FlowBoardFactory : WebApplicationFactory<Program>
{
    private readonly string _connectionString;

    public FlowBoardFactory(string connectionString) => _connectionString = connectionString;

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
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Neon"] = _connectionString,
                // NOTE: do NOT override Jwt:Secret / Jwt:RefreshSecret here.
                // Program.cs resolves the JWT secret synchronously while
                // building the builder (before this callback fires), so the
                // bearer middleware would get the dev fallback while
                // JwtService — which reads IConfiguration after host build —
                // would see our override. The mismatch produces a silent
                // 401 on every request. Letting both paths share the dev
                // fallback (Development environment) keeps them in sync.
            });
        });

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
