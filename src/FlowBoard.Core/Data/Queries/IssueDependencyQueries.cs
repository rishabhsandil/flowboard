namespace FlowBoard.Core.Data.Queries;

/// <summary>
/// SQL for the issue_dependencies table. Each row is a directed edge from
/// <c>issue_id</c> to <c>depends_on_id</c>: when <c>kind = 'blocks'</c> the
/// source issue blocks the target (the target is "blocked by" the source).
/// <c>kind = 'relates'</c> is undirected for UX purposes — the API still
/// stores it once and surfaces it on both endpoints via the union list.
/// </summary>
public static class IssueDependencyQueries
{
    /// <summary>
    /// All edges touching @IssueId — both directions in a single round-trip.
    /// `direction` is 'outgoing' when the row points away from @IssueId
    /// (i.e. @IssueId is the source) and 'incoming' otherwise.
    /// </summary>
    public const string ListForIssue = @"
        SELECT
          d.issue_id          AS IssueId,
          d.depends_on_id     AS DependsOnId,
          d.kind              AS Kind,
          d.created_at        AS CreatedAt,
          'outgoing'          AS Direction,
          other.id            AS OtherId,
          other.title         AS OtherTitle,
          other.closed_at     AS OtherClosedAt,
          col.name            AS OtherColumnName
        FROM issue_dependencies d
        JOIN issues  other ON other.id = d.depends_on_id
        LEFT JOIN columns col ON col.id = other.column_id
        WHERE d.issue_id = @IssueId
        UNION ALL
        SELECT
          d.issue_id          AS IssueId,
          d.depends_on_id     AS DependsOnId,
          d.kind              AS Kind,
          d.created_at        AS CreatedAt,
          'incoming'          AS Direction,
          other.id            AS OtherId,
          other.title         AS OtherTitle,
          other.closed_at     AS OtherClosedAt,
          col.name            AS OtherColumnName
        FROM issue_dependencies d
        JOIN issues  other ON other.id = d.issue_id
        LEFT JOIN columns col ON col.id = other.column_id
        WHERE d.depends_on_id = @IssueId
        ORDER BY Kind, CreatedAt ASC;";

    /// <summary>
    /// Insert with ON CONFLICT DO NOTHING so a double-click does not 500.
    /// Returning <c>xmax = 0</c> tells the caller whether this row was the
    /// fresh insert (true) or a no-op (false), used to emit activity once.
    /// </summary>
    public const string Insert = @"
        INSERT INTO issue_dependencies (issue_id, depends_on_id, kind)
        VALUES (@IssueId, @DependsOnId, @Kind)
        ON CONFLICT (issue_id, depends_on_id) DO NOTHING
        RETURNING issue_id, depends_on_id, kind, created_at;";

    public const string Delete = @"
        DELETE FROM issue_dependencies
        WHERE issue_id = @IssueId AND depends_on_id = @DependsOnId;";

    /// <summary>
    /// Returns 1 only when both issues exist *and* live in the same project,
    /// so a project member cannot link to an issue in a project they don't
    /// belong to. NULL means at least one issue is missing or cross-project.
    /// </summary>
    public const string ValidateSameProject = @"
        SELECT 1
        FROM issues a
        JOIN issues b ON b.project_id = a.project_id
        WHERE a.id = @IssueId AND b.id = @DependsOnId
        LIMIT 1;";

    /// <summary>
    /// Direct-cycle guard for <c>blocks</c>: refuse to add (A blocks B)
    /// when (B blocks A) already exists. Deeper cycles are intentionally
    /// allowed for now — we'd need a recursive CTE walk to detect them and
    /// the immediate UX risk is the 1-hop A↔B case.
    /// </summary>
    public const string ReverseBlocksExists = @"
        SELECT 1
        FROM issue_dependencies
        WHERE issue_id      = @DependsOnId
          AND depends_on_id = @IssueId
          AND kind          = 'blocks'
        LIMIT 1;";

    /// <summary>
    /// Project-wide issue picker for the "add dependency" autocomplete.
    /// Returns up to @Take rows that match @Search (case-insensitive
    /// substring on title), excluding the issue itself.
    /// </summary>
    public const string SearchForPicker = @"
        SELECT
          i.id           AS Id,
          i.title        AS Title,
          i.closed_at    AS ClosedAt,
          col.name       AS ColumnName
        FROM issues i
        LEFT JOIN columns col ON col.id = i.column_id
        WHERE i.project_id = @ProjectId
          AND i.id        <> @ExcludeId
          AND (@Search IS NULL OR i.title ILIKE '%' || @Search || '%')
        ORDER BY i.closed_at NULLS FIRST, i.updated_at DESC
        LIMIT @Take;";
}
