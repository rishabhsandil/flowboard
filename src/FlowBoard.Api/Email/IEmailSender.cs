namespace FlowBoard.Api.Email;

/// <summary>
/// Abstraction over the transactional mail provider so the auth flow does
/// not bind to a specific vendor. Two implementations ship in-tree:
///   • <see cref="LogEmailSender"/>  — writes the email to ILogger; default in
///     Development and tests, lets the flow be exercised without an API key.
///   • <see cref="ResendEmailSender"/> — POSTs to https://api.resend.com/emails;
///     selected when <c>Email:Provider = "resend"</c>.
/// Senders are expected to be safe to call from background tasks (fire-and-
/// forget). Implementations swallow non-fatal transport errors and log them
/// — a failed reset email must never bubble a 500 to the user, since the
/// caller may be on the "unknown email" branch where we still want a 200.
/// </summary>
public interface IEmailSender
{
    /// <summary>Send the "click here to reset" link.</summary>
    Task SendPasswordResetAsync(
        string toEmail,
        string toName,
        string resetUrl,
        DateTime expiresAtUtc,
        CancellationToken ct = default);

    /// <summary>
    /// Send the post-reset confirmation so the legitimate account holder is
    /// alerted if a takeover just happened. Contains no token / no link to
    /// act on — the alert itself is the point.
    /// </summary>
    Task SendPasswordChangedAsync(
        string toEmail,
        string toName,
        DateTime changedAtUtc,
        CancellationToken ct = default);
}
