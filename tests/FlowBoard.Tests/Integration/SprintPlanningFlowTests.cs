using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace FlowBoard.Tests.Integration;

/// <summary>
/// Integration tests for the sprint planning drag-handoff flow.
/// The planning page uses existing issue endpoints; these tests verify
/// the underlying API operations that the frontend calls during a drag.
/// </summary>
public sealed class SprintPlanningFlowTests : IntegrationTestBase
{
    private static readonly Guid EmptyGuid = Guid.Empty;

    public SprintPlanningFlowTests(PostgresFixture db) : base(db) { }

    [Fact]
    public async Task BacklogQuery_ReturnsIssuesWithNoSprint()
    {
        var owner = await RegisterUserAsync();
        var (projectId, issueId) = await CreateProjectWithIssueAsync(owner);

        // The issue was created without a sprint — it should appear in the backlog filter.
        var res = await owner.Http.GetAsync(
            $"/api/projects/{projectId}/issues?sprintId={EmptyGuid}");
        res.EnsureSuccessStatusCode();

        var body = await res.Content.ReadFromJsonAsync<JsonElement>();
        var ids = body.GetProperty("items").EnumerateArray()
            .Select(i => i.GetProperty("id").GetString())
            .ToList();

        Assert.Contains(issueId.ToString(), ids);
    }

    [Fact]
    public async Task DragToSprint_AssignsSprintId()
    {
        var owner = await RegisterUserAsync();
        var (projectId, issueId) = await CreateProjectWithIssueAsync(owner);

        // Create a sprint
        var sprintRes = await owner.Http.PostAsJsonAsync(
            $"/api/projects/{projectId}/sprints",
            new { name = "Sprint 1", startDate = "2026-06-01", endDate = "2026-06-14" });
        sprintRes.EnsureSuccessStatusCode();
        var sprintBody = await sprintRes.Content.ReadFromJsonAsync<JsonElement>();
        var sprintId = sprintBody.GetProperty("sprint").GetProperty("id").GetString()!;

        // Simulate drag: PATCH issue to assign sprint
        var patchRes = await owner.Http.PatchAsJsonAsync(
            $"/api/issues/{issueId}", new { sprintId });
        Assert.Equal(HttpStatusCode.OK, patchRes.StatusCode);

        // Verify it no longer appears in the backlog filter
        var backlog = await owner.Http.GetAsync(
            $"/api/projects/{projectId}/issues?sprintId={EmptyGuid}");
        var backlogIds = (await backlog.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("items").EnumerateArray()
            .Select(i => i.GetProperty("id").GetString())
            .ToList();
        Assert.DoesNotContain(issueId.ToString(), backlogIds);

        // And appears in the sprint filter
        var sprintIssues = await owner.Http.GetAsync(
            $"/api/projects/{projectId}/issues?sprintId={sprintId}");
        var sprintIds = (await sprintIssues.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("items").EnumerateArray()
            .Select(i => i.GetProperty("id").GetString())
            .ToList();
        Assert.Contains(issueId.ToString(), sprintIds);
    }

    [Fact]
    public async Task DragBackToBacklog_ClearsSprintId()
    {
        var owner = await RegisterUserAsync();
        var (projectId, issueId) = await CreateProjectWithIssueAsync(owner);

        // Create sprint and assign issue
        var sprintRes = await owner.Http.PostAsJsonAsync(
            $"/api/projects/{projectId}/sprints",
            new { name = "Sprint 1", startDate = "2026-06-01", endDate = "2026-06-14" });
        var sprintId = (await sprintRes.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("sprint").GetProperty("id").GetString()!;

        await owner.Http.PatchAsJsonAsync($"/api/issues/{issueId}", new { sprintId });

        // Simulate drag back: clear sprint via empty-guid sentinel
        var clearRes = await owner.Http.PatchAsJsonAsync(
            $"/api/issues/{issueId}", new { sprintId = EmptyGuid.ToString() });
        Assert.Equal(HttpStatusCode.OK, clearRes.StatusCode);

        // Issue must reappear in backlog filter
        var backlog = await owner.Http.GetAsync(
            $"/api/projects/{projectId}/issues?sprintId={EmptyGuid}");
        var backlogIds = (await backlog.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("items").EnumerateArray()
            .Select(i => i.GetProperty("id").GetString())
            .ToList();
        Assert.Contains(issueId.ToString(), backlogIds);
    }
}
