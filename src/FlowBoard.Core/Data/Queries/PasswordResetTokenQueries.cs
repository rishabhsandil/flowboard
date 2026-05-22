namespace FlowBoard.Core.Data.Queries;

/// <summary>
/// Queries for the forgot/reset password flow. The PK is the SHA-256 hash
/// of the random token — we never store plaintext, so a DB read alone does
/// not let an attacker redeem outstanding links.
/// </summary>
public static class PasswordResetTokenQueries
{
    public const string Insert = @"
        INSERT INTO password_reset_tokens (token_hash, user_id, expires_at)
        VALUES (@TokenHash, @UserId, @ExpiresAt);";

    /// <summary>
    /// Look up a token by hash. Caller checks `used_at` and `expires_at` to
    /// decide validity (so the API can distinguish expired vs already-used
    /// in logs if it ever needs to).
    /// </summary>
    public const string GetByHash = @"
        SELECT token_hash AS TokenHash,
               user_id    AS UserId,
               expires_at AS ExpiresAt,
               used_at    AS UsedAt,
               created_at AS CreatedAt
        FROM password_reset_tokens
        WHERE token_hash = @TokenHash;";

    /// <summary>
    /// Mark a token as redeemed. Used inside the reset transaction so a
    /// concurrent second redemption hits the `used_at IS NULL` guard and
    /// affects zero rows.
    /// </summary>
    public const string MarkUsed = @"
        UPDATE password_reset_tokens
        SET used_at = NOW()
        WHERE token_hash = @TokenHash AND used_at IS NULL;";

    /// <summary>
    /// Invalidate every outstanding reset link for a user. Called both
    /// after a successful reset (so a second leaked link cannot be redeemed)
    /// AND before issuing a new token on /forgot (so only the most recent
    /// link in the user's inbox is redeemable).
    /// </summary>
    public const string InvalidateAllForUser = @"
        UPDATE password_reset_tokens
        SET used_at = NOW()
        WHERE user_id = @UserId AND used_at IS NULL;";

    /// <summary>
    /// Most-recent token issuance time for a user, regardless of used_at.
    /// Used to enforce the per-email cooldown on /forgot — if the last
    /// issuance is within the cooldown window we suppress the new one
    /// silently (still returning 200 to preserve the enumeration defence).
    /// </summary>
    public const string MostRecentCreatedAtForUser = @"
        SELECT MAX(created_at)
        FROM password_reset_tokens
        WHERE user_id = @UserId;";

    /// <summary>
    /// Periodic cleanup: drop rows that are either used or expired and
    /// were created before <c>@Cutoff</c>. The caller chooses the cutoff
    /// (typically NOW() − 7d) so recently-redeemed rows linger briefly for
    /// log correlation. Returns the row count for telemetry.
    /// </summary>
    public const string DeleteStaleRows = @"
        DELETE FROM password_reset_tokens
        WHERE (used_at IS NOT NULL OR expires_at < NOW())
          AND created_at < @Cutoff;";
}
