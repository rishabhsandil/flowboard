using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FlowBoard.Core.Models;
using Xunit;

namespace FlowBoard.Tests.Integration;

[Collection(PostgresCollection.Name)]
public sealed class SubIssueFlowTests : IntegrationTestBase
{
    public SubIssueFlowTests(PostgresFixture db) : base(db) { }

    [Fact]
    public async Task List_IncludesParentId()
    {
        var owner = await RegisterUserAsync();
        var (projectId, parentId) = await CreateProjectWithIssueAsync(owner, issueTitle: "Parent");
        var childId = await CreateIssueAsync(owner, projectId, "Child", parentId: parentId);

        var res = await owner.Http.GetAsync($"/api/projects/{projectId}/issues?showSubIssues=true");
        res.EnsureSuccessStatusCode();
        var body = await res.Content.ReadFromJsonAsync<JsonElement>();
        var items = body.GetProperty("items").EnumerateArray().ToList();

        var childRow = items.First(i => Guid.Parse(i.GetProperty("id").GetString()!) == childId);
        Assert.Equal(parentId, Guid.Parse(childRow.GetProperty("parentId").GetString()!));
    }

    [Fact]
    public async Task Board_IncludesParentIdAndChildCount()
    {
        var owner = await RegisterUserAsync();
        var (projectId, parentId) = await CreateProjectWithIssueAsync(owner, issueTitle: "Parent");
        await CreateIssueAsync(owner, projectId, "Child", parentId: parentId);

        var res = await owner.Http.GetAsync($"/api/projects/{projectId}/board");
        res.EnsureSuccessStatusCode();
        var body = await res.Content.ReadFromJsonAsync<JsonElement>();
        var columns = body.GetProperty("columns").EnumerateArray().ToList();
        
        var allIssues = columns.SelectMany(c => c.GetProperty("issues").EnumerateArray()).ToList();
        var parentIssue = allIssues.First(i => Guid.Parse(i.GetProperty("id").GetString()!) == parentId);

        Assert.Equal(1, parentIssue.GetProperty("child_count").GetInt32());
    }

    [Fact]
    public async Task CrossProjectParent_IsBlocked()
    {
        var owner = await RegisterUserAsync();
        var (projectA, issueA) = await CreateProjectWithIssueAsync(owner, issueTitle: "Project A Issue");
        var (projectB, issueB) = await CreateProjectWithIssueAsync(owner, issueTitle: "Project B Issue");

        // Try to set issueA as parent of issueB
        var patchRes = await owner.Http.PatchAsJsonAsync($"/api/issues/{issueB}", new { parentId = issueA });
        
        Assert.Equal(HttpStatusCode.BadRequest, patchRes.StatusCode);
        var errorBody = await patchRes.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("cross_project_parent", errorBody.GetProperty("error").GetString());
    }

    [Fact]
    public async Task SelfParenting_IsBlocked()
    {
        var owner = await RegisterUserAsync();
        var (projectId, issueId) = await CreateProjectWithIssueAsync(owner);

        var patchRes = await owner.Http.PatchAsJsonAsync($"/api/issues/{issueId}", new { parentId = issueId });
        Assert.Equal(HttpStatusCode.BadRequest, patchRes.StatusCode);
        var errorBody = await patchRes.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("self_parenting", errorBody.GetProperty("error").GetString());
    }

    [Fact]
    public async Task CircularParenting_IsBlocked()
    {
        var owner = await RegisterUserAsync();
        var (projectId, issueA) = await CreateProjectWithIssueAsync(owner, issueTitle: "A");
        var issueB = await CreateIssueAsync(owner, projectId, "B", parentId: issueA);

        var patchRes = await owner.Http.PatchAsJsonAsync($"/api/issues/{issueA}", new { parentId = issueB });
        Assert.Equal(HttpStatusCode.BadRequest, patchRes.StatusCode);
        var errorBody = await patchRes.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("circular_dependency", errorBody.GetProperty("error").GetString());
    }

    [Fact]
    public async Task StoryPointRollup_Works()
    {
        var owner = await RegisterUserAsync();
        var (projectId, parentId) = await CreateProjectWithIssueAsync(owner, issueTitle: "Parent");
        
        // Add children with points
        await CreateIssueAsync(owner, projectId, "Child 1", parentId: parentId, storyPoints: 5);
        await CreateIssueAsync(owner, projectId, "Child 2", parentId: parentId, storyPoints: 3);

        var res = await owner.Http.GetAsync($"/api/issues/{parentId}");
        var body = await res.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(8, body.GetProperty("issue").GetProperty("storyPoints").GetInt32());

        // Update a child
        var childId = await CreateIssueAsync(owner, projectId, "Child 3", parentId: parentId, storyPoints: 2);
        await owner.Http.PatchAsJsonAsync($"/api/issues/{childId}", new { storyPoints = 10 });

        var res2 = await owner.Http.GetAsync($"/api/issues/{parentId}");
        var body2 = await res2.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(18, body2.GetProperty("issue").GetProperty("storyPoints").GetInt32());

        // Delete a child
        await owner.Http.DeleteAsync($"/api/issues/{childId}");
        var res3 = await owner.Http.GetAsync($"/api/issues/{parentId}");
        var body3 = await res3.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(8, body3.GetProperty("issue").GetProperty("storyPoints").GetInt32());
    }

    [Fact]
    public async Task StatusSync_ParentToChildren_Works()
    {
        var owner = await RegisterUserAsync();
        var (projectId, parentId) = await CreateProjectWithIssueAsync(owner, issueTitle: "Parent");
        var childId = await CreateIssueAsync(owner, projectId, "Child", parentId: parentId);

        // Close parent
        await owner.Http.PatchAsJsonAsync($"/api/issues/{parentId}", new { closed = true });

        // Check child
        var res = await owner.Http.GetAsync($"/api/issues/{childId}");
        var body = await res.Content.ReadFromJsonAsync<JsonElement>();
        Assert.NotNull(body.GetProperty("issue").GetProperty("closedAt").GetString());

        // Reopen parent
        await owner.Http.PatchAsJsonAsync($"/api/issues/{parentId}", new { closed = false });

        // Check child
        var res2 = await owner.Http.GetAsync($"/api/issues/{childId}");
        var body2 = await res2.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(JsonValueKind.Null, body2.GetProperty("issue").GetProperty("closedAt").ValueKind);
    }

    private async Task<Guid> CreateIssueAsync(
        AuthedClient owner, Guid projectId, string title, Guid? parentId = null, int storyPoints = 0)
    {
        var payload = new Dictionary<string, object?>
        {
            ["title"] = title,
            ["parentId"] = parentId,
            ["storyPoints"] = storyPoints
        };

        var res = await owner.Http.PostAsJsonAsync($"/api/projects/{projectId}/issues", payload);
        res.EnsureSuccessStatusCode();
        var body = await res.Content.ReadFromJsonAsync<JsonElement>();
        return Guid.Parse(body.GetProperty("issue").GetProperty("id").GetString()!);
    }
}
