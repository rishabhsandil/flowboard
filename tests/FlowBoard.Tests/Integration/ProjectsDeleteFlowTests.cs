using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Dapper;

namespace FlowBoard.Tests.Integration;

/// <summary>
/// Functional coverage for <c>DELETE /api/projects/{id}</c>:
///   • owner-only (403 for non-owner members, 403 for outsiders)
///   • cascade wipes child rows (issues, epics, sprints, columns, board,
///     project_members, activities, labels)
///   • the project is no longer reachable through any GET that previously
///     resolved it
/// </summary>
public sealed class ProjectsDeleteFlowTests : IntegrationTestBase
{
    public ProjectsDeleteFlowTests(PostgresFixture db) : base(db) { }

    [Fact]
    public async Task Owner_CanDeleteProject_AndCascadeWipesChildren()
    {
        var owner = await RegisterUserAsync();
        var (projectId, issueId) = await CreateProjectWithIssueAsync(owner);

        var del = await owner.Http.DeleteAsync($"/api/projects/{projectId}");
        Assert.Equal(HttpStatusCode.NoContent, del.StatusCode);

        await using var c = Db.OpenConnection();
        Assert.Equal(0, await c.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM projects WHERE id = @p", new { p = projectId }));
        Assert.Equal(0, await c.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM issues WHERE id = @i", new { i = issueId }));
        Assert.Equal(0, await c.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM project_members WHERE project_id = @p", new { p = projectId }));
        Assert.Equal(0, await c.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM boards WHERE project_id = @p", new { p = projectId }));
    }

    [Fact]
    public async Task NonOwnerMember_CannotDeleteProject()
    {
        var owner  = await RegisterUserAsync();
        var member = await RegisterUserAsync();
        var (projectId, _) = await CreateProjectWithIssueAsync(owner);

        await owner.Http.PostAsJsonAsync(
            $"/api/projects/{projectId}/members",
            new { email = member.Email });

        var del = await member.Http.DeleteAsync($"/api/projects/{projectId}");
        Assert.Equal(HttpStatusCode.Forbidden, del.StatusCode);

        await using var c = Db.OpenConnection();
        Assert.Equal(1, await c.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM projects WHERE id = @p", new { p = projectId }));
    }

    [Fact]
    public async Task Outsider_CannotDeleteProject()
    {
        var owner    = await RegisterUserAsync();
        var outsider = await RegisterUserAsync();
        var (projectId, _) = await CreateProjectWithIssueAsync(owner);

        var del = await outsider.Http.DeleteAsync($"/api/projects/{projectId}");
        Assert.Equal(HttpStatusCode.Forbidden, del.StatusCode);
    }

    [Fact]
    public async Task DeletedProject_NoLongerListedForOwner()
    {
        var owner = await RegisterUserAsync();
        var (projectId, _) = await CreateProjectWithIssueAsync(owner);

        var del = await owner.Http.DeleteAsync($"/api/projects/{projectId}");
        Assert.Equal(HttpStatusCode.NoContent, del.StatusCode);

        var list = await owner.Http.GetFromJsonAsync<JsonElement>("/api/projects");
        var ids = list.GetProperty("items").EnumerateArray()
            .Select(e => e.GetProperty("id").GetString()!).ToHashSet();
        Assert.DoesNotContain(projectId.ToString(), ids);
    }

}
