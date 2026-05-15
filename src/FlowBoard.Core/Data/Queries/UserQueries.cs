namespace FlowBoard.Core.Data.Queries;

public static class UserQueries
{
    public const string Insert = @"
        INSERT INTO users (email, name, password_hash)
        VALUES (@Email, @Name, @PasswordHash)
        RETURNING id, email, name, avatar_url, created_at;";

    public const string GetByEmail = @"
        SELECT id, email, name, avatar_url, password_hash, created_at
        FROM users WHERE email = @Email;";

    public const string GetById = @"
        SELECT id, email, name, avatar_url, created_at
        FROM users WHERE id = @Id;";

    public const string GetByIdWithHash = @"
        SELECT id, email, name, avatar_url, password_hash, created_at
        FROM users WHERE id = @Id;";

    public const string EmailExists = @"SELECT 1 FROM users WHERE email = @Email LIMIT 1;";

    /// <summary>
    /// Partial profile update via COALESCE — pass NULL on a field to leave
    /// it untouched. Email + password are NOT editable here; those need
    /// dedicated flows (verification, current-password challenge).
    /// </summary>
    public const string UpdateProfile = @"
        UPDATE users SET
          name       = COALESCE(@Name,      name),
          avatar_url = COALESCE(@AvatarUrl, avatar_url)
        WHERE id = @Id
        RETURNING id, email, name, avatar_url, created_at;";

    public const string UpdatePassword = @"
        UPDATE users SET password_hash = @PasswordHash WHERE id = @Id;";
}
