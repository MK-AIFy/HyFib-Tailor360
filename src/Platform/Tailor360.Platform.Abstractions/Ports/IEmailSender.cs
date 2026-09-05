namespace Tailor360.Platform.Abstractions.Ports;

/// <summary>
/// Sends transactional email. Introduced by #23 for account recovery and consumed later by
/// Notifications (#47); adapters live in Integration.Infrastructure so no vendor SDK leaks into a
/// business module (ARCH-009).
/// </summary>
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
