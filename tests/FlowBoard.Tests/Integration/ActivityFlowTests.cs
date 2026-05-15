using System.Net.Http.Json;
using System.Text.Json;

namespace FlowBoard.Tests.Integration;

/// <summary>
/// Verifies the activity log emits the right event types, in the right
/// order, with the expected payload fields. Drives the API end-to-end so
/// we also exercise <see cref="FlowBoard.Api.Activity.ActivityLogger"/>'s
/// JSONB serialization path.
/// </summary>
public sealed class ActivityFlowTests : IntegrationTestBase
{
    public ActivityFlowTests(PostgresFixture db) : base(db) { }

    [Fact]
    public async Task IssueLifecycle_LogsCreated_Updated_Closed_Reopened_BeforeDeletion()
    {
        // We split this from the delete case because
        // `activities.issue_id REFERENCES issues(id) ON DELETE CASCADE`
        // wipes the issue's history on delete — only the post-delete
        // "issue_deleted" row (logged with issue_id = NULL against the
        // project) survives. Verifying both pieces in one test would
        // tangle two product decisions; the deletion case has its own test.
        var owner = await RegisterUserAsync();
        var (_, issueId) = await CreateProjectWithIssueAsync(owner);

        await owner.Http.PatchAsJsonAsync($"/api/issues/{issueId}", new { title = "Renamed" });
        await owner.Http.PatchAsJsonAsync($"/api/issues/{issueId}", new { closed = true });
        await owner.Http.PatchAsJsonAsync($"/api/issues/{issueId}", new { closed = false });

        var feed = await owner.Http.GetFromJsonAsync<JsonElement>(
            $"/api/issues/{issueId}/activity?take=50");
        var types = feed.GetProperty("items").EnumerateArray()
            .Select(i => i.GetProperty("type").GetString())
            .ToList();

        // Order is DESC (newest first).
        Assert.Equal(
            new[] { "issue_reopened", "issue_closed", "issue_updated", "issue_created" },
            types);
    }

    [Fact]
    public async Task DeletingIssue_LeavesIssueDeletedRowOnProjectFeed_WithNullIssueId()
    {
        var owner = await RegisterUserAsync();
        var projRes = await (await owner.Http.PostAsJsonAsync(
            "/api/projects", new { name = "Bin" })).Content.ReadFromJsonAsync<JsonElement>();
        var projectId = Guid.Parse(projRes.GetProperty("project").GetProperty("id").GetString()!);

        var issueRes = await (await owner.Http.PostAsJsonAsync(
            $"/api/projects/{projectId}/issues", new { title = "Doomed" }))
            .Content.ReadFromJsonAsync<JsonElement>();
        var issueId = Guid.Parse(issueRes.GetProperty("issue").GetProperty("id").GetString()!);

        await owner.Http.DeleteAsync($"/api/issues/{issueId}");

        var feed = await owner.Http.GetFromJsonAsync<JsonElement>(
            $"/api/projects/{projectId}/activity?take=50");
        var rows = feed.GetProperty("items").EnumerateArray().ToList();

        // CASCADE deleted issue_created. Only issue_deleted (issue_id NULL) remains.
        Assert.Single(rows);
        Assert.Equal("issue_deleted", rows[0].GetProperty("type").GetString());
        Assert.Equal(JsonValueKind.Null, rows[0].GetProperty("issueId").ValueKind);

        // The deleted issue id is preserved inside the JSONB payload.
        var payload = JsonDocument.Parse(rows[0].GetProperty("payload").GetString()!).RootElement;
        Assert.Equal(issueId.ToString(), payload.GetProperty("issueId").GetString());
    }

    [Fact]
    public async Task IssueUpdate_OnlyLogsChangedFields_InPayload()
    {
        var owner = await RegisterUserAsync();
        var (projectId, issueId) = await CreateProjectWithIssueAsync(owner);

        await owner.Http.PatchAsJsonAsync($"/api/issues/{issueId}",
            new { priority = "high", storyPoints = 5 });

        var feed = await owner.Http.GetFromJsonAsync<JsonElement>(
            $"/api/issues/{issueId}/activity");
        var updateRow = feed.GetProperty("items").EnumerateArray()
            .First(i => i.GetProperty("type").GetString() == "issue_updated");

        var payload = JsonDocument.Parse(updateRow.GetProperty("payload").GetString()!).RootElement;
        var changedFields = payload.GetProperty("changes").EnumerateArray()
            .Select(c => c.GetProperty("field").GetString()).ToHashSet();

        Assert.Contains("priority",    changedFields);
        Assert.Contains("storyPoints", changedFields);
        Assert.DoesNotContain("title", changedFields); // unchanged → must NOT appear
    }

    [Fact]
    public async Task CommentLifecycle_EmitsAddedEditedDeletedAndMention()
    {
        var owner  = await RegisterUserAsync(name: "Alice Wonder");
        var bob    = await RegisterUserAsync(name: "Bob Builder");
        var (projectId, issueId) = await CreateProjectWithIssueAsync(owner);
        await AddProjectMemberAsync(projectId, bob.UserId);

        // Comment WITH a mention → expect comment_added + mention.
        var created = await (await owner.Http.PostAsJsonAsync(
            $"/api/issues/{issueId}/comments",
            new { body = "hi @bobbuilder" }))
            .Content.ReadFromJsonAsync<JsonElement>();
        var commentId = Guid.Parse(created.GetProperty("comment").GetProperty("id").GetString()!);

        await owner.Http.PatchAsJsonAsync($"/api/comments/{commentId}", new { body = "hi @bobbuilder!!" });
        await owner.Http.DeleteAsync($"/api/comments/{commentId}");

        var feed = await owner.Http.GetFromJsonAsync<JsonElement>(
            $"/api/issues/{issueId}/activity?take=50");
        var types = feed.GetProperty("items").EnumerateArray()
            .Select(i => i.GetProperty("type").GetString())
            .ToList();

        Assert.Contains("comment_added",   types);
        Assert.Contains("comment_edited",  types);
        Assert.Contains("comment_deleted", types);
        Assert.Contains("mention",         types);
        Assert.Contains("issue_created",   types);
    }

    [Fact]
    public async Task LabelAttachDetach_LogsLabelAddedAndLabelRemoved()
    {
        var owner = await RegisterUserAsync();
        var (projectId, issueId) = await CreateProjectWithIssueAsync(owner);

        var labelRes = await (await owner.Http.PostAsJsonAsync(
            $"/api/projects/{projectId}/labels", new { name = "Tracked" }))
            .Content.ReadFromJsonAsync<JsonElement>();
        var labelId = Guid.Parse(labelRes.GetProperty("label").GetProperty("id").GetString()!);

        await owner.Http.PostAsJsonAsync($"/api/issues/{issueId}/labels", new { labelId });
        await owner.Http.DeleteAsync($"/api/issues/{issueId}/labels/{labelId}");

        var feed = await owner.Http.GetFromJsonAsync<JsonElement>(
            $"/api/issues/{issueId}/activity");
        var types = feed.GetProperty("items").EnumerateArray()
            .Select(i => i.GetProperty("type").GetString()).ToList();

        Assert.Contains("label_added",   types);
        Assert.Contains("label_removed", types);
    }

    [Fact]
    public async Task ActivityFeed_RespectsPagination()
    {
        var owner = await RegisterUserAsync();
        var (projectId, issueId) = await CreateProjectWithIssueAsync(owner);

        // Generate a stream of comments to populate the feed.
        for (var i = 0; i < 7; i++)
        {
            await owner.Http.PostAsJsonAsync(
                $"/api/issues/{issueId}/comments", new { body = $"msg {i}" });
        }

        var page1 = await owner.Http.GetFromJsonAsync<JsonElement>(
            $"/api/issues/{issueId}/activity?skip=0&take=3");
        var page2 = await owner.Http.GetFromJsonAsync<JsonElement>(
            $"/api/issues/{issueId}/activity?skip=3&take=3");

        var ids1 = page1.GetProperty("items").EnumerateArray()
            .Select(i => i.GetProperty("id").GetString()).ToHashSet();
        var ids2 = page2.GetProperty("items").EnumerateArray()
            .Select(i => i.GetProperty("id").GetString()).ToHashSet();

        Assert.Equal(3, ids1.Count);
        Assert.Equal(3, ids2.Count);
        Assert.Empty(ids1.Intersect(ids2)); // disjoint pages
    }

    [Fact]
    public async Task ActivityPayload_IsValidJsonString_FromJsonbColumn()
    {
        // This guards the @Payload::jsonb cast + payload::text projection:
        // a regression there would either reject the INSERT or hand the
        // client raw JSON instead of a string.
        var owner = await RegisterUserAsync();
        var (_, issueId) = await CreateProjectWithIssueAsync(owner);

        var feed = await owner.Http.GetFromJsonAsync<JsonElement>(
            $"/api/issues/{issueId}/activity");
        var raw = feed.GetProperty("items").EnumerateArray()
            .First().GetProperty("payload").GetString();

        Assert.False(string.IsNullOrEmpty(raw));
        // Round-trips through System.Text.Json without throwing.
        using var parsed = JsonDocument.Parse(raw!);
        Assert.Equal(JsonValueKind.Object, parsed.RootElement.ValueKind);
    }
}
