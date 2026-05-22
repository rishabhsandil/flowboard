using System.Security.Cryptography;
using System.Text;
using BCrypt.Net;
using Dapper;
using FlowBoard.Api.Auth;
using FlowBoard.Api.Email;
using FlowBoard.Api.Models;
using FlowBoard.Core.Data;
using FlowBoard.Core.Data.Queries;
using FlowBoard.Core.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Npgsql;

namespace FlowBoard.Api.Controllers;

[ApiController]
[Route("api/auth")]
public class AuthController : ControllerBase
{
    private readonly DbConnectionFactory _db;
    private readonly JwtService _jwt;
    private readonly ILogger<AuthController> _log;

    public AuthController(DbConnectionFactory db, JwtService jwt, ILogger<AuthController> log)
    {
        _db = db; _jwt = jwt; _log = log;
    }

    /// <summary>Issues a refresh token and persists its jti row in `refresh_tokens`.</summary>
    private async Task<string> IssueRefreshTokenAsync(System.Data.IDbConnection c, Guid userId)
    {
        var (token, jti, expires) = _jwt.CreateRefreshToken(userId);
        await c.ExecuteAsync(RefreshTokenQueries.Insert, new
        {
            Id = jti, UserId = userId, ExpiresAt = expires
        });
        return token;
    }

    [HttpPost("register")]
    [EnableRateLimiting("auth-loose")]
    public async Task<IActionResult> Register(RegisterRequest req)
    {
        using var c = _db.Create();
        var existing = await c.ExecuteScalarAsync<int?>(UserQueries.EmailExists, new { req.Email });
        if (existing.HasValue) return Conflict(new { error = "email_in_use" });

        // BCrypt cost factor 12 ≈ 250ms per hash on 2026-era hardware. Tune up over time.
        var hash = BCrypt.Net.BCrypt.HashPassword(req.Password, workFactor: 12);
        var user = await c.QuerySingleAsync<User>(UserQueries.Insert, new
        {
            req.Email, req.Name, PasswordHash = hash
        });

        return Created($"/api/auth/me", new AuthResponse(
            user,
            _jwt.CreateAccessToken(user.Id),
            await IssueRefreshTokenAsync(c, user.Id)
        ));
    }

    [HttpPost("login")]
    [EnableRateLimiting("auth-strict")]
    public async Task<IActionResult> Login(LoginRequest req)
    {
        using var c = _db.Create();
        var user = await c.QuerySingleOrDefaultAsync<UserWithHash>(
            UserQueries.GetByEmail, new { req.Email });
        if (user is null || !BCrypt.Net.BCrypt.Verify(req.Password, user.PasswordHash))
            return Unauthorized(new { error = "invalid_credentials" });

        return Ok(new AuthResponse(
            new User(user.Id, user.Email, user.Name, user.AvatarUrl, user.CreatedAt),
            _jwt.CreateAccessToken(user.Id),
            await IssueRefreshTokenAsync(c, user.Id)
        ));
    }

    [HttpPost("refresh")]
    [EnableRateLimiting("auth-strict")]
    public async Task<IActionResult> Refresh(RefreshRequest req)
    {
        var parsed = _jwt.ValidateRefreshToken(req.RefreshToken);
        if (parsed is null) return Unauthorized(new { error = "invalid_refresh_token" });
        var (userId, jti) = parsed.Value;

        using var c = _db.Create();
        var row = await c.QuerySingleOrDefaultAsync<RefreshTokenRow>(
            RefreshTokenQueries.GetById, new { Id = jti });

        // Unknown jti — token was never issued by us (or already deleted).
        if (row is null) return Unauthorized(new { error = "invalid_refresh_token" });

        // Replay of an already-rotated token. Treat as theft: revoke the
        // entire chain for that user and force re-login.
        if (row.RevokedAt is not null)
        {
            _log.LogWarning("Refresh token replay detected for user {UserId} jti {Jti}", userId, jti);
            await c.ExecuteAsync(RefreshTokenQueries.RevokeAllForUser, new { UserId = userId });
            return Unauthorized(new { error = "refresh_token_replayed" });
        }

        if (row.ExpiresAt <= DateTime.UtcNow)
            return Unauthorized(new { error = "refresh_token_expired" });

        // Rotate: mint new tokens, then revoke the old one with a back-pointer.
        var (newToken, newJti, newExpires) = _jwt.CreateRefreshToken(userId);
        await c.ExecuteAsync(RefreshTokenQueries.Insert, new
        {
            Id = newJti, UserId = userId, ExpiresAt = newExpires
        });
        await c.ExecuteAsync(RefreshTokenQueries.Revoke, new { Id = jti, ReplacedBy = newJti });

        return Ok(new
        {
            accessToken  = _jwt.CreateAccessToken(userId),
            refreshToken = newToken,
        });
    }

    [Authorize]
    [HttpPost("logout")]
    public async Task<IActionResult> Logout(RefreshRequest req)
    {
        // Best-effort revoke — invalid/expired tokens still return 204 so
        // logout is idempotent from the client's perspective.
        var parsed = _jwt.ValidateRefreshToken(req.RefreshToken);
        if (parsed is { } p)
        {
            using var c = _db.Create();
            await c.ExecuteAsync(RefreshTokenQueries.Revoke, new { Id = p.Jti, ReplacedBy = (Guid?)null });
        }
        return NoContent();
    }

    /// <summary>
    /// Forgot-password kickoff. Always returns 200 regardless of whether the
    /// email is registered — this prevents account enumeration via status-code
    /// differences. The real work (DB writes + email dispatch) runs in a
    /// background task so the response latency is independent of whether the
    /// email matched, defeating the timing side-channel that would otherwise
    /// leak the answer (audit #1).
    ///
    /// In Development, when <c>Forgot:EchoTokenInResponse</c> is set, the
    /// flow runs inline and echoes the plaintext token in the response so
    /// the e2e tests / dev UI can complete without a real mailer. Production
    /// always takes the background path even if ASPNETCORE_ENVIRONMENT is
    /// accidentally set to Development (audit #8).
    /// </summary>
    [HttpPost("forgot")]
    [EnableRateLimiting("auth-strict")]
    public async Task<IActionResult> Forgot(
        ForgotPasswordRequest req,
        [FromServices] IHostEnvironment env,
        [FromServices] IConfiguration config,
        [FromServices] IEmailSender emailer)
    {
        var echoToken = env.IsDevelopment()
                        && config.GetValue<bool>("Forgot:EchoTokenInResponse");

        if (echoToken)
        {
            // Dev / test path: token surfaces in the response so callers can
            // complete the flow without SMTP. Stays inline because the token
            // must be in the synchronous response body.
            return await ForgotInlineAsync(req.Email, config, emailer);
        }

        // Production path: dispatch every branch into the background so the
        // response time is constant regardless of whether the email matches.
        var emailCopy = req.Email; // capture for the background closure
        _ = Task.Run(() => ForgotBackgroundAsync(emailCopy, config, emailer));
        return Ok(new { ok = true });
    }

    private async Task<IActionResult> ForgotInlineAsync(
        string email, IConfiguration config, IEmailSender emailer)
    {
        using var c = _db.Create();
        var user = await c.QuerySingleOrDefaultAsync<UserWithHash>(
            UserQueries.GetByEmail, new { Email = email });
        if (user is null)
        {
            _log.LogInformation("Password reset requested for unknown email (dev-echo path)");
            return Ok(new { ok = true });
        }

        if (await IsWithinCooldownAsync(c, user.Id, config))
        {
            _log.LogInformation("Password reset suppressed (cooldown) for user {UserId}", user.Id);
            return Ok(new { ok = true });
        }

        var (token, expires) = await IssueResetTokenAsync(c, user.Id);
        var url = BuildResetUrl(config, token);
        await emailer.SendPasswordResetAsync(user.Email, user.Name, url, expires);
        return Ok(new { ok = true, devToken = token });
    }

    private async Task ForgotBackgroundAsync(
        string email, IConfiguration config, IEmailSender emailer)
    {
        try
        {
            using var c = _db.Create();
            var user = await c.QuerySingleOrDefaultAsync<UserWithHash>(
                UserQueries.GetByEmail, new { Email = email });
            if (user is null)
            {
                _log.LogInformation("Password reset requested for unknown email");
                return;
            }

            if (await IsWithinCooldownAsync(c, user.Id, config))
            {
                _log.LogInformation("Password reset suppressed (cooldown) for user {UserId}", user.Id);
                return;
            }

            var (token, expires) = await IssueResetTokenAsync(c, user.Id);
            var url = BuildResetUrl(config, token);
            await emailer.SendPasswordResetAsync(user.Email, user.Name, url, expires);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Background forgot-password dispatch failed");
        }
    }

    /// <summary>
    /// Per-email throttle: silently drop a /forgot if the same user already
    /// got a token within the cooldown window. Keeps a malicious or buggy
    /// loop from spamming the user's inbox and from minting unlimited live
    /// tokens (audit #2). Returns false when the cooldown is configured to
    /// zero, which is the test default.
    /// </summary>
    private static async Task<bool> IsWithinCooldownAsync(
        NpgsqlConnection c, Guid userId, IConfiguration config)
    {
        var seconds = config.GetValue("Forgot:PerEmailCooldownSeconds", 60);
        if (seconds <= 0) return false;

        var lastIssued = await c.ExecuteScalarAsync<DateTime?>(
            PasswordResetTokenQueries.MostRecentCreatedAtForUser,
            new { UserId = userId });
        return lastIssued is not null
            && DateTime.UtcNow - lastIssued.Value < TimeSpan.FromSeconds(seconds);
    }

    /// <summary>
    /// Generates a token, invalidates any outstanding tokens for the user so
    /// only the most recent link in their inbox is redeemable (audit #2),
    /// and persists the new hash. Returns the plaintext + absolute expiry so
    /// the caller can mail the link.
    /// </summary>
    private async Task<(string Token, DateTime ExpiresAt)> IssueResetTokenAsync(
        NpgsqlConnection c, Guid userId)
    {
        var token   = GenerateResetToken();
        var hash    = HashToken(token);
        var expires = DateTime.UtcNow.AddHours(1);

        await c.ExecuteAsync(
            PasswordResetTokenQueries.InvalidateAllForUser, new { UserId = userId });
        await c.ExecuteAsync(PasswordResetTokenQueries.Insert, new
        {
            TokenHash = hash, UserId = userId, ExpiresAt = expires,
        });
        _log.LogInformation(
            "Password reset token issued for user {UserId} (expires {ExpiresAt:o})",
            userId, expires);
        return (token, expires);
    }

    private static string BuildResetUrl(IConfiguration config, string token)
    {
        var baseUrl = (config["Email:FrontendBaseUrl"] ?? "http://localhost:5173")
                      .TrimEnd('/');
        return $"{baseUrl}/reset?token={Uri.EscapeDataString(token)}";
    }

    /// <summary>
    /// Redeem a reset token: re-hash the supplied plaintext, look it up, and
    /// (atomically inside a transaction) flip the password, mark the token
    /// used, invalidate any sibling reset tokens, and revoke every refresh
    /// token for the user so all live sessions die. After commit, fire a
    /// post-reset notification email out-of-band (audit #3).
    /// </summary>
    [HttpPost("reset")]
    [EnableRateLimiting("auth-strict")]
    public async Task<IActionResult> Reset(
        ResetPasswordRequest req,
        [FromServices] IEmailSender emailer)
    {
        var hash = HashToken(req.Token);

        using var c = _db.Create();
        // Open once at the top so the lookup, the user fetch, and the tx all
        // run on the same already-open connection (audit #12). Dapper would
        // open-and-close otherwise, leaving the explicit BeginTransactionAsync
        // pattern brittle.
        await c.OpenAsync();

        var row = await c.QuerySingleOrDefaultAsync<PasswordResetTokenRow>(
            PasswordResetTokenQueries.GetByHash, new { TokenHash = hash });

        // Distinct log lines per rejection reason so token-fishing shows up in
        // monitoring without leaking the reason to the caller (audit #7).
        if (row is null)
        {
            _log.LogWarning("Password reset rejected: token not found");
            return BadRequest(new { error = "invalid_reset_token" });
        }
        if (row.UsedAt is not null)
        {
            _log.LogWarning(
                "Password reset rejected: token already used (user {UserId})", row.UserId);
            return BadRequest(new { error = "invalid_reset_token" });
        }
        if (row.ExpiresAt <= DateTime.UtcNow)
        {
            _log.LogWarning(
                "Password reset rejected: token expired (user {UserId})", row.UserId);
            return BadRequest(new { error = "invalid_reset_token" });
        }

        var newHash = BCrypt.Net.BCrypt.HashPassword(req.NewPassword, workFactor: 12);

        // Pull the user up front so we have email + name for the notification
        // without a second lookup post-commit.
        var user = await c.QuerySingleOrDefaultAsync<UserWithHash>(
            UserQueries.GetByIdWithHash, new { Id = row.UserId });

        using var tx = await c.BeginTransactionAsync();
        try
        {
            var marked = await c.ExecuteAsync(
                PasswordResetTokenQueries.MarkUsed, new { TokenHash = hash }, tx);
            if (marked == 0)
            {
                // Lost the race with a concurrent redemption.
                await tx.RollbackAsync();
                _log.LogWarning(
                    "Password reset rejected: concurrent redemption race (user {UserId})",
                    row.UserId);
                return BadRequest(new { error = "invalid_reset_token" });
            }

            await c.ExecuteAsync(UserQueries.UpdatePassword,
                new { Id = row.UserId, PasswordHash = newHash }, tx);
            await c.ExecuteAsync(PasswordResetTokenQueries.InvalidateAllForUser,
                new { UserId = row.UserId }, tx);
            await c.ExecuteAsync(RefreshTokenQueries.RevokeAllForUser,
                new { UserId = row.UserId }, tx);

            await tx.CommitAsync();
        }
        catch
        {
            await tx.RollbackAsync();
            throw;
        }

        _log.LogInformation("Password reset completed for user {UserId}", row.UserId);

        // Out-of-band notification so the account holder is alerted even if
        // the resetter is an attacker (audit #3). Fire-and-forget — a slow
        // or failing mailer must not block / fail the reset itself.
        if (user is not null)
        {
            var changedAt = DateTime.UtcNow;
            var notifyEmail = user.Email;
            var notifyName  = user.Name;
            _ = Task.Run(async () =>
            {
                try
                {
                    await emailer.SendPasswordChangedAsync(notifyEmail, notifyName, changedAt);
                }
                catch (Exception ex)
                {
                    _log.LogError(ex, "Password-changed notification failed");
                }
            });
        }

        return NoContent();
    }

    // --- token helpers -------------------------------------------------------

    /// <summary>
    /// 32 cryptographically-random bytes encoded as URL-safe base64. Roughly
    /// 256 bits of entropy — wide enough to make hash collisions a non-issue
    /// and to defeat brute-force redemption attempts within the 1 h TTL.
    /// </summary>
    private static string GenerateResetToken()
    {
        var bytes = RandomNumberGenerator.GetBytes(32);
        return Convert.ToBase64String(bytes)
            .Replace('+', '-').Replace('/', '_').TrimEnd('=');
    }

    private static string HashToken(string token)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(token));
        return Convert.ToHexString(bytes);
    }

    [Authorize]
    [HttpGet("me")]
    public async Task<IActionResult> Me()
    {
        var id = User.GetUserId();
        using var c = _db.Create();
        var user = await c.QuerySingleOrDefaultAsync<User>(UserQueries.GetById, new { Id = id });
        return user is null ? NotFound() : Ok(new { user });
    }

    /// <summary>
    /// Update the current user's profile (name + optional avatar URL).
    /// Email isn't editable here on purpose — that needs a verification
    /// flow before we let an account swap addresses.
    /// </summary>
    [Authorize]
    [HttpPatch("me")]
    public async Task<IActionResult> UpdateMe(UpdateProfileRequest req)
    {
        var id = User.GetUserId();
        using var c = _db.Create();
        var user = await c.QuerySingleAsync<User>(UserQueries.UpdateProfile, new
        {
            Id = id,
            req.Name,
            req.AvatarUrl,
        });
        return Ok(new { user });
    }

    /// <summary>
    /// Change password. Requires the current password to defend against
    /// hijacked-session takeover. On success we revoke every refresh token
    /// for this user so other devices need to re-authenticate.
    /// </summary>
    [Authorize]
    [HttpPost("me/password")]
    public async Task<IActionResult> ChangePassword(ChangePasswordRequest req)
    {
        var id = User.GetUserId();
        using var c = _db.Create();
        var existing = await c.QuerySingleOrDefaultAsync<UserWithHash>(
            UserQueries.GetByIdWithHash, new { Id = id });
        if (existing is null) return Unauthorized();
        if (!BCrypt.Net.BCrypt.Verify(req.CurrentPassword, existing.PasswordHash))
            return Unauthorized(new { error = "invalid_credentials" });

        var newHash = BCrypt.Net.BCrypt.HashPassword(req.NewPassword, workFactor: 12);
        await c.ExecuteAsync(UserQueries.UpdatePassword, new { Id = id, PasswordHash = newHash });
        await c.ExecuteAsync(RefreshTokenQueries.RevokeAllForUser, new { UserId = id });
        return NoContent();
    }
}
