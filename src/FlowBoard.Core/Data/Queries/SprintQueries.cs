namespace FlowBoard.Core.Data.Queries;

public static class SprintQueries
{
    public const string ListByProject = @"
        SELECT id, project_id, name, goal, start_date, end_date, status, created_at
        FROM sprints WHERE project_id = @ProjectId
        ORDER BY start_date DESC
        OFFSET @Skip LIMIT @Take;";

    public const string CountByProject = @"
        SELECT COUNT(*) FROM sprints WHERE project_id = @ProjectId;";

    public const string GetById = @"
        SELECT id, project_id, name, goal, start_date, end_date, status, created_at
        FROM sprints WHERE id = @Id;";

    public const string Insert = @"
        INSERT INTO sprints (project_id, name, goal, start_date, end_date, status)
        VALUES (@ProjectId, @Name, @Goal, @StartDate, @EndDate, COALESCE(@Status, 'planned'))
        RETURNING id, project_id, name, goal, start_date, end_date, status, created_at;";

    public const string Update = @"
        UPDATE sprints SET
          name       = COALESCE(@Name,      name),
          goal       = COALESCE(@Goal,      goal),
          start_date = COALESCE(@StartDate, start_date),
          end_date   = COALESCE(@EndDate,   end_date),
          status     = COALESCE(@Status,    status)
        WHERE id = @Id
        RETURNING id, project_id, name, goal, start_date, end_date, status, created_at;";

    public const string Delete = @"DELETE FROM sprints WHERE id = @Id;";

    /// <summary>
    /// Velocity per completed sprint with cumulative running total via window
    /// function over an aggregate. See docs/queries.md for the explanation.
    /// </summary>
    public const string Velocity = @"
        SELECT
          s.id,
          s.name,
          s.start_date,
          s.end_date,
          COALESCE(SUM(i.story_points), 0) AS points_completed,
          SUM(COALESCE(SUM(i.story_points), 0))
            OVER (ORDER BY s.start_date ROWS UNBOUNDED PRECEDING) AS cumulative_points
        FROM sprints s
        LEFT JOIN issues i
          ON i.sprint_id = s.id
          AND i.closed_at IS NOT NULL
          AND i.closed_at <= s.end_date + INTERVAL '1 day'
        WHERE s.project_id = @ProjectId
          AND s.status = 'completed'
        GROUP BY s.id, s.name, s.start_date, s.end_date
        ORDER BY s.start_date;";

    /// <summary>
    /// Daily burndown: for each calendar day from sprint start to
    /// min(sprint end, today), compute how many story points remain open.
    /// <para>
    /// Column types: <c>day</c> is DATE → <see cref="DateTime"/>;
    /// <c>remaining</c> is bigint arithmetic → <see cref="long"/>.
    /// </para>
    /// </summary>
    public const string Burndown = @"
        WITH sprint_bounds AS (
            SELECT start_date::date AS start_day,
                   end_date::date   AS end_day
            FROM sprints
            WHERE id = @SprintId
        ),
        date_series AS (
            SELECT generate_series(
                (SELECT start_day FROM sprint_bounds),
                LEAST((SELECT end_day FROM sprint_bounds), CURRENT_DATE),
                '1 day'::interval
            )::date AS day
        ),
        committed AS (
            SELECT COALESCE(SUM(story_points), 0) AS total
            FROM issues
            WHERE sprint_id = @SprintId
        )
        SELECT
            ds.day,
            (SELECT total FROM committed) - COALESCE(
                (SELECT SUM(i.story_points)
                 FROM issues i
                 WHERE i.sprint_id = @SprintId
                   AND i.closed_at IS NOT NULL
                   AND i.closed_at::date <= ds.day),
                0
            ) AS remaining
        FROM date_series ds
        ORDER BY ds.day;";

    /// <summary>Issues in a sprint, grouped client-side by status (open/closed).</summary>
    public const string SprintIssues = @"
        SELECT i.id, i.title, i.priority, i.story_points, i.assignee_id,
               i.column_id, i.epic_id, i.closed_at,
               c.name AS column_name, e.color AS epic_color, e.title AS epic_title
        FROM issues i
        LEFT JOIN columns c ON c.id = i.column_id
        LEFT JOIN epics   e ON e.id = i.epic_id
        WHERE i.sprint_id = @SprintId
        ORDER BY i.closed_at IS NULL DESC, i.priority, i.position;";
}
