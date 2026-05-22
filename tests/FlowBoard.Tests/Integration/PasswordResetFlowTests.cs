using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Dapper;

namespace FlowBoard.Tests.Integration;

/// <summary>
/// End-to-end coverage for the forgot/reset password flow:
///   • /auth/forgot is opaque (always 200, no enumeration)
///   • /auth/reset rejects unknown / used / expired tokens
///   • /auth/reset rotates the password and revokes outstanding refresh tokens
/// </summary>
public sealed class PasswordResetFlowTests : IntegrationTestBase
{
    public PasswordResetFlowTests(PostgresFixture db) : base(db) { }

    [Fact]
    public async Task Forgot_UnknownEmail_ReturnsOkAndCreatesNoToken()
    {
        var http = Anon();
        var res = await http.PostAsJsonAsync("/api/auth/forgot",
            new { email = "nobody@example.test" });
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);

        await using var c = Db.OpenConnection();
        var count = await c.ExecuteScalarAsync<long>(
            "SELECT COUNT(*) FROM password_reset_tokens;");
        Assert.Equal(0, count);
    }

    [Fact]
    public async Task Forgot_KnownEmail_ReturnsTokenInDevAndPersistsHash()
    {
        var user = await RegisterUserAsync();

        var http = Anon();
        var res = await http.PostAsJsonAsync("/api/auth/forgot", new { email = user.Email });
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);

        var body = await res.Content.ReadFromJsonAsync<JsonElement>();
        var token = body.GetProperty("devToken").GetString()!;
        Assert.False(string.IsNullOrWhiteSpace(token));

        await using var c = Db.OpenConnection();
        var hash = HashToken(token);
        var row = await c.QuerySingleAsync<(string TokenHash, Guid UserId, DateTime? UsedAt)>(
            "SELECT token_hash, user_id, used_at FROM password_reset_tokens WHERE token_hash = @h;",
            new { h = hash });
        Assert.Equal(user.UserId, row.UserId);
        Assert.Null(row.UsedAt);
    }

    [Fact]
    public async Task Reset_ValidToken_ChangesPasswordAndRevokesRefresh()
    {
        var user = await RegisterUserAsync();

        var http = Anon();
        var forgot = await http.PostAsJsonAsync("/api/auth/forgot", new { email = user.Email });
        var body   = await forgot.Content.ReadFromJsonAsync<JsonElement>();
        var token  = body.GetProperty("devToken").GetString()!;

        var res = await http.PostAsJsonAsync("/api/auth/reset",
            new { token, newPassword = "BrandNewPass99!" });
        Assert.Equal(HttpStatusCode.NoContent, res.StatusCode);

        // Old credentials should no longer work.
        var oldLogin = await http.PostAsJsonAsync("/api/auth/login",
            new { email = user.Email, password = "TestPassword123!" });
        Assert.Equal(HttpStatusCode.Unauthorized, oldLogin.StatusCode);

        // New credentials should work.
        var newLogin = await http.PostAsJsonAsync("/api/auth/login",
            new { email = user.Email, password = "BrandNewPass99!" });
        Assert.Equal(HttpStatusCode.OK, newLogin.StatusCode);

        // Every previously issued refresh token for the user must be revoked.
        await using var c = Db.OpenConnection();
        var activeRefresh = await c.ExecuteScalarAsync<long>(
            "SELECT COUNT(*) FROM refresh_tokens WHERE user_id = @u AND revoked_at IS NULL;",
            new { u = user.UserId });
        // The new login above just minted a fresh refresh token; only that one
        // should be active.
        Assert.Equal(1, activeRefresh);
    }

    [Fact]
    public async Task Reset_ReusedToken_IsRejected()
    {
        var user = await RegisterUserAsync();
        var http = Anon();
        var forgot = await http.PostAsJsonAsync("/api/auth/forgot", new { email = user.Email });
        var token  = (await forgot.Content.ReadFromJsonAsync<JsonElement>())
                     .GetProperty("devToken").GetString()!;

        var first = await http.PostAsJsonAsync("/api/auth/reset",
            new { token, newPassword = "First99Password!" });
        Assert.Equal(HttpStatusCode.NoContent, first.StatusCode);

        var second = await http.PostAsJsonAsync("/api/auth/reset",
            new { token, newPassword = "Second99Password!" });
        Assert.Equal(HttpStatusCode.BadRequest, second.StatusCode);
        var err = await second.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("invalid_reset_token", err.GetProperty("error").GetString());
    }

    [Fact]
    public async Task Reset_BogusToken_IsRejected()
    {
        var http = Anon();
        var res = await http.PostAsJsonAsync("/api/auth/reset",
            new { token = "this-token-was-never-issued-123456", newPassword = "doesNotMatter1!" });
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
    }

    [Fact]
    public async Task Reset_ExpiredToken_IsRejected()
    {
        var user = await RegisterUserAsync();
        var http = Anon();
        var forgot = await http.PostAsJsonAsync("/api/auth/forgot", new { email = user.Email });
        var token  = (await forgot.Content.ReadFromJsonAsync<JsonElement>())
                     .GetProperty("devToken").GetString()!;

        // Backdate the expiry to simulate the 1 h TTL passing.
        await using (var c = Db.OpenConnection())
        {
            await c.ExecuteAsync(
                "UPDATE password_reset_tokens SET expires_at = NOW() - INTERVAL '1 minute' WHERE token_hash = @h;",
                new { h = HashToken(token) });
        }

        var res = await http.PostAsJsonAsync("/api/auth/reset",
            new { token, newPassword = "AfterExpiry99!" });
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
    }

    private static string HashToken(string token)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(token));
        return Convert.ToHexString(bytes);
    }
}
