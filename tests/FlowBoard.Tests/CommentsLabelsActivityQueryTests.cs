using FlowBoard.Core.Data.Queries;

namespace FlowBoard.Tests;

/// <summary>
/// Structural assertions for the comments / labels / activity SQL added on
/// top of the v1 board model. Same spirit as <see cref="QueryShapeTests"/>:
/// catch the shape-level mistakes (missing param, dropped WHERE clause, lost
/// JSONB cast) without requiring a live Postgres.
/// </summary>
public class CommentsLabelsActivityQueryTests
{
    // ---------- Comments ----------

    [Fact]
    public void Comments_ListByIssue_JoinsAuthorAndOrdersAsc()
    {
        // We render comments oldest-first to read like a chat log.
        Assert.Contains("LEFT JOIN users", CommentQueries.ListByIssue);
        Assert.Contains("avatar_url",       CommentQueries.ListByIssue);
        Assert.Contains("@IssueId",         CommentQueries.ListByIssue);
        Assert.Contains("ORDER BY c.created_at ASC", CommentQueries.ListByIssue);
    }

    [Fact]
    public void Comments_Update_FlipsEditedFlag()
    {
        // The DB owns `edited` so a client can't fake an unedited update.
        Assert.Contains("edited = TRUE", CommentQueries.Update);
        Assert.Contains("@Body",         CommentQueries.Update);
    }

    [Fact]
    public void Comments_ResolveMentionHandles_RestrictsToProjectMembers()
    {
        // A non-member's name must never resolve to a mention row.
        Assert.Contains("project_members", CommentQueries.ResolveMentionHandles);
        Assert.Contains("@ProjectId",      CommentQueries.ResolveMentionHandles);
        Assert.Contains("@Handles",        CommentQueries.ResolveMentionHandles);
        Assert.Contains("REPLACE(u.name, ' ', '')", CommentQueries.ResolveMentionHandles);
        Assert.Contains("LOWER(",          CommentQueries.ResolveMentionHandles);
    }

    [Fact]
    public void Comments_InsertMentions_IsIdempotent()
    {
        // Two parses of the same body must not double-insert.
        Assert.Contains("ON CONFLICT", CommentQueries.InsertMentions);
        Assert.Contains("DO NOTHING",  CommentQueries.InsertMentions);
        Assert.Contains("unnest(@UserIds::uuid[])", CommentQueries.InsertMentions);
    }

    [Fact]
    public void Comments_ProjectIdLookup_ResolvesViaIssue()
    {
        // No comment.project_id column exists; we MUST go through issues.
        Assert.Contains("FROM comments", CommentQueries.ProjectIdForComment);
        Assert.Contains("JOIN issues",   CommentQueries.ProjectIdForComment);
        Assert.Contains("@CommentId",    CommentQueries.ProjectIdForComment);
    }

    // ---------- Labels ----------

    [Fact]
    public void Labels_ListByProject_OrdersCaseInsensitive()
    {
        // Display order must match the catalogue page (sorted by lowercased name).
        Assert.Contains("ORDER BY LOWER(name)", LabelQueries.ListByProject);
        Assert.Contains("@ProjectId",           LabelQueries.ListByProject);
    }

    [Fact]
    public void Labels_Attach_IsIdempotent()
    {
        // Repeated POSTs must not duplicate the join row.
        Assert.Contains("ON CONFLICT DO NOTHING", LabelQueries.Attach);
    }

    [Fact]
    public void Labels_CrossProjectGuard_RequiresMatchingProject()
    {
        // Joins issues and labels on shared project_id; if the user-supplied
        // labelId belongs to another project the query returns no row.
        var sql = LabelQueries.ValidateLabelMatchesIssueProject;
        Assert.Contains("l.project_id = i.project_id", sql);
        Assert.Contains("@IssueId",                    sql);
        Assert.Contains("@LabelId",                    sql);
        Assert.Contains("LIMIT 1",                     sql);
    }

    [Fact]
    public void Labels_Update_AllowsPartialPatch()
    {
        // PATCH semantics: name OR color may be omitted.
        Assert.Contains("COALESCE(@Name,",  LabelQueries.Update);
        Assert.Contains("COALESCE(@Color,", LabelQueries.Update);
    }

    // ---------- Activities ----------

    [Fact]
    public void Activities_Insert_CastsPayloadToJsonb()
    {
        // Without ::jsonb Npgsql sends a text param and the column rejects it.
        Assert.Contains("@Payload::jsonb", ActivityQueries.Insert);
        Assert.Contains("@ProjectId",      ActivityQueries.Insert);
        Assert.Contains("@Type",           ActivityQueries.Insert);
    }

    [Fact]
    public void Activities_List_ReturnsPayloadAsText()
    {
        // The API ships `payload` to the client as a JSON string so it can
        // be passed through axios without an intermediate parse step.
        Assert.Contains("a.payload::text", ActivityQueries.ListByIssue);
        Assert.Contains("a.payload::text", ActivityQueries.ListByProject);
    }

    [Fact]
    public void Activities_List_NewestFirst_AndPaginated()
    {
        Assert.Contains("ORDER BY a.created_at DESC", ActivityQueries.ListByIssue);
        Assert.Contains("ORDER BY a.created_at DESC", ActivityQueries.ListByProject);
        Assert.Contains("OFFSET @Skip",               ActivityQueries.ListByIssue);
        Assert.Contains("LIMIT @Take",                ActivityQueries.ListByIssue);
        Assert.Contains("OFFSET @Skip",               ActivityQueries.ListByProject);
        Assert.Contains("LIMIT @Take",                ActivityQueries.ListByProject);
    }

    [Fact]
    public void Activities_List_LeftJoinsUser_SoDeletedActorsStillRender()
    {
        // actor_id is ON DELETE SET NULL — a deleted user must NOT cause
        // the row to disappear from the feed.
        Assert.Contains("LEFT JOIN users", ActivityQueries.ListByIssue);
        Assert.Contains("LEFT JOIN users", ActivityQueries.ListByProject);
    }

    // ---------- Issue board view (now includes labels) ----------

    [Fact]
    public void GetBoard_IncludesLabelsLateralSubquery()
    {
        var sql = IssueQueries.GetBoard;
        Assert.Contains("LEFT JOIN LATERAL", sql);
        Assert.Contains("issue_labels",      sql);
        // Empty array fallback so the JSON shape is stable on the client.
        Assert.Contains("'[]'::json",        sql);
        // Labels must be ordered deterministically inside the per-issue array.
        Assert.Contains("ORDER BY LOWER(l.name)", sql);
    }

    // ---------- Issue project-wide list (Issues page) ----------

    [Fact]
    public void ListByProject_AllOptionalFiltersUseNullCheckPattern()
    {
        // Each filter must follow `(@X IS NULL OR ...)` so omitting it is a
        // no-op. Without these, a missing parameter would silently exclude
        // every row.
        var sql = IssueQueries.ListByProject;
        Assert.Contains("@Status   IS NULL",   sql);
        Assert.Contains("@Priority IS NULL",   sql);
        Assert.Contains("@EpicId   IS NULL",   sql);
        Assert.Contains("@SprintId IS NULL",   sql);
        Assert.Contains("@AssigneeId IS NULL", sql);
        Assert.Contains("@LabelId  IS NULL",   sql);
        Assert.Contains("@Search IS NULL",     sql);
        Assert.Contains("@ProjectId",          sql);
    }

    [Fact]
    public void ListByProject_NullableFkFiltersHonourEmptyGuidSentinel()
    {
        // Empty-Guid means "filter to NULL" — same contract as Update.
        var sql = IssueQueries.ListByProject;
        Assert.Contains("@EpicId = '00000000-0000-0000-0000-000000000000'",     sql);
        Assert.Contains("@SprintId = '00000000-0000-0000-0000-000000000000'",   sql);
        Assert.Contains("@AssigneeId = '00000000-0000-0000-0000-000000000000'", sql);
    }

    [Fact]
    public void ListByProject_StatusBranchesMapToClosedAt()
    {
        // The 'open'/'closed' synthetic status maps directly to closed_at IS [NOT] NULL.
        var sql = IssueQueries.ListByProject;
        Assert.Contains("@Status = 'open'   AND i.closed_at IS NULL",     sql);
        Assert.Contains("@Status = 'closed' AND i.closed_at IS NOT NULL", sql);
    }

    [Fact]
    public void ListByProject_LabelFilterUsesExistsNotJoin()
    {
        // EXISTS keeps the row count correct when an issue has multiple
        // labels; a JOIN would multiply rows by label count.
        var sql = IssueQueries.ListByProject;
        Assert.Contains("EXISTS (SELECT 1 FROM issue_labels", sql);
        Assert.Contains("@LabelId", sql);
    }

    [Fact]
    public void ListByProject_SearchUsesIlikeSubstring()
    {
        Assert.Contains("ILIKE '%' || @Search || '%'", IssueQueries.ListByProject);
    }

    [Fact]
    public void ListByProject_BundlesLabelsAsJson()
    {
        // Same pattern as the board view — keeps the row shape stable.
        var sql = IssueQueries.ListByProject;
        Assert.Contains("LEFT JOIN LATERAL",      sql);
        Assert.Contains("issue_labels",           sql);
        Assert.Contains("ORDER BY LOWER(l.name)", sql);
        Assert.Contains("'[]'::json",             sql);
    }

    [Fact]
    public void ListByProject_OrdersOpenFirstThenPriorityThenUpdated()
    {
        // The exact order users want when scanning a backlog.
        var sql = IssueQueries.ListByProject;
        Assert.Contains("ORDER BY",                       sql);
        Assert.Contains("i.closed_at IS NULL DESC",       sql);
        Assert.Contains("CASE i.priority",                sql);
        Assert.Contains("i.updated_at DESC",              sql);
    }

    [Fact]
    public void ListByProject_IsPaginated()
    {
        Assert.Contains("OFFSET @Skip", IssueQueries.ListByProject);
        Assert.Contains("LIMIT @Take",  IssueQueries.ListByProject);
    }

    [Fact]
    public void CountByProject_MirrorsListFilters()
    {
        // The two queries must accept the same parameters or paging UI lies.
        var sql = IssueQueries.CountByProject;
        Assert.Contains("SELECT COUNT(*)", sql);
        Assert.Contains("@ProjectId",  sql);
        Assert.Contains("@Status",     sql);
        Assert.Contains("@Priority",   sql);
        Assert.Contains("@EpicId",     sql);
        Assert.Contains("@SprintId",   sql);
        Assert.Contains("@AssigneeId", sql);
        Assert.Contains("@LabelId",    sql);
        Assert.Contains("@Search",     sql);
    }

    [Fact]
    public void ListMembers_OrdersOwnerFirstThenAlphabetical()
    {
        var sql = IssueQueries.ListMembers;
        Assert.Contains("project_members",       sql);
        Assert.Contains("@ProjectId",            sql);
        Assert.Contains("pm.role = 'owner' DESC", sql);
        Assert.Contains("LOWER(u.name)",         sql);
    }
}
