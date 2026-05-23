using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace FlowBoard.Tests.Integration;

/// <summary>
/// Audits every cross-column move of an issue with a dedicated
/// <c>issue_moved</c> activity row. Two entry points reach the
/// activity log:
///
///   • <c>PATCH /api/issues/{id}</c> with a new <c>columnId</c> (issue
///     modal "move to" dropdown).
///   • <c>PATCH /api/projects/{projectId}/issues/reorder</c> for
///     drag-and-drop on the board (one row per issue that crossed
///     columns; pure position changes within the same column are NOT
///     logged — that's UI noise, not a state change).
///
/// The payload carries both ids AND human-readable names so the
/// frontend feed can render "Todo → In Progress" even after a
/// column has been renamed since.
/// </summary>
public sealed class IssueMoveAuditTests : IntegrationTestBase
{
    public IssueMoveAuditTests(PostgresFixture db) : base(db) { }

    private async Task<List<(Guid Id, string Name)>> GetColumnsAsync(
        AuthedClient client, Guid projectId)
    {
        var board = await client.Http.GetFromJsonAsync<JsonElement>(
            $"/api/projects/{projectId}/board");
        return board.GetProperty("columns").EnumerateArray()
            .Select(c => (
                Guid.Parse(c.GetProperty("id").GetString()!),
                c.GetProperty("name").GetString()!))
            .ToList();
    }

    [Fact]
    public async Task PatchIssue_WithNewColumnId_LogsIssueMoved_WithBothNames()
    {
        var owner = await RegisterUserAsync();
        var (projectId, issueId) = await CreateProjectWithIssueAsync(owner);
        var columns = await GetColumnsAsync(owner, projectId);
        Assert.True(columns.Count >= 2, "default board should seed multiple columns");

        var fromCol = columns[0]; // issue lands in the first column on create
        var toCol   = columns[1];

        var res = await owner.Http.PatchAsJsonAsync(
            $"/api/issues/{issueId}", new { columnId = toCol.Id });
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);

        var feed = await owner.Http.GetFromJsonAsync<JsonElement>(
            $"/api/issues/{issueId}/activity?take=50");
        var moved = feed.GetProperty("items").EnumerateArray()
            .Single(i => i.GetProperty("type").GetString() == "issue_moved");

        var payload = JsonDocument.Parse(moved.GetProperty("payload").GetString()!).RootElement;
        Assert.Equal(fromCol.Id.ToString(), payload.GetProperty("fromColumnId").GetString());
        Assert.Equal(toCol.Id.ToString(),   payload.GetProperty("toColumnId").GetString());
        Assert.Equal(fromCol.Name, payload.GetProperty("fromColumnName").GetString());
        Assert.Equal(toCol.Name,   payload.GetProperty("toColumnName").GetString());
    }

    [Fact]
    public async Task PatchIssue_ColumnChange_IsNotDoubleLoggedAsIssueUpdated()
    {
        // The column carve-out is a behavioural contract: a pure column
        // move should NOT also fire an "issue_updated" row with a
        // `columnId` change in its payload. If a future refactor folds
        // the move back into the generic update path, both rows would
        // appear and the activity feed would say the same thing twice.
        var owner = await RegisterUserAsync();
        var (projectId, issueId) = await CreateProjectWithIssueAsync(owner);
        var columns = await GetColumnsAsync(owner, projectId);

        await owner.Http.PatchAsJsonAsync(
            $"/api/issues/{issueId}", new { columnId = columns[1].Id });

        var feed = await owner.Http.GetFromJsonAsync<JsonElement>(
            $"/api/issues/{issueId}/activity?take=50");
        var items = feed.GetProperty("items").EnumerateArray().ToList();

        Assert.Single(items, i => i.GetProperty("type").GetString() == "issue_moved");
        Assert.DoesNotContain(items, i => i.GetProperty("type").GetString() == "issue_updated");
    }

    [Fact]
    public async Task PatchIssue_NoColumnChange_DoesNotLogIssueMoved()
    {
        var owner = await RegisterUserAsync();
        var (_, issueId) = await CreateProjectWithIssueAsync(owner);

        await owner.Http.PatchAsJsonAsync(
            $"/api/issues/{issueId}", new { title = "Just a rename" });

        var feed = await owner.Http.GetFromJsonAsync<JsonElement>(
            $"/api/issues/{issueId}/activity?take=50");
        Assert.DoesNotContain(
            feed.GetProperty("items").EnumerateArray(),
            i => i.GetProperty("type").GetString() == "issue_moved");
    }

    [Fact]
    public async Task Reorder_AcrossColumns_LogsOneIssueMovedPerMovedIssue()
    {
        var owner = await RegisterUserAsync();
        var (projectId, issueId) = await CreateProjectWithIssueAsync(owner);
        var columns = await GetColumnsAsync(owner, projectId);
        var (fromCol, toCol) = (columns[0], columns[1]);

        var res = await owner.Http.PatchAsJsonAsync(
            $"/api/projects/{projectId}/issues/reorder",
            new
            {
                items = new[]
                {
                    new { id = issueId, columnId = toCol.Id, position = 0 }
                }
            });
        Assert.Equal(HttpStatusCode.NoContent, res.StatusCode);

        var feed = await owner.Http.GetFromJsonAsync<JsonElement>(
            $"/api/issues/{issueId}/activity?take=50");
        var movedRows = feed.GetProperty("items").EnumerateArray()
            .Where(i => i.GetProperty("type").GetString() == "issue_moved")
            .ToList();
        Assert.Single(movedRows);

        var payload = JsonDocument.Parse(movedRows[0].GetProperty("payload").GetString()!).RootElement;
        Assert.Equal(fromCol.Id.ToString(), payload.GetProperty("fromColumnId").GetString());
        Assert.Equal(toCol.Id.ToString(),   payload.GetProperty("toColumnId").GetString());
        Assert.Equal(fromCol.Name, payload.GetProperty("fromColumnName").GetString());
        Assert.Equal(toCol.Name,   payload.GetProperty("toColumnName").GetString());
    }

    [Fact]
    public async Task Reorder_WithinSameColumn_DoesNotLogIssueMoved()
    {
        var owner = await RegisterUserAsync();
        var (projectId, issueId) = await CreateProjectWithIssueAsync(owner);
        var columns = await GetColumnsAsync(owner, projectId);
        var sameCol = columns[0]; // issue is already here

        var res = await owner.Http.PatchAsJsonAsync(
            $"/api/projects/{projectId}/issues/reorder",
            new
            {
                items = new[]
                {
                    new { id = issueId, columnId = sameCol.Id, position = 5 }
                }
            });
        Assert.Equal(HttpStatusCode.NoContent, res.StatusCode);

        var feed = await owner.Http.GetFromJsonAsync<JsonElement>(
            $"/api/issues/{issueId}/activity?take=50");
        Assert.DoesNotContain(
            feed.GetProperty("items").EnumerateArray(),
            i => i.GetProperty("type").GetString() == "issue_moved");
    }

    [Fact]
    public async Task Reorder_AcrossColumns_MoveIsVisibleOnProjectActivityFeed()
    {
        // Regression guard: the user explicitly asked for moves to surface
        // on the activity view. The project feed pulls from the same
        // `activities` table as the per-issue feed, so this should fall out
        // for free — but pin it so a future filter that hides
        // `issue_moved` at the project level fails loudly.
        var owner = await RegisterUserAsync();
        var (projectId, issueId) = await CreateProjectWithIssueAsync(owner);
        var columns = await GetColumnsAsync(owner, projectId);

        await owner.Http.PatchAsJsonAsync(
            $"/api/projects/{projectId}/issues/reorder",
            new
            {
                items = new[]
                {
                    new { id = issueId, columnId = columns[1].Id, position = 0 }
                }
            });

        var projectFeed = await owner.Http.GetFromJsonAsync<JsonElement>(
            $"/api/projects/{projectId}/activity?take=50");
        Assert.Contains(
            projectFeed.GetProperty("items").EnumerateArray(),
            i => i.GetProperty("type").GetString() == "issue_moved"
                 && i.GetProperty("issueId").GetString() == issueId.ToString());
    }
}
