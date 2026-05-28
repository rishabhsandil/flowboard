namespace FlowBoard.Core.Data.Queries;

/// <summary>SQL for per-user board filter presets.</summary>
public static class SavedFilterQueries
{
    public const string ListByUserProject = @"
        SELECT id, project_id, user_id, name, filters::text AS filters, created_at
        FROM saved_filters
        WHERE project_id = @ProjectId AND user_id = @UserId
        ORDER BY created_at;";

    public const string GetById = @"
        SELECT id, project_id, user_id, name, filters::text AS filters, created_at
        FROM saved_filters WHERE id = @Id;";

    public const string Insert = @"
        INSERT INTO saved_filters (project_id, user_id, name, filters)
        VALUES (@ProjectId, @UserId, @Name, @Filters::jsonb)
        RETURNING id, project_id, user_id, name, filters::text AS filters, created_at;";

    public const string Delete = @"DELETE FROM saved_filters WHERE id = @Id;";
}
