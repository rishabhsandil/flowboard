namespace FlowBoard.Core.Data.Queries;

public static class EpicQueries
{
    /// <summary>
    /// Epics with progress in a single aggregation. Uses NULLIF for safe division
    /// and conditional SUM to avoid a second join. See docs/queries.md.
    /// </summary>
    public const string ListWithProgress = @"
        SELECT
          e.id,
          e.title,
          e.color,
          e.due_date,
          e.start_date,
          e.description,
          COUNT(i.id) AS total_issues,
          COUNT(i.closed_at) AS closed_issues,
          ROUND(
            COUNT(i.closed_at)::NUMERIC / NULLIF(COUNT(i.id), 0) * 100, 1
          ) AS percent_complete,
          COALESCE(SUM(i.story_points), 0) AS total_points,
          COALESCE(
            SUM(CASE WHEN i.closed_at IS NOT NULL THEN i.story_points ELSE 0 END),
            0
          ) AS completed_points
        FROM epics e
        LEFT JOIN issues i ON i.epic_id = e.id
        WHERE e.project_id = @ProjectId
        GROUP BY e.id, e.title, e.color, e.due_date, e.start_date, e.description, e.created_at
        ORDER BY e.created_at
        OFFSET @Skip LIMIT @Take;";

    public const string CountByProject = @"
        SELECT COUNT(*) FROM epics WHERE project_id = @ProjectId;";

    /// <summary>
    /// Single epic with the same progress aggregation as the list query.
    /// Powers the epic detail page. Returns NULL if the id doesn't exist
    /// — the controller maps that to 404.
    /// </summary>
    public const string GetWithProgress = @"
        SELECT
          e.id,
          e.title,
          e.color,
          e.due_date,
          e.start_date,
          e.description,
          COUNT(i.id) AS total_issues,
          COUNT(i.closed_at) AS closed_issues,
          ROUND(
            COUNT(i.closed_at)::NUMERIC / NULLIF(COUNT(i.id), 0) * 100, 1
          ) AS percent_complete,
          COALESCE(SUM(i.story_points), 0) AS total_points,
          COALESCE(
            SUM(CASE WHEN i.closed_at IS NOT NULL THEN i.story_points ELSE 0 END),
            0
          ) AS completed_points
        FROM epics e
        LEFT JOIN issues i ON i.epic_id = e.id
        WHERE e.id = @Id
        GROUP BY e.id, e.title, e.color, e.due_date, e.start_date, e.description, e.created_at;";

    public const string GetProjectId = @"
        SELECT project_id FROM epics WHERE id = @Id;";

    public const string Insert = @"
        INSERT INTO epics (project_id, title, description, color, start_date, due_date)
        VALUES (@ProjectId, @Title, @Description, COALESCE(@Color, '#22d3ee'), @StartDate, @DueDate)
        RETURNING id, project_id, title, description, color, start_date, due_date, created_at;";

    public const string Update = @"
        UPDATE epics SET
          title       = COALESCE(@Title,       title),
          description = COALESCE(@Description, description),
          color       = COALESCE(@Color,       color),
          start_date  = COALESCE(@StartDate,   start_date),
          due_date    = COALESCE(@DueDate,     due_date)
        WHERE id = @Id
        RETURNING id, project_id, title, description, color, start_date, due_date, created_at;";

    public const string Delete = @"DELETE FROM epics WHERE id = @Id;";
}
