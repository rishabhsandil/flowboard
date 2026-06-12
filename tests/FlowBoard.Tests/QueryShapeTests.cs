using FlowBoard.Core.Data.Queries;
using FlowBoard.Core.Models;

namespace FlowBoard.Tests;

/// <summary>
/// Cheap structural assertions that catch the most common SQL-string mistakes
/// (missing OFFSET/LIMIT after we added pagination, accidental SELECT *,
/// dropped WHERE clause). Not a substitute for integration tests but they run
/// in milliseconds and have already paid for themselves once.
/// </summary>
public class QueryShapeTests
{
    [Fact]
    public void ProjectQueries_ListForUser_IsPaginated()
    {
        Assert.Contains("OFFSET @Skip", ProjectQueries.ListForUser);
        Assert.Contains("LIMIT @Take",  ProjectQueries.ListForUser);
        Assert.Contains("@UserId",      ProjectQueries.ListForUser);
    }

    [Fact]
    public void SprintQueries_ListByProject_IsPaginated()
    {
        Assert.Contains("OFFSET @Skip", SprintQueries.ListByProject);
        Assert.Contains("LIMIT @Take",  SprintQueries.ListByProject);
    }

    [Fact]
    public void EpicQueries_ListWithProgress_IsPaginated()
    {
        Assert.Contains("OFFSET @Skip", EpicQueries.ListWithProgress);
        Assert.Contains("LIMIT @Take",  EpicQueries.ListWithProgress);
    }

    [Fact]
    public void CountQueries_AreScopedToProject()
    {
        Assert.Contains("@ProjectId", SprintQueries.CountByProject);
        Assert.Contains("@ProjectId", EpicQueries.CountByProject);
        Assert.Contains("@UserId",    ProjectQueries.CountForUser);
    }

    [Fact]
    public void RefreshTokenQueries_RevokeChecksRevokedAtIsNull()
    {
        // Idempotency guard: re-revoking should be a no-op.
        Assert.Contains("revoked_at IS NULL", RefreshTokenQueries.Revoke);
    }

    [Fact]
    public void ProjectQueries_AuthorizationHelpersUseLimit1()
    {
        // These must short-circuit; without LIMIT 1 a misconfigured row
        // could return many rows and waste time.
        Assert.Contains("LIMIT 1", ProjectQueries.IsMember);
        Assert.Contains("LIMIT 1", ProjectQueries.IsOwner);
    }

    [Fact]
    public void ProjectQueries_FindUserByEmail_IsCaseInsensitive()
    {
        // Without LOWER() invitations would silently fail when the inviter
        // doesn't match the casing in the users table.
        Assert.Contains("LOWER(email) = LOWER(@Email)", ProjectQueries.FindUserByEmail);
    }

    [Fact]
    public void ProjectQueries_InsertMember_IsIdempotent()
    {
        // ON CONFLICT DO NOTHING means re-inviting an existing member does
        // not bump their role — promotions go through UpdateMemberRole.
        Assert.Contains("ON CONFLICT (project_id, user_id) DO NOTHING",
            ProjectQueries.InsertMemberReturning);
        Assert.Contains("RETURNING", ProjectQueries.InsertMemberReturning);
    }

    [Fact]
    public void ProjectQueries_UpdateMemberRole_TargetsCompositeKey()
    {
        // Both halves of the PK must be in the WHERE; otherwise we could
        // promote a member in the wrong project.
        Assert.Contains("@ProjectId", ProjectQueries.UpdateMemberRole);
        Assert.Contains("@UserId",    ProjectQueries.UpdateMemberRole);
        Assert.Contains("@Role",      ProjectQueries.UpdateMemberRole);
    }

    [Fact]
    public void ProjectQueries_CountOwners_FiltersByOwnerRole()
    {
        // Last-owner protection relies on this count being scoped.
        Assert.Contains("@ProjectId", ProjectQueries.CountOwners);
        Assert.Contains("role = 'owner'", ProjectQueries.CountOwners);
    }

    [Fact]
    public void EpicQueries_GetWithProgress_ScopedById()
    {
        Assert.Contains("WHERE e.id = @Id", EpicQueries.GetWithProgress);
        Assert.Contains("LEFT JOIN issues", EpicQueries.GetWithProgress);
        Assert.Contains("percent_complete", EpicQueries.GetWithProgress);
    }

    [Fact]
    public void UserQueries_UpdateProfile_UsesCoalesceForPartialUpdates()
    {
        // COALESCE(@Field, field) means a NULL parameter leaves the column
        // alone — required for the PATCH semantics.
        Assert.Contains("COALESCE(@Name", UserQueries.UpdateProfile);
        Assert.Contains("COALESCE(@AvatarUrl", UserQueries.UpdateProfile);
        Assert.Contains("WHERE id = @Id", UserQueries.UpdateProfile);
    }

    // ---- Burndown chart ----

    [Fact]
    public void SprintQueries_Burndown_IsScopedToSprint()
    {
        Assert.Contains("@SprintId", SprintQueries.Burndown);
        Assert.Contains("generate_series", SprintQueries.Burndown);
        Assert.Contains("closed_at", SprintQueries.Burndown);
        Assert.Contains("remaining", SprintQueries.Burndown);
    }

    [Fact]
    public void SprintQueries_Burndown_CapsAtToday()
    {
        // Must not project future dates beyond CURRENT_DATE.
        Assert.Contains("CURRENT_DATE", SprintQueries.Burndown);
        Assert.Contains("LEAST(", SprintQueries.Burndown);
    }

    // ---- Sub-issues ----

    [Fact]
    public void IssueQueries_GetById_IncludesParentId()
    {
        Assert.Contains("parent_id", IssueQueries.GetById);
    }

    [Fact]
    public void IssueQueries_Insert_IncludesParentId()
    {
        Assert.Contains("parent_id", IssueQueries.Insert);
        Assert.Contains("@ParentId", IssueQueries.Insert);
    }

    [Fact]
    public void IssueQueries_Update_HandlesParentIdSentinel()
    {
        // Empty-Guid sentinel must clear parent_id, matching the epic/sprint pattern.
        Assert.Contains("@ParentId", IssueQueries.Update);
        Assert.Contains("00000000-0000-0000-0000-000000000000", IssueQueries.Update);
        Assert.Contains("parent_id", IssueQueries.Update);
    }

    [Fact]
    public void IssueQueries_ChildrenByParent_IsScopedToParent()
    {
        Assert.Contains("@ParentId", IssueQueries.ChildrenByParent);
        Assert.Contains("parent_id = @ParentId", IssueQueries.ChildrenByParent);
    }

    // ---- Per-project issue numbering ----

    [Fact]
    public void IssueQueries_ProjectAllIssueRecords_ProjectTheNumberColumn()
    {
        // The number is server-assigned by the assign_issue_number() trigger
        // and surfaced everywhere the issue is read; if any of these drops the
        // column, Dapper still materializes (Number defaults via ctor) but the
        // API silently returns 0, so pin it here.
        Assert.Contains("number", IssueQueries.Insert);
        Assert.Contains("number", IssueQueries.GetById);
        Assert.Contains("number", IssueQueries.Update);
        Assert.Contains("number", IssueQueries.ChildrenByParent);
        Assert.Contains("i.number", IssueQueries.ListByProject);
        Assert.Contains("'number'", IssueQueries.GetBoard);
    }

    [Fact]
    public void IssueQueries_Insert_DoesNotSupplyNumber_LeavingItToTheTrigger()
    {
        // The VALUES list must NOT bind @Number — allocation is atomic in the
        // BEFORE INSERT trigger. Supplying it from the app would reintroduce
        // the read-modify-write race the trigger exists to eliminate.
        Assert.DoesNotContain("@Number", IssueQueries.Insert);
    }

    // ---- Password reset tokens ----

    [Fact]
    public void PasswordResetTokenQueries_Insert_StoresHashAndExpiry()
    {
        // Plaintext tokens must never hit the DB — Insert only takes a hash.
        Assert.Contains("token_hash", PasswordResetTokenQueries.Insert);
        Assert.Contains("@TokenHash", PasswordResetTokenQueries.Insert);
        Assert.Contains("@UserId",    PasswordResetTokenQueries.Insert);
        Assert.Contains("@ExpiresAt", PasswordResetTokenQueries.Insert);
    }

    [Fact]
    public void PasswordResetTokenQueries_MarkUsed_GuardsAgainstReuse()
    {
        // Idempotency: marking an already-used token affects zero rows so the
        // controller can detect lost races and reject the redemption.
        Assert.Contains("used_at IS NULL", PasswordResetTokenQueries.MarkUsed);
        Assert.Contains("@TokenHash",      PasswordResetTokenQueries.MarkUsed);
    }

    [Fact]
    public void PasswordResetTokenQueries_InvalidateAllForUser_IsScoped()
    {
        Assert.Contains("@UserId",         PasswordResetTokenQueries.InvalidateAllForUser);
        Assert.Contains("used_at IS NULL", PasswordResetTokenQueries.InvalidateAllForUser);
    }

    // ---- Saved Filters ----

    [Fact]
    public void SavedFilterQueries_List_ScopedToUserAndProject()
    {
        Assert.Contains("@ProjectId", SavedFilterQueries.ListByUserProject);
        Assert.Contains("@UserId",    SavedFilterQueries.ListByUserProject);
    }

    [Fact]
    public void SavedFilterQueries_Insert_ReturnsRow()
    {
        Assert.Contains("RETURNING", SavedFilterQueries.Insert);
        Assert.Contains("@ProjectId", SavedFilterQueries.Insert);
        Assert.Contains("@UserId",    SavedFilterQueries.Insert);
        Assert.Contains("@Filters::jsonb", SavedFilterQueries.Insert);
    }

    [Fact]
    public void SavedFilterQueries_FiltersColumn_ProjectedAsText()
    {
        // JSONB must be cast to text so Dapper can bind it to string Filters.
        Assert.Contains("filters::text AS filters", SavedFilterQueries.ListByUserProject);
        Assert.Contains("filters::text AS filters", SavedFilterQueries.Insert);
        Assert.Contains("filters::text AS filters", SavedFilterQueries.GetById);
    }

    [Fact]
    public void SavedFilter_Record_MapsFiltersAsString()
    {
        // Compile-time check: the SavedFilter record has a string Filters property.
        var prop = typeof(SavedFilter).GetProperty("Filters");
        Assert.NotNull(prop);
        Assert.Equal(typeof(string), prop!.PropertyType);
    }
}
