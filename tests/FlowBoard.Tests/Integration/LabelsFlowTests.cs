using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace FlowBoard.Tests.Integration;

/// <summary>
/// Functional coverage for the ZenHub-style label feature: project-scoped
/// catalogue, attach/detach to issues, cross-project guard, duplicate name
/// 409, and surfacing on the board view.
/// </summary>
public sealed class LabelsFlowTests : IntegrationTestBase
{
    public LabelsFlowTests(PostgresFixture db) : base(db) { }

    [Fact]
    public async Task CreateLabel_ReturnsItAndShowsInList()
    {
        var owner = await RegisterUserAsync();
        var (projectId, _) = await CreateProjectWithIssueAsync(owner);

        var post = await owner.Http.PostAsJsonAsync(
            $"/api/projects/{projectId}/labels", new { name = "Bug", color = "#ff0000" });
        Assert.Equal(HttpStatusCode.Created, post.StatusCode);

        var list = await owner.Http.GetFromJsonAsync<JsonElement>($"/api/projects/{projectId}/labels");
        var item = list.GetProperty("items").EnumerateArray().Single();
        Assert.Equal("Bug",     item.GetProperty("name").GetString());
        Assert.Equal("#ff0000", item.GetProperty("color").GetString());
    }

    [Fact]
    public async Task DuplicateName_CaseInsensitive_Returns409()
    {
        var owner = await RegisterUserAsync();
        var (projectId, _) = await CreateProjectWithIssueAsync(owner);

        var first = await owner.Http.PostAsJsonAsync(
            $"/api/projects/{projectId}/labels", new { name = "Bug" });
        first.EnsureSuccessStatusCode();

        // Different casing must still trip the unique index.
        var dup = await owner.Http.PostAsJsonAsync(
            $"/api/projects/{projectId}/labels", new { name = "BUG" });
        Assert.Equal(HttpStatusCode.Conflict, dup.StatusCode);
        var body = await dup.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("label_name_exists", body.GetProperty("error").GetString());
    }

    [Fact]
    public async Task InvalidColorFormat_RejectedByValidator()
    {
        var owner = await RegisterUserAsync();
        var (projectId, _) = await CreateProjectWithIssueAsync(owner);

        var bad = await owner.Http.PostAsJsonAsync(
            $"/api/projects/{projectId}/labels", new { name = "Bug", color = "not-a-color" });
        Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);
    }

    [Fact]
    public async Task AttachAndDetachLabel_RoundTripsThroughIssue()
    {
        var owner = await RegisterUserAsync();
        var (projectId, issueId) = await CreateProjectWithIssueAsync(owner);

        var label = await CreateLabelAsync(owner, projectId, "Backend", "#3366ff");

        var attach = await owner.Http.PostAsJsonAsync(
            $"/api/issues/{issueId}/labels", new { labelId = label });
        Assert.Equal(HttpStatusCode.NoContent, attach.StatusCode);

        var listed = await owner.Http.GetFromJsonAsync<JsonElement>(
            $"/api/issues/{issueId}/labels");
        Assert.Single(listed.GetProperty("items").EnumerateArray());

        var detach = await owner.Http.DeleteAsync($"/api/issues/{issueId}/labels/{label}");
        Assert.Equal(HttpStatusCode.NoContent, detach.StatusCode);

        var afterDetach = await owner.Http.GetFromJsonAsync<JsonElement>(
            $"/api/issues/{issueId}/labels");
        Assert.Empty(afterDetach.GetProperty("items").EnumerateArray());
    }

    [Fact]
    public async Task DoubleAttach_IsIdempotent_NoDuplicateRows()
    {
        var owner = await RegisterUserAsync();
        var (projectId, issueId) = await CreateProjectWithIssueAsync(owner);
        var label = await CreateLabelAsync(owner, projectId, "Tech Debt");

        // Attach twice; ON CONFLICT DO NOTHING means the join row is unique.
        await owner.Http.PostAsJsonAsync($"/api/issues/{issueId}/labels", new { labelId = label });
        var second = await owner.Http.PostAsJsonAsync($"/api/issues/{issueId}/labels", new { labelId = label });
        Assert.Equal(HttpStatusCode.NoContent, second.StatusCode);

        var listed = await owner.Http.GetFromJsonAsync<JsonElement>(
            $"/api/issues/{issueId}/labels");
        Assert.Single(listed.GetProperty("items").EnumerateArray());
    }

    [Fact]
    public async Task CrossProjectAttach_IsRejected()
    {
        var owner = await RegisterUserAsync();
        // Two separate projects owned by the same user.
        var (projectA, issueA) = await CreateProjectWithIssueAsync(owner, projectName: "ProjA");
        var (projectB, _)      = await CreateProjectWithIssueAsync(owner, projectName: "ProjB");

        var labelB = await CreateLabelAsync(owner, projectB, "Wrong");

        var bad = await owner.Http.PostAsJsonAsync(
            $"/api/issues/{issueA}/labels", new { labelId = labelB });
        Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);
        var body = await bad.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("label_project_mismatch", body.GetProperty("error").GetString());
    }

    [Fact]
    public async Task BoardView_IncludesLabelsArrayPerIssue()
    {
        var owner = await RegisterUserAsync();
        var (projectId, issueId) = await CreateProjectWithIssueAsync(owner, projectName: "Boarded");

        var l1 = await CreateLabelAsync(owner, projectId, "alpha", "#111111");
        var l2 = await CreateLabelAsync(owner, projectId, "Beta",  "#222222");
        await owner.Http.PostAsJsonAsync($"/api/issues/{issueId}/labels", new { labelId = l1 });
        await owner.Http.PostAsJsonAsync($"/api/issues/{issueId}/labels", new { labelId = l2 });

        var board = await owner.Http.GetFromJsonAsync<JsonElement>(
            $"/api/projects/{projectId}/board");

        // The board groups issues by column; flatten and find ours.
        var allIssues = board.GetProperty("columns").EnumerateArray()
            .SelectMany(col => col.GetProperty("issues").EnumerateArray())
            .ToList();
        var match = allIssues.Single(i => i.GetProperty("id").GetString() == issueId.ToString());

        var labels = match.GetProperty("labels").EnumerateArray()
            .Select(l => l.GetProperty("name").GetString()).ToList();
        // Server orders by LOWER(name): "alpha" then "Beta".
        Assert.Equal(new[] { "alpha", "Beta" }, labels);
    }

    [Fact]
    public async Task UpdateLabel_PartialPatch_PreservesUntouchedFields()
    {
        var owner = await RegisterUserAsync();
        var (projectId, _) = await CreateProjectWithIssueAsync(owner);
        var labelId = await CreateLabelAsync(owner, projectId, "Old", "#abcdef");

        // Patch only the color — name must survive.
        var patch = await owner.Http.PatchAsJsonAsync($"/api/labels/{labelId}", new { color = "#fedcba" });
        patch.EnsureSuccessStatusCode();
        var json = await patch.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Old",     json.GetProperty("label").GetProperty("name").GetString());
        Assert.Equal("#fedcba", json.GetProperty("label").GetProperty("color").GetString());
    }

    private async Task<Guid> CreateLabelAsync(AuthedClient owner, Guid projectId, string name, string? color = null)
    {
        var res = await owner.Http.PostAsJsonAsync(
            $"/api/projects/{projectId}/labels",
            color is null ? new { name } : (object)new { name, color });
        res.EnsureSuccessStatusCode();
        var body = await res.Content.ReadFromJsonAsync<JsonElement>();
        return Guid.Parse(body.GetProperty("label").GetProperty("id").GetString()!);
    }
}
