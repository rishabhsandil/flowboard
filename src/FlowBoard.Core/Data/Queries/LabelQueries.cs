namespace FlowBoard.Core.Data.Queries;

/// <summary>
/// SQL for project labels and the issue_labels junction table.
/// </summary>
public static class LabelQueries
{
    public const string ListByProject = @"
        SELECT id, project_id, name, color, created_at
        FROM labels
        WHERE project_id = @ProjectId
        ORDER BY LOWER(name);";

    public const string GetById = @"
        SELECT id, project_id, name, color, created_at
        FROM labels WHERE id = @Id;";

    public const string Insert = @"
        INSERT INTO labels (project_id, name, color)
        VALUES (@ProjectId, @Name, @Color)
        RETURNING id, project_id, name, color, created_at;";

    public const string Update = @"
        UPDATE labels SET
          name  = COALESCE(@Name,  name),
          color = COALESCE(@Color, color)
        WHERE id = @Id
        RETURNING id, project_id, name, color, created_at;";

    public const string Delete = @"DELETE FROM labels WHERE id = @Id;";

    /// <summary>Labels currently attached to an issue.</summary>
    public const string ListForIssue = @"
        SELECT l.id, l.project_id, l.name, l.color, l.created_at
        FROM issue_labels il
        JOIN labels l ON l.id = il.label_id
        WHERE il.issue_id = @IssueId
        ORDER BY LOWER(l.name);";

    public const string Attach = @"
        INSERT INTO issue_labels (issue_id, label_id)
        VALUES (@IssueId, @LabelId)
        ON CONFLICT DO NOTHING;";

    public const string Detach = @"
        DELETE FROM issue_labels
        WHERE issue_id = @IssueId AND label_id = @LabelId;";

    /// <summary>Both records must agree on project so members can't attach a foreign label.</summary>
    public const string ValidateLabelMatchesIssueProject = @"
        SELECT 1
        FROM issues i
        JOIN labels l ON l.project_id = i.project_id
        WHERE i.id = @IssueId AND l.id = @LabelId
        LIMIT 1;";
}
