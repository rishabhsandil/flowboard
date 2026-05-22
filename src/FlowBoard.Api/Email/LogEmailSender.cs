namespace FlowBoard.Api.Email;

/// <summary>
/// IEmailSender implementation that writes the message to the logger instead
/// of delivering it. Default in Development and tests so the flow can be
/// exercised without a real SMTP / API integration. In Production this is
/// only selected when the operator explicitly sets <c>Email:Provider = "log"</c>
/// — the more common Production choice is <see cref="ResendEmailSender"/>.
/// </summary>
public sealed class LogEmailSender : IEmailSender
{
    private readonly ILogger<LogEmailSender> _log;

    public LogEmailSender(ILogger<LogEmailSender> log) => _log = log;

    public Task SendPasswordResetAsync(
        string toEmail, string toName, string resetUrl, DateTime expiresAtUtc, CancellationToken ct = default)
    {
        // Do NOT log the token-bearing URL at Information by default in prod —
        // anyone with log access could redeem the reset. We emit at Debug so
        // dev consoles still show it, but aggregators usually filter it out.
        _log.LogDebug(
            "[email/dev] PasswordReset to {Email} (name={Name}) url={Url} expiresAt={ExpiresAt:o}",
            toEmail, toName, resetUrl, expiresAtUtc);
        _log.LogInformation(
            "[email/dev] PasswordReset queued for {Email} (expiresAt={ExpiresAt:o})",
            toEmail, expiresAtUtc);
        return Task.CompletedTask;
    }

    public Task SendPasswordChangedAsync(
        string toEmail, string toName, DateTime changedAtUtc, CancellationToken ct = default)
    {
        _log.LogInformation(
            "[email/dev] PasswordChanged notification queued for {Email} (changedAt={ChangedAt:o})",
            toEmail, changedAtUtc);
        return Task.CompletedTask;
    }
}
