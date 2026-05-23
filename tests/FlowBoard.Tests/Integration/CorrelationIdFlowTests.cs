using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FlowBoard.Api.Middleware;

namespace FlowBoard.Tests.Integration;

/// <summary>
/// Pins the request-scoped correlation id contract end-to-end:
///   • <see cref="CorrelationIdMiddleware"/> echoes the inbound
///     <c>X-Correlation-Id</c> header back on the response (or generates one
///     when absent), and
///   • framework-shaped 4xx ProblemDetails responses surface the same id in
///     the JSON body via <c>Extensions["correlationId"]</c>, so a user who
///     screenshots the error body still has something an operator can grep
///     for in the structured logs.
///
/// The custom <c>{ error: code }</c> envelope (BadRequest/Unauthorized with
/// an explicit body) is NOT covered here — those responses keep their shape
/// for the frontend's <c>humanizeApiError</c> mapping; correlation id rides
/// in the response header for those.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class CorrelationIdFlowTests : IntegrationTestBase
{
    public CorrelationIdFlowTests(PostgresFixture db) : base(db) { }

    [Fact]
    public async Task InboundCorrelationId_IsEchoedOnResponseHeader()
    {
        var http = Anon();
        var inbound = "test-corr-" + Guid.NewGuid().ToString("N");

        var req = new HttpRequestMessage(HttpMethod.Get, "/api/health");
        req.Headers.Add(CorrelationIdMiddleware.HeaderName, inbound);

        var res = await http.SendAsync(req);

        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        Assert.True(res.Headers.TryGetValues(CorrelationIdMiddleware.HeaderName, out var echoed));
        Assert.Equal(inbound, echoed!.Single());
    }

    [Fact]
    public async Task MissingCorrelationId_IsGeneratedAndReturned()
    {
        var http = Anon();

        var res = await http.GetAsync("/api/health");

        Assert.True(res.Headers.TryGetValues(CorrelationIdMiddleware.HeaderName, out var values));
        var id = values!.Single();
        Assert.False(string.IsNullOrWhiteSpace(id));
        // Generated ids are 32-char "N" format Guids — pin the shape so a
        // future change that picks a different scheme is caught.
        Assert.Equal(32, id.Length);
    }

    [Fact]
    public async Task ProblemDetailsResponse_IncludesCorrelationId_MatchingResponseHeader()
    {
        // POSTing an invalid login body trips [ApiController] model
        // validation, which returns a framework-generated ProblemDetails
        // 400 — the path that flows through CustomizeProblemDetails.
        var http = Anon();
        var inbound = "fixed-corr-id-" + Guid.NewGuid().ToString("N");

        var req = new HttpRequestMessage(HttpMethod.Post, "/api/auth/login")
        {
            Content = JsonContent.Create(new { email = "not-an-email", password = "" })
        };
        req.Headers.Add(CorrelationIdMiddleware.HeaderName, inbound);

        var res = await http.SendAsync(req);

        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        Assert.Equal(inbound, res.Headers.GetValues(CorrelationIdMiddleware.HeaderName).Single());

        var body = await res.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(body.TryGetProperty("correlationId", out var corrProp),
            "ProblemDetails body should expose correlationId via Extensions");
        Assert.Equal(inbound, corrProp.GetString());
    }
}
