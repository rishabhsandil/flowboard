using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace FlowBoard.Tests.Integration;

/// <summary>
/// Regression coverage for coding-standard A8: the global exception handler
/// must never echo <c>Exception.Message</c> or <c>Exception.StackTrace</c> to
/// the client. Earlier the handler did exactly that "for debuggability",
/// which leaked file paths, secrets in inner-exception messages, and internal
/// SQL when an Npgsql error bubbled up.
///
/// Strategy: register a test-only <see cref="IStartupFilter"/> that appends a
/// middleware which throws on a magic path. Then issue a GET to that path and
/// inspect the response — it must be the sanitized 500 envelope.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class GlobalExceptionHandlerTests : IAsyncLifetime
{
    private const string TripPath = "/__test/throw-please";
    private const string SecretSentinel = "ALPHA-DO-NOT-LEAK";

    private readonly PostgresFixture _db;

    public GlobalExceptionHandlerTests(PostgresFixture db) { _db = db; }

    public async Task InitializeAsync() => await _db.ResetDataAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task UnhandledException_ReturnsSanitizedEnvelope_NoStackTraceNoMessage()
    {
        await using var factory = new FlowBoardFactory(_db.ConnectionString)
            .WithWebHostBuilder(b => b.ConfigureServices(s =>
                s.AddSingleton<IStartupFilter, ThrowingStartupFilter>()));

        var http = factory.CreateClient();
        var res = await http.GetAsync(TripPath);

        Assert.Equal(HttpStatusCode.InternalServerError, res.StatusCode);

        var body = await res.Content.ReadAsStringAsync();

        // Positive: the sanitized envelope and the correlation id ride along
        // so the user has something to quote when reporting an incident.
        Assert.Contains("\"error\":\"internal_error\"", body);
        Assert.Contains("correlationId", body);

        // Negative: none of the exception internals leak. Each of these would
        // ride out in the old handler's payload — keep the assertion list
        // exhaustive so a future regression fails loudly.
        Assert.DoesNotContain(SecretSentinel, body);
        Assert.DoesNotContain("stackTrace",   body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("at FlowBoard", body);
        Assert.DoesNotContain("InvalidOperationException", body);
        Assert.DoesNotContain(".cs:line",     body);
    }

    /// <summary>
    /// IStartupFilter that appends a middleware AFTER everything Program.cs
    /// configures. Because UseExceptionHandler is registered earlier in
    /// Program's pipeline, the throw bubbles up into the handler we want to
    /// exercise — not into the framework's default error page.
    /// </summary>
    private sealed class ThrowingStartupFilter : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next)
        {
            return app =>
            {
                next(app);
                app.Use(async (ctx, n) =>
                {
                    if (ctx.Request.Path.Equals(TripPath, StringComparison.Ordinal))
                    {
                        // Include a sentinel string so the assertion can prove
                        // the message isn't being copied into the response.
                        throw new InvalidOperationException(
                            $"boom: {SecretSentinel} — db host=internal.local password=hunter2");
                    }
                    await n();
                });
            };
        }
    }
}
