using System.Net;

namespace FlowBoard.Api.Email;

/// <summary>
/// Builds the subject / html / text payloads for the auth-flow emails.
/// Centralised so the wording is identical regardless of which IEmailSender
/// delivers it, and so a future i18n pass has one place to change.
/// </summary>
internal static class EmailMessages
{
    public static (string Subject, string Html, string Text) PasswordReset(
        string toName, string resetUrl, DateTime expiresAtUtc)
    {
        var name = WebUtility.HtmlEncode(toName);
        var url  = WebUtility.HtmlEncode(resetUrl);
        var when = expiresAtUtc.ToString("u");

        var subject = "Reset your FlowBoard password";

        var html = $"""
            <p>Hi {name},</p>
            <p>We received a request to reset your FlowBoard password. Click the link below to choose a new one — it will expire at <strong>{when}</strong>.</p>
            <p><a href="{url}">Reset your password</a></p>
            <p>If you did not request this, you can ignore this email — your password has not changed.</p>
            <p>— FlowBoard</p>
            """;

        var text = $"""
            Hi {toName},

            We received a request to reset your FlowBoard password. Open the link below to choose a new one — it expires at {when}.

            {resetUrl}

            If you did not request this, ignore this email — your password has not changed.

            — FlowBoard
            """;

        return (subject, html, text);
    }

    public static (string Subject, string Html, string Text) PasswordChanged(
        string toName, DateTime changedAtUtc)
    {
        var name = WebUtility.HtmlEncode(toName);
        var when = changedAtUtc.ToString("u");

        var subject = "Your FlowBoard password was changed";

        var html = $"""
            <p>Hi {name},</p>
            <p>Your FlowBoard password was just changed at <strong>{when}</strong>. All active sessions have been signed out.</p>
            <p>If this was you, no further action is needed.</p>
            <p>If this was <strong>not</strong> you, your account may be compromised — reset your password again and contact support.</p>
            <p>— FlowBoard</p>
            """;

        var text = $"""
            Hi {toName},

            Your FlowBoard password was just changed at {when}. All active sessions have been signed out.

            If this was you, no further action is needed.

            If this was NOT you, your account may be compromised — reset your password again and contact support.

            — FlowBoard
            """;

        return (subject, html, text);
    }
}
