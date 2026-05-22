using System.Collections.Concurrent;
using FlowBoard.Api.Email;

namespace FlowBoard.Tests.Integration;

/// <summary>
/// In-memory <see cref="IEmailSender"/> for integration tests. Each call is
/// appended to <see cref="Sent"/> so the test can assert which mails were
/// dispatched without binding to a real provider. Thread-safe — the auth
/// flow can call this from a background Task.
/// </summary>
public sealed class RecordingEmailSender : IEmailSender
{
    public sealed record SentMail(
        string Kind,
        string ToEmail,
        string ToName,
        string? ResetUrl,
        DateTime At);

    public ConcurrentBag<SentMail> Sent { get; } = new();

    public Task SendPasswordResetAsync(
        string toEmail, string toName, string resetUrl, DateTime expiresAtUtc, CancellationToken ct = default)
    {
        Sent.Add(new SentMail("reset", toEmail, toName, resetUrl, expiresAtUtc));
        return Task.CompletedTask;
    }

    public Task SendPasswordChangedAsync(
        string toEmail, string toName, DateTime changedAtUtc, CancellationToken ct = default)
    {
        Sent.Add(new SentMail("changed", toEmail, toName, null, changedAtUtc));
        return Task.CompletedTask;
    }

    /// <summary>
    /// Polls the recorder until <paramref name="predicate"/> is satisfied or
    /// the timeout elapses. Used to bridge fire-and-forget background sends:
    /// the controller queues the email after returning the HTTP response, so
    /// the assertion has to wait briefly for the task to land.
    /// </summary>
    public async Task<bool> WaitForAsync(Func<IReadOnlyCollection<SentMail>, bool> predicate, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (predicate(Sent.ToArray())) return true;
            await Task.Delay(25);
        }
        return predicate(Sent.ToArray());
    }
}
