namespace FlowBoard.Core.Data.Queries;

/// <summary>
/// SQL for issue comments. The list query joins users so we can render the
/// author name + avatar in a single round-trip.
/// </summary>
public static class CommentQueries
{
    public const string ListByIssue = @"
        SELECT
          c.id          AS Id,
          c.issue_id    AS IssueId,
          c.author_id   AS AuthorId,
          u.name        AS AuthorName,
          u.avatar_url  AS AuthorAvatarUrl,
          c.body        AS Body,
          c.created_at  AS CreatedAt,
          c.updated_at  AS UpdatedAt,
          c.edited      AS Edited
        FROM comments c
        LEFT JOIN users u ON u.id = c.author_id
        WHERE c.issue_id = @IssueId
        ORDER BY c.created_at ASC;";

    public const string GetById = @"
        SELECT id, issue_id, author_id, body, created_at, updated_at, edited
        FROM comments WHERE id = @Id;";

    public const string Insert = @"
        INSERT INTO comments (issue_id, author_id, body)
        VALUES (@IssueId, @AuthorId, @Body)
        RETURNING id, issue_id, author_id, body, created_at, updated_at, edited;";

    public const string Update = @"
        UPDATE comments
           SET body   = @Body,
               edited = TRUE
         WHERE id = @Id
         RETURNING id, issue_id, author_id, body, created_at, updated_at, edited;";

    public const string Delete = @"DELETE FROM comments WHERE id = @Id;";

    /// <summary>Returns the project_id for the issue this comment belongs to.</summary>
    public const string ProjectIdForComment = @"
        SELECT i.project_id
        FROM comments c
        JOIN issues i ON i.id = c.issue_id
        WHERE c.id = @CommentId;";

    /// <summary>
    /// Bulk-resolve @handles to user ids, restricted to project members. The
    /// "handle" form is the user's name lowercased and stripped of whitespace
    /// so "Jane Doe" is mentionable as @janedoe. One round-trip per comment.
    /// </summary>
    public const string ResolveMentionHandles = @"
        SELECT u.id
        FROM users u
        JOIN project_members pm ON pm.user_id = u.id
        WHERE pm.project_id = @ProjectId
          AND LOWER(REPLACE(u.name, ' ', '')) = ANY(@Handles);";

    /// <summary>Insert mentions, ignoring duplicates within the same comment.</summary>
    public const string InsertMentions = @"
        INSERT INTO mentions (comment_id, mentioned_user_id)
        SELECT @CommentId, uid
        FROM unnest(@UserIds::uuid[]) AS uid
        ON CONFLICT (comment_id, mentioned_user_id) DO NOTHING;";

    public const string DeleteMentionsForComment = @"
        DELETE FROM mentions WHERE comment_id = @CommentId;";
}
