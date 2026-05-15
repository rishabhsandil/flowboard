namespace FlowBoard.Core.Data.Queries;

public static class RefreshTokenQueries
{
    public const string Insert = @"
        INSERT INTO refresh_tokens (id, user_id, expires_at)
        VALUES (@Id, @UserId, @ExpiresAt);";

    /// <summary>
    /// Returns the row if it exists. Caller checks `revoked_at` and `expires_at`
    /// to decide validity — we always read so we can detect re-use of a revoked
    /// token and trigger the chain-revocation path.
    /// </summary>
    public const string GetById = @"
        SELECT id, user_id AS UserId, issued_at AS IssuedAt,
               expires_at AS ExpiresAt, revoked_at AS RevokedAt,
               replaced_by AS ReplacedBy
        FROM refresh_tokens
        WHERE id = @Id;";

    public const string Revoke = @"
        UPDATE refresh_tokens
        SET revoked_at = NOW(), replaced_by = @ReplacedBy
        WHERE id = @Id AND revoked_at IS NULL;";

    /// <summary>
    /// Theft response: revoke every active token for the user. Called when a
    /// revoked-but-replaced token is replayed.
    /// </summary>
    public const string RevokeAllForUser = @"
        UPDATE refresh_tokens
        SET revoked_at = NOW()
        WHERE user_id = @UserId AND revoked_at IS NULL;";
}
