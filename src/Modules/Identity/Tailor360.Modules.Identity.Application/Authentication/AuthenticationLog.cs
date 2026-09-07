using Microsoft.Extensions.Logging;

namespace Tailor360.Modules.Identity.Application.Authentication;

/// <summary>
/// Source-generated log messages for the sign-in path.
/// </summary>
/// <remarks>
/// None of these carries a sign-in name, an address, a password, a code or a token. An account appears
/// as its identifier, and a failed attempt against an account that does not exist appears as no
/// identifier at all — writing down what was typed would put an unknown person's email address, or
/// somebody's password typed into the wrong box, into the log for anyone with log access to read.
/// </remarks>
internal static partial class AuthenticationLog
{
    [LoggerMessage(EventId = 2320, Level = LogLevel.Information,
        Message = "Account {UserId} signed in; next step {Step}.")]
    public static partial void SignInSucceeded(ILogger logger, Guid userId, SignInStep step);

    [LoggerMessage(EventId = 2321, Level = LogLevel.Information,
        Message = "A sign-in attempt against account {UserId} failed; {FailureCount} consecutive " +
                  "failure(s) recorded.")]
    public static partial void SignInFailed(ILogger logger, Guid userId, int failureCount);

    [LoggerMessage(EventId = 2322, Level = LogLevel.Information,
        Message = "A sign-in attempt named an account that does not exist, or one that cannot sign in. " +
                  "The caller was answered exactly as a wrong password would be.")]
    public static partial void SignInRejectedWithoutAccount(ILogger logger);

    [LoggerMessage(EventId = 2323, Level = LogLevel.Warning,
        Message = "Account {UserId} is locked out until the lockout lapses after repeated failures.")]
    public static partial void AccountLockedOut(ILogger logger, Guid userId);

    [LoggerMessage(EventId = 2324, Level = LogLevel.Information,
        Message = "Account {UserId} satisfied a second factor and its session was rotated.")]
    public static partial void MultiFactorSatisfied(ILogger logger, Guid userId);

    [LoggerMessage(EventId = 2325, Level = LogLevel.Information,
        Message = "A second-factor answer for account {UserId} was refused.")]
    public static partial void MultiFactorRefused(ILogger logger, Guid userId);

    [LoggerMessage(EventId = 2326, Level = LogLevel.Information,
        Message = "Account {UserId} signed in from a remembered device, which skipped the challenge " +
                  "without satisfying it.")]
    public static partial void TrustedDeviceUsed(ILogger logger, Guid userId);

    [LoggerMessage(EventId = 2327, Level = LogLevel.Information,
        Message = "Account {UserId} remembered a device until {ExpiresAt:u}.")]
    public static partial void TrustedDeviceRemembered(ILogger logger, Guid userId, DateTimeOffset expiresAt);

    [LoggerMessage(EventId = 2328, Level = LogLevel.Information,
        Message = "The stored password for account {UserId} was made with parameters weaker than the " +
                  "ones now configured and is marked for rehashing at its next change.")]
    public static partial void PasswordRehashRequired(ILogger logger, Guid userId);

    [LoggerMessage(EventId = 2329, Level = LogLevel.Information,
        Message = "Account {UserId} signed out of {SessionCount} session(s).")]
    public static partial void SignedOut(ILogger logger, Guid userId, int sessionCount);

    [LoggerMessage(EventId = 2330, Level = LogLevel.Information,
        Message = "Account {UserId} registered a passkey.")]
    public static partial void PasskeyRegistered(ILogger logger, Guid userId);

    [LoggerMessage(EventId = 2331, Level = LogLevel.Information,
        Message = "A passkey assertion was refused.")]
    public static partial void PasskeyAssertionRefused(ILogger logger);
}
