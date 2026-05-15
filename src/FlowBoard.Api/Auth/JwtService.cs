using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.Tokens;

namespace FlowBoard.Api.Auth;

public class JwtService
{
    private readonly string _secret;
    private readonly string _refreshSecret;
    private readonly int _accessMinutes;
    private readonly int _refreshDays;

    private static string ResolveSecret(IConfiguration config, IHostEnvironment env, string configKey, string envKey, string devFallback)
    {
        var envValue = Environment.GetEnvironmentVariable(envKey);
        if (!string.IsNullOrWhiteSpace(envValue)) return envValue;

        var configValue = config[configKey];
        if (!string.IsNullOrWhiteSpace(configValue)) return configValue;

        if (env.IsDevelopment()) return devFallback;

        throw new InvalidOperationException($"{configKey} missing or empty");
    }

    public JwtService(IConfiguration config, IHostEnvironment env)
    {
        _secret = ResolveSecret(
            config,
            env,
            "Jwt:Secret",
            "Jwt__Secret",
            "dev-access-secret-change-before-prod-1234567890");
        _refreshSecret = ResolveSecret(
            config,
            env,
            "Jwt:RefreshSecret",
            "Jwt__RefreshSecret",
            "dev-refresh-secret-change-before-prod-0987654321");
        _accessMinutes = int.TryParse(config["Jwt:AccessTokenMinutes"], out var a) ? a : 15;
        _refreshDays   = int.TryParse(config["Jwt:RefreshTokenDays"],   out var r) ? r : 30;
    }

    public string CreateAccessToken(Guid userId)
        => Build(_secret, userId, TimeSpan.FromMinutes(_accessMinutes), jti: null);

    /// <summary>
    /// Creates a refresh JWT and returns the token, the embedded jti (so the
    /// caller can persist it in `refresh_tokens`), and its absolute expiry.
    /// </summary>
    public (string Token, Guid Jti, DateTime ExpiresAt) CreateRefreshToken(Guid userId)
    {
        var jti = Guid.NewGuid();
        var lifetime = TimeSpan.FromDays(_refreshDays);
        var expiresAt = DateTime.UtcNow.Add(lifetime);
        var token = Build(_refreshSecret, userId, lifetime, jti);
        return (token, jti, expiresAt);
    }

    /// <summary>
    /// Validates the refresh-token signature/expiry and extracts (userId, jti).
    /// Returns null on any failure — the caller still needs to check the DB
    /// row for revocation.
    /// </summary>
    public (Guid UserId, Guid Jti)? ValidateRefreshToken(string token)
    {
        // MapInboundClaims=false keeps "sub"/"jti" as-is; otherwise the
        // handler silently rewrites "sub" -> ClaimTypes.NameIdentifier and
        // FindFirstValue(JwtRegisteredClaimNames.Sub) returns null.
        var handler = new JwtSecurityTokenHandler { MapInboundClaims = false };
        try
        {
            var principal = handler.ValidateToken(token, new TokenValidationParameters
            {
                ValidateIssuerSigningKey = true,
                IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_refreshSecret)),
                ValidateIssuer = false,
                ValidateAudience = false,
                ValidateLifetime = true,
                ClockSkew = TimeSpan.FromMinutes(1),
            }, out _);
            var sub = principal.FindFirstValue(JwtRegisteredClaimNames.Sub);
            var jti = principal.FindFirstValue(JwtRegisteredClaimNames.Jti);
            if (!Guid.TryParse(sub, out var userId)) return null;
            if (!Guid.TryParse(jti, out var jtiId))  return null;
            return (userId, jtiId);
        }
        catch { return null; }
    }

    private static string Build(string secret, Guid userId, TimeSpan lifetime, Guid? jti)
    {
        var creds = new SigningCredentials(
            new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secret)),
            SecurityAlgorithms.HmacSha256);

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, userId.ToString()),
        };
        if (jti is { } j) claims.Add(new Claim(JwtRegisteredClaimNames.Jti, j.ToString()));

        var token = new JwtSecurityToken(
            claims: claims,
            expires: DateTime.UtcNow.Add(lifetime),
            signingCredentials: creds);
        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}

public static class ClaimsPrincipalExtensions
{
    public static Guid GetUserId(this System.Security.Claims.ClaimsPrincipal user)
    {
        var sub = user.FindFirstValue(JwtRegisteredClaimNames.Sub)
                  ?? user.FindFirstValue(ClaimTypes.NameIdentifier);
        return Guid.Parse(sub!);
    }
}
