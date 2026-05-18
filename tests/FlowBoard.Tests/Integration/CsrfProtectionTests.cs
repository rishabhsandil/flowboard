using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace FlowBoard.Tests.Integration;

/// <summary>
/// Verifies that the CSRF protection middleware blocks state-mutating requests
/// that lack the <c>X-Requested-With: XMLHttpRequest</c> header, and allows
/// safe requests (GET, auth endpoints) through unconditionally.
/// </summary>
public sealed class CsrfProtectionTests : IntegrationTestBase
{
    public CsrfProtectionTests(PostgresFixture db) : base(db) { }

    [Fact]
    public async Task Get_WithoutXRequestedWith_IsAllowed()
    {
        // GETs never need the header — safe methods are always allowed.
        var client = Factory.CreateClient();
        client.DefaultRequestHeaders.Remove("X-Requested-With");

        var res = await client.GetAsync("/api/health");
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
    }

    [Fact]
    public async Task AuthPost_WithoutXRequestedWith_IsAllowed()
    {
        // /api/auth/* is exempt — rate-limited separately, no CSRF needed.
        var client = Factory.CreateClient();
        client.DefaultRequestHeaders.Remove("X-Requested-With");

        var res = await client.PostAsJsonAsync("/api/auth/login",
            new { email = "nobody@example.test", password = "wrong" });

        // 401 from the auth controller, not 403 from CSRF middleware.
        Assert.Equal(HttpStatusCode.Unauthorized, res.StatusCode);
    }

    [Fact]
    public async Task Post_WithoutXRequestedWith_Returns403()
    {
        var owner = await RegisterUserAsync();
        // Remove the header that FlowBoardFactory adds by default.
        owner.Http.DefaultRequestHeaders.Remove("X-Requested-With");

        var res = await owner.Http.PostAsJsonAsync("/api/projects", new { name = "Test" });
        Assert.Equal(HttpStatusCode.Forbidden, res.StatusCode);

        var body = await res.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("csrf_validation_failed", body.GetProperty("error").GetString());
    }

    [Fact]
    public async Task Patch_WithoutXRequestedWith_Returns403()
    {
        var owner = await RegisterUserAsync();
        owner.Http.DefaultRequestHeaders.Remove("X-Requested-With");

        // Any guid — we're testing middleware rejection before controller runs.
        var res = await owner.Http.PatchAsJsonAsync(
            $"/api/issues/{Guid.NewGuid()}", new { title = "x" });
        Assert.Equal(HttpStatusCode.Forbidden, res.StatusCode);
    }

    [Fact]
    public async Task Post_WithXRequestedWith_ProceedsToController()
    {
        // This is the normal path: header present → middleware passes through.
        var owner = await RegisterUserAsync();
        // Header already set by FlowBoardFactory.ConfigureClient.
        var res = await owner.Http.PostAsJsonAsync("/api/projects", new { name = "My Project" });
        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
    }
}
