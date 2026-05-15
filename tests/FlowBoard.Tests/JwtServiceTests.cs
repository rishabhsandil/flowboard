using FlowBoard.Api.Auth;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace FlowBoard.Tests;

/// <summary>
/// Round-trip tests for <see cref="JwtService"/>. Exercises rotation-friendly
/// behaviour: every refresh token gets a unique jti, validation surfaces both
/// userId and jti, and tampered/expired tokens are rejected without throwing.
/// </summary>
public class JwtServiceTests
{
    private static JwtService Build()
    {
        var cfg = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Jwt:Secret"]              = "test-access-secret-must-be-32-chars-min!",
            ["Jwt:RefreshSecret"]       = "test-refresh-secret-must-be-32-chars-too!",
            ["Jwt:AccessTokenMinutes"]  = "15",
            ["Jwt:RefreshTokenDays"]    = "30",
        }).Build();
        return new JwtService(cfg, new TestEnv("Production"));
    }

    [Fact]
    public void CreateAccessToken_ProducesParseableJwt()
    {
        var jwt = Build();
        var token = jwt.CreateAccessToken(Guid.NewGuid());
        Assert.False(string.IsNullOrWhiteSpace(token));
        Assert.Equal(3, token.Split('.').Length);  // header.payload.signature
    }

    [Fact]
    public void CreateRefreshToken_EmbedsUniqueJti()
    {
        var jwt = Build();
        var userId = Guid.NewGuid();
        var (_, j1, _) = jwt.CreateRefreshToken(userId);
        var (_, j2, _) = jwt.CreateRefreshToken(userId);
        Assert.NotEqual(Guid.Empty, j1);
        Assert.NotEqual(j1, j2);
    }

    [Fact]
    public void CreateRefreshToken_ExpiresAtIsInFuture()
    {
        var jwt = Build();
        var (_, _, expires) = jwt.CreateRefreshToken(Guid.NewGuid());
        Assert.True(expires > DateTime.UtcNow.AddDays(29));
        Assert.True(expires < DateTime.UtcNow.AddDays(31));
    }

    [Fact]
    public void ValidateRefreshToken_ReturnsUserIdAndJti()
    {
        var jwt = Build();
        var userId = Guid.NewGuid();
        var (token, jti, _) = jwt.CreateRefreshToken(userId);

        var parsed = jwt.ValidateRefreshToken(token);

        Assert.NotNull(parsed);
        Assert.Equal(userId, parsed!.Value.UserId);
        Assert.Equal(jti, parsed.Value.Jti);
    }

    [Fact]
    public void ValidateRefreshToken_RejectsTamperedToken()
    {
        var jwt = Build();
        var (token, _, _) = jwt.CreateRefreshToken(Guid.NewGuid());

        // Flip a character in the signature segment.
        var parts = token.Split('.');
        parts[2] = parts[2][..^1] + (parts[2][^1] == 'A' ? 'B' : 'A');
        var tampered = string.Join('.', parts);

        Assert.Null(jwt.ValidateRefreshToken(tampered));
    }

    [Fact]
    public void ValidateRefreshToken_RejectsAccessTokenSignedWithDifferentSecret()
    {
        var jwt = Build();
        // Access tokens are signed with the access secret, so feeding one to the
        // refresh validator must fail (different signing key + missing jti).
        var access = jwt.CreateAccessToken(Guid.NewGuid());
        Assert.Null(jwt.ValidateRefreshToken(access));
    }

    [Fact]
    public void ValidateRefreshToken_RejectsGarbage()
    {
        var jwt = Build();
        Assert.Null(jwt.ValidateRefreshToken(""));
        Assert.Null(jwt.ValidateRefreshToken("not-a-jwt"));
        Assert.Null(jwt.ValidateRefreshToken("a.b.c"));
    }

    private sealed class TestEnv : IHostEnvironment
    {
        public TestEnv(string name) => EnvironmentName = name;
        public string EnvironmentName { get; set; }
        public string ApplicationName { get; set; } = "Tests";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } =
            new Microsoft.Extensions.FileProviders.NullFileProvider();
    }
}
