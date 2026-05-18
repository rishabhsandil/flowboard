namespace FlowBoard.Core.Data.Queries;

public static class CfdQueries
{
    /// <summary>
    /// Upsert today's open-issue count per column for the project.
    /// Uses ON CONFLICT so repeated calls within the same day are idempotent.
    /// Only open issues (closed_at IS NULL) are counted; "done" column issues
    /// are still included because a Done count > 0 is meaningful for the CFD.
    /// </summary>
    public const string UpsertTodaySnapshot = @"
        INSERT INTO board_snapshots (project_id, column_id, column_name, issue_count, snapped_at)
        SELECT
            b.project_id,
            c.id,
            c.name,
            COUNT(i.id),
            CURRENT_DATE
        FROM columns c
        JOIN boards b ON b.id = c.board_id
        LEFT JOIN issues i ON i.column_id = c.id AND i.closed_at IS NULL
        WHERE b.project_id = @ProjectId
        GROUP BY b.project_id, c.id, c.name
        ON CONFLICT (project_id, column_id, snapped_at)
        DO UPDATE SET
            issue_count = EXCLUDED.issue_count,
            column_name = EXCLUDED.column_name;";

    /// <summary>
    /// Return all snapshots for the project over the last <c>@Days</c> calendar
    /// days, ordered by date then column name for deterministic client rendering.
    /// </summary>
    public const string GetCfdData = @"
        SELECT snapped_at AS day, column_name, issue_count
        FROM board_snapshots
        WHERE project_id = @ProjectId
          AND snapped_at >= CURRENT_DATE - (@Days::int - 1)
        ORDER BY snapped_at, column_name;";
}
