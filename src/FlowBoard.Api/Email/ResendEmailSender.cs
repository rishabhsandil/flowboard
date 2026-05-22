using System.Net.Http.Headers;
using System.Net.Http.Json;

namespace FlowBoard.Api.Email;

/// <summary>
/// IEmailSender backed by Resend's transactional HTTP API
/// (https://resend.com/docs/api-reference/emails/send-email). Uses a typed
/// <see cref="HttpClient"/> registered through <c>AddHttpClient</c> so retries
/// / timeouts can be configured at the DI layer without touching this class.
///
/// API key resolution: <c>Email:ResendApiKey</c> in config, override via env
/// var <c>Email__ResendApiKey</c>. Constructor throws if neither is set in
/// non-Development — fail fast at startup rather than silently dropping mail.
/// </summary>
public sealed class ResendEmailSender : IEmailSender
{
    private const string Endpoint = "https://api.resend.com/emails";

    private readonly HttpClient _http;
    private readonly ILogger<ResendEmailSender> _log;
    private readonly string _from;
    private readonly string _apiKey;

    public ResendEmailSender(
        HttpClient http,
        IConfiguration config,
        IHostEnvironment env,
        ILogger<ResendEmailSender> log)
    {
        _http = http;
        _log  = log;

        var apiKey = Environment.GetEnvironmentVariable("Email__ResendApiKey")
                     ?? config["Email:ResendApiKey"];
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            if (env.IsDevelopment())
            {
                // Dev mistake guard: if you picked Resend but didn't set the key
                // we want a clear log line, not silent failures.
                _log.LogWarning(
                    "Email:Provider=resend but no API key configured — emails will fail at send time.");
                apiKey = "missing-api-key";
            }
            else
            {
                throw new InvalidOperationException(
                    "Email:ResendApiKey (or Email__ResendApiKey env var) must be set when Email:Provider = 'resend'.");
            }
        }
        _apiKey = apiKey;

        var fromAddress = config["Email:FromAddress"]
                          ?? throw new InvalidOperationException("Email:FromAddress is required.");
        var fromName    = config["Email:FromName"] ?? "FlowBoard";
        _from = $"{fromName} <{fromAddress}>";
    }

    public async Task SendPasswordResetAsync(
        string toEmail, string toName, string resetUrl, DateTime expiresAtUtc, CancellationToken ct = default)
    {
        var (subject, html, text) = EmailMessages.PasswordReset(toName, resetUrl, expiresAtUtc);
        await SendAsync(toEmail, subject, html, text, ct);
    }

    public async Task SendPasswordChangedAsync(
        string toEmail, string toName, DateTime changedAtUtc, CancellationToken ct = default)
    {
        var (subject, html, text) = EmailMessages.PasswordChanged(toName, changedAtUtc);
        await SendAsync(toEmail, subject, html, text, ct);
    }

    private async Task SendAsync(string to, string subject, string html, string text, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, Endpoint)
        {
            Content = JsonContent.Create(new
            {
                from    = _from,
                to      = new[] { to },
                subject = subject,
                html    = html,
                text    = text,
            }),
        };
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);

        try
        {
            using var res = await _http.SendAsync(req, ct);
            if (!res.IsSuccessStatusCode)
            {
                // Read the body for diagnostics but never echo it to the
                // caller — auth flows must stay opaque.
                var body = await res.Content.ReadAsStringAsync(ct);
                _log.LogError(
                    "Resend send failed: {Status} {Body}",
                    (int)res.StatusCode, body);
            }
            else
            {
                _log.LogInformation(
                    "Resend send ok to={To} subject={Subject}", to, subject);
            }
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Resend send threw to={To} subject={Subject}", to, subject);
        }
    }
}
