namespace FlowBoard.Core.Models;

public record User(
    Guid Id,
    string Email,
    string Name,
    string? AvatarUrl,
    DateTime CreatedAt
);

public record UserWithHash(
    Guid Id,
    string Email,
    string Name,
    string? AvatarUrl,
    string PasswordHash,
    DateTime CreatedAt
);

public record Project(
    Guid Id,
    string Name,
    string Slug,
    string? Description,
    Guid OwnerId,
    DateTime CreatedAt,
    string? Role = null
)
{
    public Project(Guid id, string name, string slug, string? description, Guid ownerId, DateTime createdAt)
        : this(id, name, slug, description, ownerId, createdAt, null)
    {
    }
}

public record BoardColumn(
    Guid Id,
    Guid BoardId,
    string Name,
    int Position,
    bool IsDone,
    DateTime CreatedAt
);

public record Issue(
    Guid Id,
    Guid ProjectId,
    Guid? ColumnId,
    Guid? EpicId,
    Guid? SprintId,
    Guid? AssigneeId,
    string Title,
    string? Description,
    string Priority,
    int StoryPoints,
    int Position,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    DateTime? ClosedAt,
    Guid? ParentId = null
)
{
    /// <summary>
    /// Forwarding constructor for queries that do not project <c>parent_id</c>.
    /// Dapper picks this 14-param overload when the result set lacks that column.
    /// </summary>
    public Issue(Guid id, Guid projectId, Guid? columnId, Guid? epicId, Guid? sprintId,
                 Guid? assigneeId, string title, string? description, string priority,
                 int storyPoints, int position, DateTime createdAt, DateTime updatedAt,
                 DateTime? closedAt)
        : this(id, projectId, columnId, epicId, sprintId, assigneeId, title, description,
               priority, storyPoints, position, createdAt, updatedAt, closedAt, null) { }
}

public record Epic(
    Guid Id,
    Guid ProjectId,
    string Title,
    string? Description,
    string Color,
    DateTime? StartDate,
    DateTime? DueDate,
    DateTime CreatedAt
);

public record EpicWithProgress(
    Guid Id,
    string Title,
    string Color,
    DateTime? DueDate,
    DateTime? StartDate,
    string? Description,
    long TotalIssues,
    long ClosedIssues,
    decimal? PercentComplete,
    long TotalPoints,
    long CompletedPoints
);

public record Sprint(
    Guid Id,
    Guid ProjectId,
    string Name,
    string? Goal,
    DateTime StartDate,
    DateTime EndDate,
    string Status,
    DateTime CreatedAt
);

public record SprintVelocityRow(
    Guid Id,
    string Name,
    DateTime StartDate,
    DateTime EndDate,
    long PointsCompleted,
    decimal CumulativePoints
);

/// <summary>One row from the GetBoard query — issues are returned as a JSON string.</summary>
public record BoardColumnRow(
    Guid ColumnId,
    string ColumnName,
    int ColumnPosition,
    bool ColumnIsDone,
    string IssuesJson
);

/// <summary>(issue id, current column id) pair used by the move-audit path.</summary>
public record IssueColumnRow(Guid Id, Guid? ColumnId);

/// <summary>(column id, display name) lookup row used by the move-audit path.</summary>
public record ColumnNameRow(Guid Id, string Name);

/// <summary>
/// Row shape for the project-wide issue list. Joins in the column / epic /
/// sprint / assignee names so the table renders without N+1 lookups, and
/// labels are bundled as JSON the same way the board does it.
/// </summary>
public record IssueListRow(
    Guid Id,
    Guid ProjectId,
    Guid? ColumnId,
    Guid? EpicId,
    Guid? SprintId,
    Guid? AssigneeId,
    string Title,
    string? Description,
    string Priority,
    int StoryPoints,
    int Position,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    DateTime? ClosedAt,
    Guid? ParentId,
    string? ColumnName,
    string? EpicTitle,
    string? EpicColor,
    string? SprintName,
    string? AssigneeName,
    string? AssigneeAvatarUrl,
    string LabelsJson
);

/// <summary>Project member row for the assignee picker.</summary>
public record ProjectMember(
    Guid Id,
    string Email,
    string Name,
    string? AvatarUrl,
    DateTime CreatedAt,
    string Role
);

/// <summary>Row from refresh_tokens. RevokedAt/ReplacedBy null = active.</summary>
public record RefreshTokenRow(
    Guid Id,
    Guid UserId,
    DateTime IssuedAt,
    DateTime ExpiresAt,
    DateTime? RevokedAt,
    Guid? ReplacedBy
);

/// <summary>Row from password_reset_tokens. UsedAt null = still redeemable.</summary>
public record PasswordResetTokenRow(
    string TokenHash,
    Guid UserId,
    DateTime ExpiresAt,
    DateTime? UsedAt,
    DateTime CreatedAt
);

// ---------- COMMENTS ----------
public record Comment(
    Guid Id,
    Guid IssueId,
    Guid? AuthorId,
    string Body,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    bool Edited
);

/// <summary>Comment joined with its author for the issue thread view.</summary>
public record CommentWithAuthor(
    Guid Id,
    Guid IssueId,
    Guid? AuthorId,
    string? AuthorName,
    string? AuthorAvatarUrl,
    string Body,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    bool Edited
);

// ---------- LABELS ----------
public record Label(
    Guid Id,
    Guid ProjectId,
    string Name,
    string Color,
    DateTime CreatedAt
);

// ---------- ACTIVITY LOG ----------
/// <summary>
/// One row from the activity feed. <c>Payload</c> is the raw JSON string from
/// the JSONB column so the API can pass it through to the client untouched.
/// </summary>
public record ActivityRow(
    Guid Id,
    Guid ProjectId,
    Guid? IssueId,
    Guid? ActorId,
    string? ActorName,
    string? ActorAvatarUrl,
    string Type,
    string Payload,
    DateTime CreatedAt
);

// ---------- BURNDOWN ----------
/// <summary>
/// One data point for the burndown chart: date + remaining story points.
/// <c>Day</c> is a DATE column → <see cref="DateTime"/> (not DateOnly) per Npgsql rules.
/// <c>Remaining</c> is a bigint arithmetic result → <see cref="long"/>.
/// </summary>
public record BurndownPoint(DateTime Day, long Remaining);

// ---------- CUMULATIVE FLOW DIAGRAM ----------
/// <summary>
/// One data point for the CFD chart: date + column name + issue count.
/// <c>Day</c> is a DATE column → <see cref="DateTime"/> per Npgsql rules.
/// </summary>
public record CfdPoint(DateTime Day, string ColumnName, int IssueCount);
