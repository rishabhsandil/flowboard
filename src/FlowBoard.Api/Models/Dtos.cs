using System.ComponentModel.DataAnnotations;

namespace FlowBoard.Api.Models;

// Shared validation patterns. Mirrors Postgres CHECK constraints in schema.sql
// so invalid values are rejected at the API edge with a 400 instead of a 500.
internal static class ValidationPatterns
{
    public const string Priority         = "^(low|medium|high|critical)$";
    public const string SprintStatus     = "^(planned|active|completed)$";
    public const string HexColor         = "^#[0-9a-fA-F]{6}$";
    public const string DependencyKind   = "^(blocks|relates)$";
}

// ---------- Pagination ----------
// Bound `take` so a misbehaving client can't request millions of rows; default
// `skip=0, take=50` is safe for current dashboard sizes.
public sealed class PageRequest
{
    [Range(0, int.MaxValue)] public int Skip { get; init; } = 0;
    [Range(1, 100)]          public int Take { get; init; } = 50;
}
public record Paged<T>(IEnumerable<T> Items, int Total, int Skip, int Take);

// ---------- Auth ----------
public record RegisterRequest(
    [Required, EmailAddress] string Email,
    [Required, StringLength(80, MinimumLength = 1)] string Name,
    [Required, StringLength(100, MinimumLength = 8)] string Password
);
public record LoginRequest([Required, EmailAddress] string Email, [Required] string Password);
public record RefreshRequest([Required] string RefreshToken);
public record AuthResponse(object User, string AccessToken, string RefreshToken);

/// <summary>
/// Partial profile update; both fields optional. NULL = leave field as-is
/// (the SQL uses COALESCE). To CLEAR the avatar URL pass an empty string —
/// the Url validator only kicks in for non-empty values.
/// </summary>
public record UpdateProfileRequest(
    [StringLength(80, MinimumLength = 1)] string? Name,
    [StringLength(500)] string? AvatarUrl
);
public record ChangePasswordRequest(
    [Required] string CurrentPassword,
    [Required, StringLength(100, MinimumLength = 8)] string NewPassword
);

// Forgot-password kickoff. Always returns 200 to avoid revealing whether
// the email is registered (user enumeration defence).
public record ForgotPasswordRequest(
    [Required, EmailAddress, StringLength(254)] string Email
);

// Reset redemption. `Token` is the raw value the user pasted from the email;
// it is hashed server-side before looking up `password_reset_tokens`.
public record ResetPasswordRequest(
    [Required, StringLength(200, MinimumLength = 16)] string Token,
    [Required, StringLength(100, MinimumLength = 8)] string NewPassword
);

// ---------- Projects ----------
public record CreateProjectRequest([Required, StringLength(80)] string Name, [StringLength(500)] string? Description);
public record UpdateProjectRequest([StringLength(80)] string? Name, [StringLength(500)] string? Description);

// ---------- Project members ----------
internal static class MemberRoles
{
    public const string Pattern = "^(owner|member)$";
}
public record AddMemberRequest(
    [Required, EmailAddress, StringLength(254)] string Email,
    [RegularExpression(MemberRoles.Pattern)] string? Role
);
public record UpdateMemberRoleRequest(
    [Required, RegularExpression(MemberRoles.Pattern)] string Role
);

// ---------- Columns ----------
public record CreateColumnRequest([Required, StringLength(50)] string Name, bool IsDone = false);
public record UpdateColumnRequest([Required, StringLength(50)] string Name, bool IsDone);
public record ReorderColumnsRequest(Guid[] Ids, int[] Positions);

// ---------- Issues ----------
public record CreateIssueRequest(
    [Required, StringLength(200)] string Title,
    [StringLength(5000)] string? Description,
    Guid? ColumnId,
    Guid? EpicId,
    Guid? SprintId,
    Guid? AssigneeId,
    /// <summary>Optional parent issue ID; set to create this as a sub-issue.</summary>
    Guid? ParentId,
    [RegularExpression(ValidationPatterns.Priority)] string? Priority,
    [Range(0, 1000)] int? StoryPoints
);
public record UpdateIssueRequest(
    [StringLength(200)] string? Title,
    [StringLength(5000)] string? Description,
    [RegularExpression(ValidationPatterns.Priority)] string? Priority,
    [Range(0, 1000)] int? StoryPoints,
    Guid? ColumnId,
    Guid? EpicId,
    Guid? SprintId,
    Guid? AssigneeId,
    /// <summary>Pass empty Guid to detach from parent; null leaves it unchanged.</summary>
    Guid? ParentId,
    bool? Closed
);
public record ReorderIssuesItem(Guid Id, Guid ColumnId, int Position);
public record ReorderIssuesRequest(ReorderIssuesItem[] Items);

// ---------- Epics ----------
public record CreateEpicRequest(
    [Required, StringLength(120)] string Title,
    [StringLength(2000)] string? Description,
    [RegularExpression(ValidationPatterns.HexColor)] string? Color,
    DateOnly? StartDate,
    DateOnly? DueDate
);
public record UpdateEpicRequest(
    [StringLength(120)] string? Title,
    [StringLength(2000)] string? Description,
    [RegularExpression(ValidationPatterns.HexColor)] string? Color,
    DateOnly? StartDate,
    DateOnly? DueDate
);

// ---------- Sprints ----------
public record CreateSprintRequest(
    [Required, StringLength(80)] string Name,
    [StringLength(500)] string? Goal,
    [Required] DateOnly StartDate,
    [Required] DateOnly EndDate,
    [RegularExpression(ValidationPatterns.SprintStatus)] string? Status
);
public record UpdateSprintRequest(
    [StringLength(80)] string? Name,
    [StringLength(500)] string? Goal,
    DateOnly? StartDate,
    DateOnly? EndDate,
    [RegularExpression(ValidationPatterns.SprintStatus)] string? Status
);

// ---------- Comments ----------
public record CreateCommentRequest(
    [Required, StringLength(5000, MinimumLength = 1)] string Body
);
public record UpdateCommentRequest(
    [Required, StringLength(5000, MinimumLength = 1)] string Body
);

// ---------- Labels ----------
public record CreateLabelRequest(
    [Required, StringLength(40, MinimumLength = 1)] string Name,
    [RegularExpression(ValidationPatterns.HexColor)] string? Color
);
public record UpdateLabelRequest(
    [StringLength(40, MinimumLength = 1)] string? Name,
    [RegularExpression(ValidationPatterns.HexColor)] string? Color
);
public record AttachLabelRequest([Required] Guid LabelId);

// ---------- Issue dependencies ----------
public record CreateIssueDependencyRequest(
    [Required] Guid DependsOnId,
    [RegularExpression(ValidationPatterns.DependencyKind)] string? Kind
);
