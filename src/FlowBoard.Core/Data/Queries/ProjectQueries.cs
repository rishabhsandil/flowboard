namespace FlowBoard.Core.Data.Queries;

public static class ProjectQueries
{
    public const string ListForUser = @"
        SELECT p.id,
               p.name,
               p.slug,
               p.description,
               p.owner_id AS OwnerId,
               p.created_at AS CreatedAt,
               pm.role AS Role
        FROM projects p
        JOIN project_members pm ON pm.project_id = p.id
        WHERE pm.user_id = @UserId
        ORDER BY p.created_at DESC
        OFFSET @Skip LIMIT @Take;";

    public const string CountForUser = @"
        SELECT COUNT(*) FROM project_members WHERE user_id = @UserId;";

    public const string GetBySlugForUser = @"
        SELECT p.id,
               p.name,
               p.slug,
               p.description,
               p.owner_id AS OwnerId,
               p.created_at AS CreatedAt,
               pm.role AS Role
        FROM projects p
        JOIN project_members pm ON pm.project_id = p.id AND pm.user_id = @UserId
        WHERE p.slug = @Slug;";

    public const string GetBoardId = @"
        SELECT id FROM boards WHERE project_id = @ProjectId LIMIT 1;";

    public const string GetFirstColumnId = @"
        SELECT id FROM columns WHERE board_id = @BoardId ORDER BY position LIMIT 1;";

    public const string SlugExists = @"SELECT 1 FROM projects WHERE slug = @Slug LIMIT 1;";

    public const string Insert = @"
        INSERT INTO projects (name, slug, description, owner_id)
        VALUES (@Name, @Slug, @Description, @OwnerId)
        RETURNING id,
                  name,
                  slug,
                  description,
                  owner_id AS OwnerId,
                  created_at AS CreatedAt;";

    public const string InsertMember = @"
        INSERT INTO project_members (project_id, user_id, role)
        VALUES (@ProjectId, @UserId, @Role)
        ON CONFLICT DO NOTHING;";

    public const string InsertBoard = @"
        INSERT INTO boards (project_id) VALUES (@ProjectId) RETURNING id;";

    public const string InsertDefaultColumns = @"
        INSERT INTO columns (board_id, name, position) VALUES
          (@BoardId, 'Backlog',     0),
          (@BoardId, 'In Progress', 1),
          (@BoardId, 'In Review',   2),
          (@BoardId, 'Done',        3);";

    public const string Update = @"
        UPDATE projects SET
          name        = COALESCE(@Name,        name),
          description = COALESCE(@Description, description)
        WHERE id = @Id
                RETURNING id,
                                    name,
                                    slug,
                                    description,
                                    owner_id AS OwnerId,
                                    created_at AS CreatedAt;";

    public const string Delete = @"DELETE FROM projects WHERE id = @Id;";

    // Authorization helpers
    public const string IsMember = @"
        SELECT 1 FROM project_members
        WHERE project_id = @ProjectId AND user_id = @UserId LIMIT 1;";

    public const string IsOwner = @"
        SELECT 1 FROM project_members
        WHERE project_id = @ProjectId AND user_id = @UserId AND role = 'owner' LIMIT 1;";

    // Columns
    public const string ListColumns = @"
        SELECT id, board_id, name, position, created_at
        FROM columns WHERE board_id = @BoardId ORDER BY position;";

    public const string InsertColumn = @"
        INSERT INTO columns (board_id, name, position)
        VALUES (
          @BoardId, @Name,
          COALESCE((SELECT MAX(position) + 1 FROM columns WHERE board_id = @BoardId), 0)
        )
        RETURNING id, board_id, name, position, created_at;";

    public const string RenameColumn = @"
        UPDATE columns SET name = @Name WHERE id = @Id
        RETURNING id, board_id, name, position, created_at;";

    public const string DeleteColumn = @"DELETE FROM columns WHERE id = @Id;";

    public const string ReorderColumns = @"
        UPDATE columns AS c
        SET position = v.position
        FROM unnest(@Ids::uuid[], @Positions::int[]) AS v(id, position)
        WHERE c.id = v.id;";

    public const string ColumnProjectId = @"
        SELECT b.project_id FROM columns c
        JOIN boards b ON b.id = c.board_id
        WHERE c.id = @ColumnId;";

    public const string IssueProjectId = @"
        SELECT project_id FROM issues WHERE id = @IssueId;";

    public const string EpicProjectId = @"
        SELECT project_id FROM epics WHERE id = @EpicId;";

    public const string SprintProjectId = @"
        SELECT project_id FROM sprints WHERE id = @SprintId;";

    // ----- Members -----
    /// <summary>Look up a user by case-insensitive email so invites work
    /// regardless of how the inviter typed the address.</summary>
    public const string FindUserByEmail = @"
        SELECT id, email, name, avatar_url AS AvatarUrl, created_at AS CreatedAt
        FROM users WHERE LOWER(email) = LOWER(@Email);";

    /// <summary>
    /// Add a project member. ON CONFLICT keeps the call idempotent — if the
    /// row already exists, leave the existing role unchanged (use the
    /// dedicated <see cref="UpdateMemberRole"/> for promotions).
    /// </summary>
    public const string InsertMemberReturning = @"
        INSERT INTO project_members (project_id, user_id, role)
        VALUES (@ProjectId, @UserId, @Role)
        ON CONFLICT (project_id, user_id) DO NOTHING
        RETURNING project_id, user_id, role;";

    public const string UpdateMemberRole = @"
        UPDATE project_members SET role = @Role
        WHERE project_id = @ProjectId AND user_id = @UserId
        RETURNING project_id, user_id, role;";

    public const string DeleteMember = @"
        DELETE FROM project_members
        WHERE project_id = @ProjectId AND user_id = @UserId;";

    public const string CountOwners = @"
        SELECT COUNT(*) FROM project_members
        WHERE project_id = @ProjectId AND role = 'owner';";

    public const string GetMemberRole = @"
        SELECT role FROM project_members
        WHERE project_id = @ProjectId AND user_id = @UserId;";
}
