using Tailor360.Modules.Identity.Application.Sessions;

namespace Tailor360.Modules.Identity.Application.Authentication;

/// <summary>What a sign-in attempt supplies.</summary>
/// <param name="Identifier">The sign-in name or email address as typed. Never logged.</param>
/// <param name="Password">The password as typed. Never logged, never stored, never echoed.</param>
/// <param name="DeviceLabel">What the holder will recognise this session by in their inventory.</param>
/// <param name="IpAddress">The client address, for the holder's inventory and for the throttle.</param>
/// <param name="UserAgent">The user agent, for the holder's inventory.</param>
/// <param name="TrustedDeviceToken">
/// The remembered-device cookie value, when the browser presented one. It skips the second-factor
/// challenge; it never counts as having passed one.
/// </param>
/// <param name="PresentedSessionId">
/// The session the caller was already holding, which is revoked as the new one is created. This is
/// what closes session fixation: a planted ticket is not the ticket the person leaves with.
/// </param>
public sealed record SignInCommand(
    string? Identifier,
    string? Password,
    string DeviceLabel,
    string? IpAddress = null,
    string? UserAgent = null,
    string? TrustedDeviceToken = null,
    Guid? PresentedSessionId = null);

/// <summary>What the caller must do next.</summary>
public enum SignInStep
{
    /// <summary>Nothing. The session is fully established.</summary>
    Complete = 0,

    /// <summary>A second factor must be answered before the session can do anything that needs one.</summary>
    MultiFactorRequired = 1,

    /// <summary>
    /// The account holds permissions that require a second factor and has none enrolled, so the only
    /// thing this session may usefully do is enrol one.
    /// </summary>
    MultiFactorEnrolmentRequired = 2,
}

/// <summary>Which second factors this account can answer a challenge with.</summary>
/// <param name="Authenticator">True when a confirmed authenticator is enrolled.</param>
/// <param name="RecoveryCode">True when at least one unspent recovery code remains.</param>
/// <param name="Passkey">True when at least one passkey is registered.</param>
public sealed record AvailableFactors(bool Authenticator, bool RecoveryCode, bool Passkey);

/// <summary>A sign-in that got as far as issuing a session.</summary>
/// <remarks>
/// A successful first factor always produces a session, even when a second factor is still owed. That
/// session is the thing the challenge is answered under, and it carries no satisfied second factor, so
/// it reaches nothing that requires one. Handing out a separate short-lived "half token" instead would
/// mean a second credential format to protect, revoke and expire, for no gain.
/// </remarks>
/// <param name="Step">What the caller must do next.</param>
/// <param name="Session">The session issued, whose token the caller must place in the cookie.</param>
/// <param name="UserId">The account.</param>
/// <param name="DisplayName">The name to greet the holder by.</param>
/// <param name="MustChangePassword">True when the holder must set a new password before working.</param>
/// <param name="Factors">Which challenges the interface may offer.</param>
public sealed record SignInSucceeded(
    SignInStep Step,
    IssuedSession Session,
    Guid UserId,
    string DisplayName,
    bool MustChangePassword,
    AvailableFactors Factors);

/// <summary>What answering a multi-factor challenge supplies.</summary>
/// <param name="SessionId">The session that answered the first factor.</param>
/// <param name="UserId">The account the session belongs to.</param>
/// <param name="Factor">Which factor is being answered.</param>
/// <param name="Code">The code as typed. Never logged.</param>
/// <param name="RememberDevice">
/// True when the holder asked for this device to be remembered. Honoured only when the deployment
/// allows it; a remembered device skips later challenges but never satisfies one.
/// </param>
/// <param name="DeviceLabel">What the remembered device is called in the inventory.</param>
/// <param name="IpAddress">The client address, for the throttle. Never logged.</param>
public sealed record MultiFactorAnswer(
    Guid SessionId,
    Guid UserId,
    Mfa.MfaFactor Factor,
    string? Code,
    bool RememberDevice = false,
    string? DeviceLabel = null,
    string? IpAddress = null);

/// <summary>A challenge that was answered correctly.</summary>
/// <param name="Session">The replacement session, whose token the caller must place in the cookie.</param>
/// <param name="RemainingRecoveryCodes">How many unspent codes the holder has left.</param>
/// <param name="ShouldReissueRecoveryCodes">True when the holder should be pressed to print a new sheet.</param>
/// <param name="TrustedDeviceToken">
/// The remembered-device cookie value, when one was issued. Sensitive: it goes in a cookie and nowhere
/// else.
/// </param>
/// <param name="TrustedDeviceExpiresAt">When the memory of the device lapses.</param>
public sealed record MultiFactorSatisfied(
    IssuedSession Session,
    int RemainingRecoveryCodes,
    bool ShouldReissueRecoveryCodes,
    string? TrustedDeviceToken = null,
    DateTimeOffset? TrustedDeviceExpiresAt = null);
