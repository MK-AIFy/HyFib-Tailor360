using Microsoft.Extensions.Logging;

namespace Tailor360.Modules.Identity.Application;

/// <summary>
/// Source-generated log messages for the Identity application layer.
/// </summary>
/// <remarks>
/// Every message here is written to be safe to read. None of them carries an email address, a token, a
/// code, a secret or a password: an account appears as its identifier and nothing else, which is what
/// <c>docs/nfr/data-classification.md</c> requires of a log line and what makes these messages usable
/// during an incident without a further access decision.
/// </remarks>
internal static partial class IdentityLog
{
    [LoggerMessage(EventId = 2301, Level = LogLevel.Information,
        Message = "Issued a recovery link for account {UserId}, valid for {LifetimeMinutes} minute(s).")]
    public static partial void RecoveryLinkIssued(ILogger logger, Guid userId, double lifetimeMinutes);

    [LoggerMessage(EventId = 2302, Level = LogLevel.Information,
        Message = "A recovery request named an address that is not an account that can be recovered. " +
                  "No message was sent and the caller was answered exactly as a known address would be.")]
    public static partial void RecoveryRequestedForUnknownAddress(ILogger logger);

    [LoggerMessage(EventId = 2303, Level = LogLevel.Information,
        Message = "Account {UserId} completed a password recovery; {SessionCount} session(s) were ended. " +
                  "The account's second factor was not altered.")]
    public static partial void RecoveryCompleted(ILogger logger, Guid userId, int sessionCount);

    [LoggerMessage(EventId = 2304, Level = LogLevel.Error,
        Message = "Identity:Recovery:PublicBaseUrl is not configured, so the link in this message is " +
                  "relative and nobody will be able to follow it from a mail client. Password recovery " +
                  "is not working in this deployment. Set it to the address staff reach this " +
                  "deployment on.")]
    public static partial void PublicBaseUrlMissing(ILogger logger);

    [LoggerMessage(EventId = 2305, Level = LogLevel.Error,
        Message = "The {TemplateKey} message could not be rendered ({Reason}); nothing was sent.")]
    public static partial void TemplateRenderFailed(ILogger logger, string templateKey, string reason);

    [LoggerMessage(EventId = 2306, Level = LogLevel.Error,
        Message = "The {TemplateKey} message could not be queued for delivery; the queue is full.")]
    public static partial void MessageNotQueued(ILogger logger, string templateKey);

    [LoggerMessage(EventId = 2310, Level = LogLevel.Information,
        Message = "Account {UserId} started an authenticator enrolment.")]
    public static partial void TotpEnrolmentStarted(ILogger logger, Guid userId);

    [LoggerMessage(EventId = 2311, Level = LogLevel.Information,
        Message = "Account {UserId} confirmed an authenticator and was issued {CodeCount} recovery code(s).")]
    public static partial void TotpEnrolmentConfirmed(ILogger logger, Guid userId, int codeCount);

    [LoggerMessage(EventId = 2312, Level = LogLevel.Warning,
        Message = "Account {UserId} has {Remaining} unspent recovery code(s) left and should print a new sheet.")]
    public static partial void RecoveryCodesRunningLow(ILogger logger, Guid userId, int remaining);

    [LoggerMessage(EventId = 2314, Level = LogLevel.Information,
        Message = "Account {UserId} printed a fresh sheet of {CodeCount} recovery code(s); the previous " +
                  "sheet no longer works.")]
    public static partial void RecoveryCodesReissued(ILogger logger, Guid userId, int codeCount);

    [LoggerMessage(EventId = 2315, Level = LogLevel.Warning,
        Message = "An answer to account {UserId}'s {Factor} challenge was refused because another " +
                  "request had already spent the same credential. Two answers arriving together is " +
                  "ordinary — a double tap or a retry — but a run of them is worth looking at.")]
    public static partial void ChallengeAnswerSuperseded(ILogger logger, Guid userId, string factor);

    [LoggerMessage(EventId = 2320, Level = LogLevel.Warning,
        Message = "Account {UserId} was suspended by an administrator and {EndedSessions} session(s) " +
                  "were ended.")]
    public static partial void AccountSuspended(ILogger logger, Guid userId, int endedSessions);

    [LoggerMessage(EventId = 2313, Level = LogLevel.Error,
        Message = "The stored authenticator secret for account {UserId} could not be read with the current " +
                  "data-protection keys. The holder has to re-enrol; check that the key ring is persisted.")]
    public static partial void TotpSecretUnreadable(ILogger logger, Guid userId);
}
