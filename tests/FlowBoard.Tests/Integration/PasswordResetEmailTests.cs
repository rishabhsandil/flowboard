using System.Net;
using System.Net.Http.Json;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Dapper;

namespace FlowBoard.Tests.Integration;

/// <summary>
/// Behavioural coverage for the audit-driven changes to the forgot/reset
/// flow:
///   • /auth/forgot kicks the password-reset email to IEmailSender
///   • /auth/forgot invalidates prior unredeemed tokens for the same user
///   • /auth/forgot honours the per-email cooldown (silent 200, no insert)
///   • /auth/reset fires a post-reset notification email to the account
///   • /auth/forgot suppresses the dev-token echo when the config flag is off
///
/// These tests bypass <see cref="IntegrationTestBase"/> because they need a
/// custom <see cref="FlowBoardFactory"/> per scenario (config overrides +
/// a swapped-in <see cref="RecordingEmailSender"/>).
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class PasswordResetEmailTests : IAsyncLifetime
{
    private readonly PostgresFixture _db;
    public PasswordResetEmailTests(PostgresFixture db) { _db = db; }

    public async Task InitializeAsync() => await _db.ResetDataAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Forgot_KnownEmail_DispatchesResetEmailToSender()
    {
        var sink = new RecordingEmailSender();
        await using var factory = NewFactory(sink);

        var (email, _, _) = await RegisterAsync(factory);

        var http = factory.CreateClient();
        SetCsrf(http);
        var res = await http.PostAsJsonAsync("/api/auth/forgot", new { email });
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);

        var got = await sink.WaitForAsync(
            sent => sent.Any(m => m.Kind == "reset" && m.ToEmail == email),
            TimeSpan.FromSeconds(2));
        Assert.True(got, "expected reset email to be dispatched to IEmailSender");

        var reset = sink.Sent.Single(m => m.Kind == "reset");
        Assert.Contains("/reset?token=", reset.ResetUrl);
    }

    [Fact]
    public async Task Forgot_Resend_InvalidatesPriorUnredeemedToken()
    {
        var sink = new RecordingEmailSender();
        await using var factory = NewFactory(sink);

        var (email, userId, _) = await RegisterAsync(factory);

        var http = factory.CreateClient();
        SetCsrf(http);

        var first  = await PostForgotAsync(http, email);
        var firstToken = first.GetProperty("devToken").GetString()!;

        var second = await PostForgotAsync(http, email);
        var secondToken = second.GetProperty("devToken").GetString()!;
        Assert.NotEqual(firstToken, secondToken);

        // The first token's row must now be marked used_at (invalidated). The
        // second token's row must be fresh.
        await using var c = _db.OpenConnection();
        var firstUsed = await c.ExecuteScalarAsync<DateTime?>(
            "SELECT used_at FROM password_reset_tokens WHERE token_hash = @h;",
            new { h = HashToken(firstToken) });
        Assert.NotNull(firstUsed);

        var secondUsed = await c.ExecuteScalarAsync<DateTime?>(
            "SELECT used_at FROM password_reset_tokens WHERE token_hash = @h;",
            new { h = HashToken(secondToken) });
        Assert.Null(secondUsed);

        // Redeeming the (now invalidated) first token must fail; the second
        // one is the only path through.
        var reuse = await http.PostAsJsonAsync("/api/auth/reset",
            new { token = firstToken, newPassword = "Replacement99!" });
        Assert.Equal(HttpStatusCode.BadRequest, reuse.StatusCode);
    }

    [Fact]
    public async Task Forgot_WithinCooldown_SuppressesSecondTokenSilently()
    {
        var sink = new RecordingEmailSender();
        // Cooldown of 60s — far longer than this test's wall-clock duration,
        // so the second call must be silently dropped.
        await using var factory = NewFactory(sink, extra: new()
        {
            ["Forgot:PerEmailCooldownSeconds"] = "60",
        });

        var (email, userId, _) = await RegisterAsync(factory);
        var http = factory.CreateClient();
        SetCsrf(http);

        var first  = await PostForgotAsync(http, email);
        Assert.True(first.TryGetProperty("devToken", out _));

        var second = await PostForgotAsync(http, email);
        // Within cooldown: response is still 200 (no enumeration via 429) and
        // contains no token because the controller never minted one.
        Assert.False(second.TryGetProperty("devToken", out _),
            "expected cooldown to suppress the second token");

        await using var c = _db.OpenConnection();
        var count = await c.ExecuteScalarAsync<long>(
            "SELECT COUNT(*) FROM password_reset_tokens WHERE user_id = @u;",
            new { u = userId });
        Assert.Equal(1, count);
    }

    [Fact]
    public async Task Reset_SuccessfulRedemption_NotifiesAccountHolder()
    {
        var sink = new RecordingEmailSender();
        await using var factory = NewFactory(sink);

        var (email, _, _) = await RegisterAsync(factory);
        var http = factory.CreateClient();
        SetCsrf(http);

        var forgot = await PostForgotAsync(http, email);
        var token  = forgot.GetProperty("devToken").GetString()!;

        var reset = await http.PostAsJsonAsync("/api/auth/reset",
            new { token, newPassword = "NewPass99!" });
        Assert.Equal(HttpStatusCode.NoContent, reset.StatusCode);

        var got = await sink.WaitForAsync(
            sent => sent.Any(m => m.Kind == "changed" && m.ToEmail == email),
            TimeSpan.FromSeconds(2));
        Assert.True(got, "expected post-reset notification email to be dispatched");
    }

    [Fact]
    public async Task Forgot_EchoFlagOff_DoesNotSurfaceDevToken()
    {
        var sink = new RecordingEmailSender();
        await using var factory = NewFactory(sink, extra: new()
        {
            ["Forgot:EchoTokenInResponse"] = "false",
        });

        var (email, _, _) = await RegisterAsync(factory);
        var http = factory.CreateClient();
        SetCsrf(http);

        var res = await http.PostAsJsonAsync("/api/auth/forgot", new { email });
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);

        var body = await res.Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(body.TryGetProperty("devToken", out _),
            "dev-token must NOT be present when Forgot:EchoTokenInResponse is false");

        // The token is still minted (sender receives it via the email URL);
        // we just don't echo it back to the HTTP caller.
        var got = await sink.WaitForAsync(
            sent => sent.Any(m => m.Kind == "reset" && m.ToEmail == email),
            TimeSpan.FromSeconds(2));
        Assert.True(got, "background dispatch should still send the reset email");
    }

    // ---- helpers ------------------------------------------------------

    private FlowBoardFactory NewFactory(
        RecordingEmailSender sink,
        Dictionary<string, string?>? extra = null)
        => new(_db.ConnectionString, extra, sink);

    private static void SetCsrf(HttpClient http)
        => http.DefaultRequestHeaders.Add("X-Requested-With", "XMLHttpRequest");

    private static async Task<JsonElement> PostForgotAsync(HttpClient http, string email)
    {
        var res = await http.PostAsJsonAsync("/api/auth/forgot", new { email });
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        return await res.Content.ReadFromJsonAsync<JsonElement>();
    }

    private static async Task<(string Email, Guid UserId, string Name)> RegisterAsync(FlowBoardFactory factory)
    {
        var suffix = Guid.NewGuid().ToString("N")[..10];
        var email  = $"u{suffix}@example.test";
        var name   = $"User {suffix}";

        var http = factory.CreateClient();
        SetCsrf(http);
        var res = await http.PostAsJsonAsync("/api/auth/register",
            new { email, name, password = "TestPassword123!" });
        res.EnsureSuccessStatusCode();

        var body = await res.Content.ReadFromJsonAsync<JsonElement>();
        var id = Guid.Parse(body.GetProperty("user").GetProperty("id").GetString()!);
        return (email, id, name);
    }

    private static string HashToken(string token)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(token));
        return Convert.ToHexString(bytes);
    }
}
