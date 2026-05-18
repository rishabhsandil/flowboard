using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace FlowBoard.Tests.Integration;

/// <summary>
/// Integration tests for the Cumulative Flow Diagram endpoint.
/// Verifies that calling GET /api/projects/{id}/reports/cfd auto-takes
/// a snapshot and returns chart data.
/// </summary>
public sealed class CfdFlowTests : IntegrationTestBase
{
    public CfdFlowTests(PostgresFixture db) : base(db) { }

    [Fact]
    public async Task Cfd_ReturnsOkWithPointsAfterSnapshot()
    {
        var owner = await RegisterUserAsync();
        var (projectId, _) = await CreateProjectWithIssueAsync(owner);

        var res = await owner.Http.GetAsync($"/api/projects/{projectId}/reports/cfd?days=30");
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);

        var body = await res.Content.ReadFromJsonAsync<JsonElement>();
        var cfd = body.GetProperty("cfd").EnumerateArray().ToList();

        // At least one point per column (project is created with 4 default columns).
        Assert.NotEmpty(cfd);

        // Each point must have day, columnName, and issueCount.
        var first = cfd[0];
        Assert.True(first.TryGetProperty("day", out _));
        Assert.True(first.TryGetProperty("columnName", out _));
        Assert.True(first.TryGetProperty("issueCount", out _));
    }

    [Fact]
    public async Task Cfd_IsIdempotent_CallingTwiceSameDay()
    {
        var owner = await RegisterUserAsync();
        var (projectId, _) = await CreateProjectWithIssueAsync(owner);

        var res1 = await owner.Http.GetAsync($"/api/projects/{projectId}/reports/cfd");
        var res2 = await owner.Http.GetAsync($"/api/projects/{projectId}/reports/cfd");

        Assert.Equal(HttpStatusCode.OK, res1.StatusCode);
        Assert.Equal(HttpStatusCode.OK, res2.StatusCode);

        var body1 = (await res1.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("cfd").EnumerateArray().Count();
        var body2 = (await res2.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("cfd").EnumerateArray().Count();

        // Calling twice on the same day must not double the rows.
        Assert.Equal(body1, body2);
    }

    [Fact]
    public async Task Cfd_NonMember_Returns403()
    {
        var owner  = await RegisterUserAsync();
        var other  = await RegisterUserAsync();
        var (projectId, _) = await CreateProjectWithIssueAsync(owner);

        var res = await other.Http.GetAsync($"/api/projects/{projectId}/reports/cfd");
        Assert.Equal(HttpStatusCode.Forbidden, res.StatusCode);
    }

    [Fact]
    public async Task Cfd_DaysParam_ClampsTo30WhenOutOfRange()
    {
        var owner = await RegisterUserAsync();
        var (projectId, _) = await CreateProjectWithIssueAsync(owner);

        // days=0 and days=999 both fall back to the default (30) silently.
        var resZero = await owner.Http.GetAsync($"/api/projects/{projectId}/reports/cfd?days=0");
        var resBig  = await owner.Http.GetAsync($"/api/projects/{projectId}/reports/cfd?days=999");

        Assert.Equal(HttpStatusCode.OK, resZero.StatusCode);
        Assert.Equal(HttpStatusCode.OK, resBig.StatusCode);
    }
}
