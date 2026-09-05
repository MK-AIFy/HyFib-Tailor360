namespace Tailor360.Platform.Abstractions.Ports;

/// <summary>
/// Sends transactional email. Introduced by #23 for account recovery and consumed later by
/// Notifications (#47).
/// </summary>
/// <remarks>
/// The adapters ship in <c>Identity.Infrastructure</c> for now, because Identity is the only module
/// that sends a message in this release and the shipped adapter is the framework's own SMTP client
/// rather than a provider SDK, so ARCH-009 has nothing to catch. Issue #47 moves them under
/// <c>Integration.Infrastructure</c> along with the first adapter that does carry a vendor SDK.
/// </remarks>
public interface IEmailSender
{
    /// <summary>Sends one message. Implementations must not log the body or the recipient address.</summary>
    Task<Results.Result> SendAsync(EmailMessage message, CancellationToken cancellationToken = default);
}

/// <summary>One outbound email.</summary>
/// <param name="To">Recipient address.</param>
/// <param name="Subject">Subject line.</param>
/// <param name="PlainTextBody">Plain-text body, always supplied.</param>
/// <param name="HtmlBody">Optional HTML body.</param>
/// <param name="Tag">Stable template tag used for delivery reporting, for example <c>account.recovery</c>.</param>
public sealed record EmailMessage(
    string To,
    string Subject,
    string PlainTextBody,
    string? HtmlBody = null,
    string? Tag = null);
