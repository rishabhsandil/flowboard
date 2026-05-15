namespace FlowBoard.Core.Data.Queries;

/// <summary>
/// SQL for the append-only activity feed. Reads always join users so we can
/// render the actor's name + avatar without a second round-trip.
/// </summary>
public static class ActivityQueries
{
    public const string Insert = @"
        INSERT INTO activities (project_id, issue_id, actor_id, type, payload)
        VALUES (@ProjectId, @IssueId, @ActorId, @Type, @Payload::jsonb)
        RETURNING id, project_id, issue_id, actor_id, type, payload::text AS payload, created_at;";

    public const string ListByIssue = @"
        SELECT
          a.id           AS Id,
          a.project_id   AS ProjectId,
          a.issue_id     AS IssueId,
          a.actor_id     AS ActorId,
          u.name         AS ActorName,
          u.avatar_url   AS ActorAvatarUrl,
          a.type         AS Type,
          a.payload::text AS Payload,
          a.created_at   AS CreatedAt
        FROM activities a
        LEFT JOIN users u ON u.id = a.actor_id
        WHERE a.issue_id = @IssueId
        ORDER BY a.created_at DESC
        OFFSET @Skip LIMIT @Take;";

    public const string CountByIssue = @"
        SELECT COUNT(*) FROM activities WHERE issue_id = @IssueId;";

    public const string ListByProject = @"
        SELECT
          a.id           AS Id,
          a.project_id   AS ProjectId,
          a.issue_id     AS IssueId,
          a.actor_id     AS ActorId,
          u.name         AS ActorName,
          u.avatar_url   AS ActorAvatarUrl,
          a.type         AS Type,
          a.payload::text AS Payload,
          a.created_at   AS CreatedAt
        FROM activities a
        LEFT JOIN users u ON u.id = a.actor_id
        WHERE a.project_id = @ProjectId
        ORDER BY a.created_at DESC
        OFFSET @Skip LIMIT @Take;";

    public const string CountByProject = @"
        SELECT COUNT(*) FROM activities WHERE project_id = @ProjectId;";
}
