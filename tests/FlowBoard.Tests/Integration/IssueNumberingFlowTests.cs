using System.Net.Http.Json;
using System.Text.Json;
using Dapper;

namespace FlowBoard.Tests.Integration;

/// <summary>
/// Functional coverage for per-project issue numbering. Exercises the
/// assign_issue_number() trigger end-to-end through the HTTP boundary:
/// sequential allocation, per-project independence, the explicit-number
/// ratchet that the schema.sql backfill relies on, and the contract that the
/// number is surfaced on the read paths the UI uses.
/// </summary>
public sealed class IssueNumberingFlowTests : IntegrationTestBase
{
    public IssueNumberingFlowTests(PostgresFixture db) : base(db) { }

    [Fact]
    public async Task Issues_AreNumberedSequentially_StartingAtOne()
    {
        var owner = await RegisterUserAsync();
        var (projectId, firstId) = await CreateProjectWithIssueAsync(owner);

        Assert.Equal(1, await GetNumberAsync(owner, firstId));
        Assert.Equal(2, await CreateAndGetNumberAsync(owner, projectId, "second"));
        Assert.Equal(3, await CreateAndGetNumberAsync(owner, projectId, "third"));
    }

    [Fact]
    public async Task Numbering_IsIndependent_AcrossProjects()
    {
        var owner = await RegisterUserAsync();
        var (projectA, _) = await CreateProjectWithIssueAsync(owner);                       // A #1
        var (projectB, _) = await CreateProjectWithIssueAsync(owner, projectName: "Beta");  // B #1

        // Each project keeps its own sequence — both reach #2 next.
        Assert.Equal(2, await CreateAndGetNumberAsync(owner, projectA, "a2"));
        Assert.Equal(2, await CreateAndGetNumberAsync(owner, projectB, "b2"));
    }

    [Fact]
    public async Task ExplicitNumber_RatchetsCounter_SoNextAllocationDoesNotCollide()
    {
        // Mirrors the backfill / data-import path: a row written with an
        // explicit number must push the per-project counter forward (GREATEST)
        // so the next trigger-allocated number is max+1, never a duplicate.
        var owner = await RegisterUserAsync();
        var (projectId, _) = await CreateProjectWithIssueAsync(owner); // #1

        await using (var c = Db.OpenConnection())
        {
            await c.ExecuteAsync(
                "INSERT INTO issues (project_id, title, number) VALUES (@p, 'imported', 100);",
                new { p = projectId });
        }

        Assert.Equal(101, await CreateAndGetNumberAsync(owner, projectId, "after-import"));
    }

    [Fact]
    public async Task DuplicateNumber_InSameProject_IsRejected()
    {
        // The uq_issues_project_number index is the concurrency backstop;
        // a second row forced to the same (project, number) must fail.
        var owner = await RegisterUserAsync();
        var (projectId, _) = await CreateProjectWithIssueAsync(owner); // #1

        await using var c = Db.OpenConnection();
        await Assert.ThrowsAsync<Npgsql.PostgresException>(() => c.ExecuteAsync(
            "INSERT INTO issues (project_id, title, number) VALUES (@p, 'dup', 1);",
            new { p = projectId }));
    }

    [Fact]
    public async Task Number_IsExposedOnTheProjectIssueList()
    {
        var owner = await RegisterUserAsync();
        var (projectId, _) = await CreateProjectWithIssueAsync(owner);

        var list = await owner.Http.GetFromJsonAsync<JsonElement>(
            $"/api/projects/{projectId}/issues");
        var item = list.GetProperty("items").EnumerateArray().Single();
        Assert.Equal(1, item.GetProperty("number").GetInt32());
    }

    // -------- helpers ---------------------------------------------------

    private static async Task<int> GetNumberAsync(AuthedClient c, Guid issueId)
    {
        var res = await c.Http.GetFromJsonAsync<JsonElement>($"/api/issues/{issueId}");
        return res.GetProperty("issue").GetProperty("number").GetInt32();
    }

    private static async Task<int> CreateAndGetNumberAsync(
        AuthedClient c, Guid projectId, string title)
    {
        var res = await c.Http.PostAsJsonAsync(
            $"/api/projects/{projectId}/issues", new { title });
        res.EnsureSuccessStatusCode();
        var body = await res.Content.ReadFromJsonAsync<JsonElement>();
        return body.GetProperty("issue").GetProperty("number").GetInt32();
    }
}
