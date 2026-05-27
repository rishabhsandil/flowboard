using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

namespace FlowBoard.Tests.Integration;

/// <summary>
/// End-to-end coverage for the issue_dependencies feature: create / list /
/// delete; the two guards (self-link, cross-project); the 1-hop "blocks"
/// cycle conflict; and the picker search filter.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class IssueDependenciesFlowTests : IntegrationTestBase
{
    public IssueDependenciesFlowTests(PostgresFixture db) : base(db) { }

    [Fact]
    public async Task Create_AddsOutgoingAndIncomingEdges()
    {
        var owner = await RegisterUserAsync();
        var (projectId, sourceId) = await CreateProjectWithIssueAsync(owner, issueTitle: "Source");
        var targetId = await CreateIssueAsync(owner, projectId, "Target");

        var post = await owner.Http.PostAsJsonAsync(
            $"/api/issues/{sourceId}/dependencies",
            new { dependsOnId = targetId, kind = "blocks" });
        Assert.Equal(HttpStatusCode.Created, post.StatusCode);

        // The source sees the edge as outgoing…
        var outgoingSide = await owner.Http.GetFromJsonAsync<JsonElement>(
            $"/api/issues/{sourceId}/dependencies");
        var outgoing = outgoingSide.GetProperty("outgoing").EnumerateArray().Single();
        Assert.Equal("blocks",          outgoing.GetProperty("kind").GetString());
        Assert.Equal(targetId.ToString(), outgoing.GetProperty("otherId").GetString());
        Assert.Empty(outgoingSide.GetProperty("incoming").EnumerateArray());

        // …and the target sees the same edge as incoming.
        var incomingSide = await owner.Http.GetFromJsonAsync<JsonElement>(
            $"/api/issues/{targetId}/dependencies");
        var incoming = incomingSide.GetProperty("incoming").EnumerateArray().Single();
        Assert.Equal("blocks",          incoming.GetProperty("kind").GetString());
        Assert.Equal(sourceId.ToString(), incoming.GetProperty("otherId").GetString());
        Assert.Empty(incomingSide.GetProperty("outgoing").EnumerateArray());
    }

    [Fact]
    public async Task Create_DefaultsKindToBlocks_WhenOmitted()
    {
        var owner = await RegisterUserAsync();
        var (projectId, sourceId) = await CreateProjectWithIssueAsync(owner);
        var targetId = await CreateIssueAsync(owner, projectId, "Target");

        var post = await owner.Http.PostAsJsonAsync(
            $"/api/issues/{sourceId}/dependencies",
            new { dependsOnId = targetId });
        Assert.Equal(HttpStatusCode.Created, post.StatusCode);

        var list = await owner.Http.GetFromJsonAsync<JsonElement>(
            $"/api/issues/{sourceId}/dependencies");
        Assert.Equal("blocks",
            list.GetProperty("outgoing").EnumerateArray().Single()
                .GetProperty("kind").GetString());
    }

    [Fact]
    public async Task Create_RejectsSelfDependency()
    {
        var owner = await RegisterUserAsync();
        var (_, issueId) = await CreateProjectWithIssueAsync(owner);

        var post = await owner.Http.PostAsJsonAsync(
            $"/api/issues/{issueId}/dependencies",
            new { dependsOnId = issueId, kind = "blocks" });
        Assert.Equal(HttpStatusCode.BadRequest, post.StatusCode);
        var body = await post.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("dependency_self", body.GetProperty("error").GetString());
    }

    [Fact]
    public async Task Create_RejectsCrossProjectDependency()
    {
        var owner = await RegisterUserAsync();
        var (_, issueA) = await CreateProjectWithIssueAsync(owner, projectName: "ProjA");
        var (_, issueB) = await CreateProjectWithIssueAsync(owner, projectName: "ProjB");

        var post = await owner.Http.PostAsJsonAsync(
            $"/api/issues/{issueA}/dependencies",
            new { dependsOnId = issueB, kind = "blocks" });
        Assert.Equal(HttpStatusCode.BadRequest, post.StatusCode);
        var body = await post.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("dependency_project_mismatch", body.GetProperty("error").GetString());
    }

    [Fact]
    public async Task Create_RejectsOneHopBlocksCycle()
    {
        var owner = await RegisterUserAsync();
        var (projectId, issueA) = await CreateProjectWithIssueAsync(owner, issueTitle: "A");
        var issueB = await CreateIssueAsync(owner, projectId, "B");

        var first = await owner.Http.PostAsJsonAsync(
            $"/api/issues/{issueA}/dependencies",
            new { dependsOnId = issueB, kind = "blocks" });
        first.EnsureSuccessStatusCode();

        var reverse = await owner.Http.PostAsJsonAsync(
            $"/api/issues/{issueB}/dependencies",
            new { dependsOnId = issueA, kind = "blocks" });
        Assert.Equal(HttpStatusCode.Conflict, reverse.StatusCode);
        var body = await reverse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("dependency_cycle", body.GetProperty("error").GetString());
    }

    [Fact]
    public async Task Create_DoubleAdd_IsIdempotent()
    {
        var owner = await RegisterUserAsync();
        var (projectId, sourceId) = await CreateProjectWithIssueAsync(owner);
        var targetId = await CreateIssueAsync(owner, projectId, "Target");

        await owner.Http.PostAsJsonAsync(
            $"/api/issues/{sourceId}/dependencies",
            new { dependsOnId = targetId, kind = "blocks" });
        var second = await owner.Http.PostAsJsonAsync(
            $"/api/issues/{sourceId}/dependencies",
            new { dependsOnId = targetId, kind = "blocks" });
        Assert.Equal(HttpStatusCode.Created, second.StatusCode);

        var list = await owner.Http.GetFromJsonAsync<JsonElement>(
            $"/api/issues/{sourceId}/dependencies");
        Assert.Single(list.GetProperty("outgoing").EnumerateArray());
    }

    [Fact]
    public async Task Delete_RemovesEdge_AndDeletingIssueCascades()
    {
        var owner = await RegisterUserAsync();
        var (projectId, sourceId) = await CreateProjectWithIssueAsync(owner);
        var targetId = await CreateIssueAsync(owner, projectId, "Target");

        await owner.Http.PostAsJsonAsync(
            $"/api/issues/{sourceId}/dependencies",
            new { dependsOnId = targetId, kind = "blocks" });

        var del = await owner.Http.DeleteAsync(
            $"/api/issues/{sourceId}/dependencies/{targetId}");
        Assert.Equal(HttpStatusCode.NoContent, del.StatusCode);

        var afterDelete = await owner.Http.GetFromJsonAsync<JsonElement>(
            $"/api/issues/{sourceId}/dependencies");
        Assert.Empty(afterDelete.GetProperty("outgoing").EnumerateArray());

        // Re-add then delete the *other* issue — the row should be wiped
        // by ON DELETE CASCADE on either FK.
        await owner.Http.PostAsJsonAsync(
            $"/api/issues/{sourceId}/dependencies",
            new { dependsOnId = targetId, kind = "blocks" });
        await owner.Http.DeleteAsync($"/api/issues/{targetId}");

        var afterCascade = await owner.Http.GetFromJsonAsync<JsonElement>(
            $"/api/issues/{sourceId}/dependencies");
        Assert.Empty(afterCascade.GetProperty("outgoing").EnumerateArray());
    }

    [Fact]
    public async Task NonMember_CannotReadOrWriteDependencies()
    {
        var owner    = await RegisterUserAsync();
        var outsider = await RegisterUserAsync();
        var (projectId, sourceId) = await CreateProjectWithIssueAsync(owner);
        var targetId = await CreateIssueAsync(owner, projectId, "Target");

        var listAsOutsider = await outsider.Http.GetAsync($"/api/issues/{sourceId}/dependencies");
        Assert.Equal(HttpStatusCode.Forbidden, listAsOutsider.StatusCode);

        var postAsOutsider = await outsider.Http.PostAsJsonAsync(
            $"/api/issues/{sourceId}/dependencies",
            new { dependsOnId = targetId, kind = "blocks" });
        Assert.Equal(HttpStatusCode.Forbidden, postAsOutsider.StatusCode);
    }

    [Fact]
    public async Task Search_FiltersByTitle_AndExcludesSelf()
    {
        var owner = await RegisterUserAsync();
        var (projectId, sourceId) = await CreateProjectWithIssueAsync(
            owner, issueTitle: "Migrate auth to JWT");
        await CreateIssueAsync(owner, projectId, "Add password reset");
        await CreateIssueAsync(owner, projectId, "Refactor JWT signing");

        var res = await owner.Http.GetFromJsonAsync<JsonElement>(
            $"/api/issues/{sourceId}/dependencies/search?q=jwt");
        var items = res.GetProperty("items").EnumerateArray().ToList();

        Assert.Single(items);
        Assert.Equal("Refactor JWT signing", items[0].GetProperty("title").GetString());
    }

    [Fact]
    public async Task DependencyAdd_LogsActivity()
    {
        var owner = await RegisterUserAsync();
        var (projectId, sourceId) = await CreateProjectWithIssueAsync(owner);
        var targetId = await CreateIssueAsync(owner, projectId, "Target");

        await owner.Http.PostAsJsonAsync(
            $"/api/issues/{sourceId}/dependencies",
            new { dependsOnId = targetId, kind = "blocks" });

        var feed = await owner.Http.GetFromJsonAsync<JsonElement>(
            $"/api/projects/{projectId}/activity");
        var added = feed.GetProperty("items").EnumerateArray()
            .Single(a => a.GetProperty("type").GetString() == "dependency_added");
        Assert.Equal(sourceId.ToString(), added.GetProperty("issueId").GetString());
    }

    private async Task<Guid> CreateIssueAsync(AuthedClient owner, Guid projectId, string title)
    {
        var res = await owner.Http.PostAsJsonAsync(
            $"/api/projects/{projectId}/issues", new { title });
        res.EnsureSuccessStatusCode();
        var body = await res.Content.ReadFromJsonAsync<JsonElement>();
        return Guid.Parse(body.GetProperty("issue").GetProperty("id").GetString()!);
    }
}
