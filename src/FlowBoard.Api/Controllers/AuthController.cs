using BCrypt.Net;
using Dapper;
using FlowBoard.Api.Auth;
using FlowBoard.Api.Models;
using FlowBoard.Core.Data;
using FlowBoard.Core.Data.Queries;
using FlowBoard.Core.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

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
