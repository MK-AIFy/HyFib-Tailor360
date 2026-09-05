namespace Tailor360.Modules.Identity.Application.Recovery;

/// <summary>A request to start account recovery.</summary>
/// <param name="Email">The address the person believes their account uses. Never logged.</param>
public sealed record RequestPasswordRecovery(string? Email);

/// <summary>
/// The answer to a recovery request. It carries nothing, and that is the design: a response that
/// differed by so much as a field name between a known and an unknown address would be an
/// account-enumeration oracle, and the same is true of the message the interface shows, which is fixed
/// wording about checking the inbox.
/// </summary>
public sealed record RecoveryRequestAccepted;

/// <summary>A request to complete a password reset.</summary>
/// <param name="Token">The value from the recovery link. Never logged.</param>
/// <param name="NewPassword">The proposed password. Never logged, never stored.</param>
public sealed record ConfirmPasswordRecovery(string? Token, string? NewPassword);

/// <summary>What completing a reset changed.</summary>
/// <param name="UserId">The account.</param>
/// <param name="MultiFactorStillRequired">
/// True when the account still holds a confirmed second factor, which after a password reset it always
/// does — a reset changes the password and nothing else. The sign-in that follows still has to answer a
/// multi-factor challenge, and this field is here so the interface says so rather than implying the
/// reset was the whole journey.
/// </param>
/// <param name="SessionsRevoked">How many live sessions the reset ended.</param>
/// <param name="MustChangePassword">Whether the holder must change the password again at next sign-in.</param>
public sealed record RecoveryCompleted(
    Guid UserId,
    bool MultiFactorStillRequired,
    int SessionsRevoked,
    bool MustChangePassword);
