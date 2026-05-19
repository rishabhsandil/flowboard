namespace FlowBoard.Core.Data.Queries;

/// <summary>
/// All hand-written SQL for issues and the board view.
/// Parameter style is Dapper (@name).
/// </summary>
public static class IssueQueries
{
    public const string GetBoard = @"
        SELECT
          c.id   AS column_id,
          c.name AS column_name,
          c.position AS column_position,
          c.is_done AS column_is_done,
          COALESCE(
            json_agg(
              json_build_object(
                'id',           i.id,
                'title',        i.title,
                'priority',     i.priority,
                'story_points', i.story_points,
                'position',     i.position,
                'assignee_id',  i.assignee_id,
                'epic_id',      i.epic_id,
                'sprint_id',    i.sprint_id,
                'parent_id',    i.parent_id,
                'child_count',  (SELECT COUNT(*) FROM issues WHERE parent_id = i.id),
                'epic_color',   e.color,
                'epic_title',   e.title,
                'assignee_name',       u.name,
                'assignee_avatar_url', u.avatar_url,
                'labels',       COALESCE(lb.labels, '[]'::json)
              ) ORDER BY i.position
            ) FILTER (WHERE i.id IS NOT NULL),
            '[]'
          )::text AS issues_json
        FROM columns c
        LEFT JOIN issues i ON i.column_id = c.id
        LEFT JOIN epics  e ON e.id = i.epic_id
        LEFT JOIN users  u ON u.id = i.assignee_id
        LEFT JOIN LATERAL (
          SELECT json_agg(
                   json_build_object('id', l.id, 'name', l.name, 'color', l.color)
                   ORDER BY LOWER(l.name)
                 ) AS labels
          FROM issue_labels il
          JOIN labels l ON l.id = il.label_id
          WHERE il.issue_id = i.id
        ) lb ON TRUE
        WHERE c.board_id = @BoardId
        GROUP BY c.id, c.name, c.position, c.is_done
        ORDER BY c.position;";

    public const string GetById = @"
        SELECT id, project_id, column_id, epic_id, sprint_id, assignee_id,
               title, description, priority, story_points, position,
               created_at, updated_at, closed_at, parent_id
        FROM issues WHERE id = @Id;";

    public const string Insert = @"
        INSERT INTO issues
          (project_id, column_id, epic_id, sprint_id, assignee_id, parent_id,
           title, description, priority, story_points, position)
        VALUES
          (@ProjectId, @ColumnId, @EpicId, @SprintId, @AssigneeId, @ParentId,
           @Title, @Description, @Priority, @StoryPoints,
           COALESCE((SELECT MAX(position) + 1 FROM issues WHERE column_id = @ColumnId), 0))
        RETURNING id, project_id, column_id, epic_id, sprint_id, assignee_id,
                  title, description, priority, story_points, position,
                  created_at, updated_at, closed_at, parent_id;";

    // Dynamic-friendly partial update via COALESCE; pass NULL to leave a field unchanged.
    // For epic_id / sprint_id / assignee_id / parent_id: pass NULL to leave unchanged, or
    // pass the empty Guid '00000000-0000-0000-0000-000000000000' to explicitly clear.
    public const string Update = @"
        UPDATE issues SET
          title        = COALESCE(@Title,       title),
          description  = COALESCE(@Description, description),
          priority     = COALESCE(@Priority,    priority),
          story_points = COALESCE(@StoryPoints, story_points),
          column_id    = COALESCE(@ColumnId,    column_id),
          epic_id      = CASE
                           WHEN @EpicId IS NULL THEN epic_id
                           WHEN @EpicId = '00000000-0000-0000-0000-000000000000' THEN NULL
                           ELSE @EpicId
                         END,
          sprint_id    = CASE
                           WHEN @SprintId IS NULL THEN sprint_id
                           WHEN @SprintId = '00000000-0000-0000-0000-000000000000' THEN NULL
                           ELSE @SprintId
                         END,
          assignee_id  = CASE
                           WHEN @AssigneeId IS NULL THEN assignee_id
                           WHEN @AssigneeId = '00000000-0000-0000-0000-000000000000' THEN NULL
                           ELSE @AssigneeId
                         END,
          parent_id    = CASE
                           WHEN @ParentId IS NULL THEN parent_id
                           WHEN @ParentId = '00000000-0000-0000-0000-000000000000' THEN NULL
                           ELSE @ParentId
                         END,
          closed_at    = CASE
                           WHEN @SetClosed = TRUE  THEN COALESCE(closed_at, NOW())
                           WHEN @SetClosed = FALSE THEN NULL
                           ELSE closed_at
                         END
        WHERE id = @Id
        RETURNING id, project_id, column_id, epic_id, sprint_id, assignee_id,
                  title, description, priority, story_points, position,
                  created_at, updated_at, closed_at, parent_id;";

    public const string Delete = @"DELETE FROM issues WHERE id = @Id;";

    /// <summary>Direct children of a parent issue; minimal columns for the sub-issue list.</summary>
    public const string ChildrenByParent = @"
        SELECT id, project_id, column_id, epic_id, sprint_id, assignee_id,
               title, description, priority, story_points, position,
               created_at, updated_at, closed_at, parent_id
        FROM issues
        WHERE parent_id = @ParentId
        ORDER BY position, created_at;";

    /// <summary>
    /// Bulk reorder. Pass three parallel arrays (ids, columnIds, positions).
    /// One round-trip via UPDATE ... FROM unnest(...).
    /// </summary>
    public const string Reorder = @"
        UPDATE issues AS i
        SET column_id = v.column_id,
            position  = v.position
        FROM unnest(@Ids::uuid[], @ColumnIds::uuid[], @Positions::int[])
          AS v(id, column_id, position)
        WHERE i.id = v.id;";

    /// <summary>
    /// Project-scoped issue list with optional filters. Every filter
    /// parameter follows the <c>@X IS NULL OR ...</c> pattern so a single
    /// query handles all combinations without string concatenation. Sentinel
    /// for &quot;no sprint&quot; / &quot;no epic&quot; / &quot;no assignee&quot;
    /// is the empty UUID, mirroring the <see cref="Update"/> contract.
    /// Status: <c>'open'</c>, <c>'closed'</c>, or NULL for all.
    /// Search is a case-insensitive substring match on title.
    /// </summary>
    public const string ListByProject = @"
        SELECT i.id, i.project_id, i.column_id, i.epic_id, i.sprint_id, i.assignee_id,
               i.title, i.description, i.priority, i.story_points, i.position,
               i.created_at, i.updated_at, i.closed_at, i.parent_id,
               c.name AS column_name,
               e.title AS epic_title, e.color AS epic_color,
               s.name AS sprint_name,
               u.name AS assignee_name,
               u.avatar_url AS assignee_avatar_url,
               COALESCE(lb.labels, '[]'::json)::text AS labels_json
        FROM issues i
        LEFT JOIN columns c ON c.id = i.column_id
        LEFT JOIN epics   e ON e.id = i.epic_id
        LEFT JOIN sprints s ON s.id = i.sprint_id
        LEFT JOIN users   u ON u.id = i.assignee_id
        LEFT JOIN LATERAL (
          SELECT json_agg(
                   json_build_object('id', l.id, 'name', l.name, 'color', l.color)
                   ORDER BY LOWER(l.name)
                 ) AS labels
          FROM issue_labels il
          JOIN labels l ON l.id = il.label_id
          WHERE il.issue_id = i.id
        ) lb ON TRUE
        WHERE i.project_id = @ProjectId
          AND (@Status   IS NULL
               OR (@Status = 'open'   AND i.closed_at IS NULL)
               OR (@Status = 'closed' AND i.closed_at IS NOT NULL))
          AND (@Priority IS NULL OR i.priority = @Priority)
          AND (@EpicId   IS NULL
               OR (@EpicId = '00000000-0000-0000-0000-000000000000' AND i.epic_id IS NULL)
               OR i.epic_id = @EpicId)
          AND (@SprintId IS NULL
               OR (@SprintId = '00000000-0000-0000-0000-000000000000' AND i.sprint_id IS NULL)
               OR i.sprint_id = @SprintId)
          AND (@AssigneeId IS NULL
               OR (@AssigneeId = '00000000-0000-0000-0000-000000000000' AND i.assignee_id IS NULL)
               OR i.assignee_id = @AssigneeId)
          AND (@LabelId  IS NULL
               OR EXISTS (SELECT 1 FROM issue_labels il2
                          WHERE il2.issue_id = i.id AND il2.label_id = @LabelId))
          AND (@ParentId IS NULL
               OR (@ParentId = '00000000-0000-0000-0000-000000000000' AND i.parent_id IS NULL)
               OR i.parent_id = @ParentId)
          AND (@Search   IS NULL OR i.title ILIKE '%' || @Search || '%')
        ORDER BY i.closed_at IS NULL DESC,
                 CASE i.priority
                   WHEN 'critical' THEN 0
                   WHEN 'high'     THEN 1
                   WHEN 'medium'   THEN 2
                   WHEN 'low'      THEN 3
                   ELSE 4
                 END,
                 i.updated_at DESC
        OFFSET @Skip LIMIT @Take;";

    /// <summary>Total count matching the same filters as <see cref="ListByProject"/>.</summary>
    public const string CountByProject = @"
        SELECT COUNT(*) FROM issues i
        WHERE i.project_id = @ProjectId
          AND (@Status   IS NULL
               OR (@Status = 'open'   AND i.closed_at IS NULL)
               OR (@Status = 'closed' AND i.closed_at IS NOT NULL))
          AND (@Priority IS NULL OR i.priority = @Priority)
          AND (@EpicId   IS NULL
               OR (@EpicId = '00000000-0000-0000-0000-000000000000' AND i.epic_id IS NULL)
               OR i.epic_id = @EpicId)
          AND (@SprintId IS NULL
               OR (@SprintId = '00000000-0000-0000-0000-000000000000' AND i.sprint_id IS NULL)
               OR i.sprint_id = @SprintId)
          AND (@AssigneeId IS NULL
               OR (@AssigneeId = '00000000-0000-0000-0000-000000000000' AND i.assignee_id IS NULL)
               OR i.assignee_id = @AssigneeId)
          AND (@LabelId  IS NULL
               OR EXISTS (SELECT 1 FROM issue_labels il2
                          WHERE il2.issue_id = i.id AND il2.label_id = @LabelId))
          AND (@ParentId IS NULL
               OR (@ParentId = '00000000-0000-0000-0000-000000000000' AND i.parent_id IS NULL)
               OR i.parent_id = @ParentId)
          AND (@Search   IS NULL OR i.title ILIKE '%' || @Search || '%');";

    /// <summary>
    /// Project members for the assignee picker on the issues list page.
    /// Owner first, then alphabetical.
    /// </summary>
    public const string ListMembers = @"
        SELECT u.id, u.email, u.name, u.avatar_url AS AvatarUrl, u.created_at AS CreatedAt,
               pm.role
        FROM project_members pm
        JOIN users u ON u.id = pm.user_id
        WHERE pm.project_id = @ProjectId
        ORDER BY pm.role = 'owner' DESC, LOWER(u.name);";
}
