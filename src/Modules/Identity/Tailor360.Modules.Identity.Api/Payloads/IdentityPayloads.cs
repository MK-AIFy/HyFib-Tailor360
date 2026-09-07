using Tailor360.Modules.Identity.Application.Me;
using Tailor360.Modules.Identity.Application.Sessions;

namespace Tailor360.Modules.Identity.Api.Payloads;

/// <summary>
/// Everything the client shell needs before it paints: who the caller is, how they want the interface
/// to behave, what their account still owes, and when this session ends.
/// </summary>
/// <param name="UserId">The account.</param>
/// <param name="UserName">The sign-in name.</param>
/// <param name="DisplayName">The name shown in the interface.</param>
/// <param name="Email">The holder's own address, returned only to them.</param>
/// <param name="Status">Whether the account is invited, active or suspended.</param>
/// <param name="OrganisationId">The organisation the session is working in.</param>
/// <param name="BranchId">The branch the session is currently working in, when it has one.</param>
/// <param name="Permissions">
/// The permission keys the holder's roles grant. Empty until #24 lands the role model, which is why an
/// endpoint that gates on a permission would deny everybody today.
/// </param>
/// <param name="Security">What the account still owes, and what it can answer a challenge with.</param>
/// <param name="Preferences">How the holder wants the interface to behave.</param>
/// <param name="Session">When this session ends.</param>
public sealed record CurrentUserResponse(
    Guid UserId,
    string UserName,
    string DisplayName,
    string Email,
    string Status,
    Guid OrganisationId,
    Guid? BranchId,
    IReadOnlyList<string> Permissions,
    AccountSecurityPayload Security,
    PreferencesPayload Preferences,
    SessionExpiryPayload Session);

/// <summary>What the account still owes and what it can prove.</summary>
/// <param name="MfaEnrolment">How far the account has got with its second factor.</param>
/// <param name="MustChangePassword">True while the holder must set a new password before working.</param>
/// <param name="MfaSatisfied">True when this session has satisfied a second factor.</param>
/// <param name="LastStrongAuthenticationAt">
/// When the holder last proved a strong factor. The client uses it to decide whether a step-up dialog
/// is about to be demanded, rather than discovering it from a refusal mid-action.
/// </param>
/// <param name="Factors">Which challenges the interface may offer.</param>
/// <param name="UnusedRecoveryCodes">How many unspent recovery codes remain.</param>
public sealed record AccountSecurityPayload(
    string MfaEnrolment,
    bool MustChangePassword,
    bool MfaSatisfied,
    DateTimeOffset? LastStrongAuthenticationAt,
    FactorAvailabilityPayload Factors,
    int UnusedRecoveryCodes);

/// <summary>The interface preferences the shell reads before it paints.</summary>
/// <param name="Locale">The BCP 47 language tag.</param>
/// <param name="TimeZoneId">The IANA timezone dates are shown in.</param>
/// <param name="Theme">Light, dark, high contrast or whatever the device asks for.</param>
/// <param name="Density">How tightly the interface packs information.</param>
/// <param name="ReducedMotion">True when animation is suppressed beyond what the device reports.</param>
/// <param name="LandingRoute">Where the holder lands after signing in, when they have chosen.</param>
public sealed record PreferencesPayload(
    string Locale,
    string TimeZoneId,
    string Theme,
    string Density,
    bool ReducedMotion,
    string? LandingRoute)
{
    /// <summary>Builds the payload from the account's preferences.</summary>
    public static PreferencesPayload From(CurrentUserPreferences preferences)
    {
        ArgumentNullException.ThrowIfNull(preferences);

        return new PreferencesPayload(
            preferences.Locale,
            preferences.TimeZoneId,
            preferences.Theme.ToString(),
            preferences.Density.ToString(),
            preferences.ReducedMotion,
            preferences.LandingRoute);
    }
}

/// <summary>One row of the holder's own session and device inventory.</summary>
/// <remarks>
/// It carries nothing that could be used to impersonate the session — no token, no digest. The address
/// is here because the holder is the one person entitled to see where their own account is signed in
/// from, and because "somewhere I do not recognise" is the whole reason the screen exists.
/// </remarks>
/// <param name="SessionId">Identity of the session, which a revoke request names.</param>
/// <param name="DeviceLabel">What the holder recognises the device by.</param>
/// <param name="IpAddress">The address it was last seen from.</param>
/// <param name="CreatedAt">When the session started.</param>
/// <param name="LastSeenAt">When a request last used it.</param>
/// <param name="IdleExpiresAt">When it ends if nothing further uses it.</param>
/// <param name="AbsoluteExpiresAt">When it ends however active it is.</param>
/// <param name="MfaSatisfied">True when a second factor was satisfied on it.</param>
/// <param name="IsCurrent">True for the session making the request.</param>
public sealed record SessionPayload(
    Guid SessionId,
    string DeviceLabel,
    string? IpAddress,
    DateTimeOffset CreatedAt,
    DateTimeOffset LastSeenAt,
    DateTimeOffset IdleExpiresAt,
    DateTimeOffset AbsoluteExpiresAt,
    bool MfaSatisfied,
    bool IsCurrent)
{
    /// <summary>Builds the payload from what the session service returned.</summary>
    public static SessionPayload From(SessionSummary summary)
    {
        ArgumentNullException.ThrowIfNull(summary);

        return new SessionPayload(
            summary.SessionId,
            summary.DeviceLabel,
            summary.IpAddress,
            summary.CreatedAt,
            summary.LastSeenAt,
            summary.IdleExpiresAt,
            summary.AbsoluteExpiresAt,
            summary.MfaSatisfied,
            summary.IsCurrent);
    }
}

/// <summary>What a client posts to start recovering an account.</summary>
/// <param name="Email">The address the person believes their account uses. Never logged.</param>
public sealed record RecoveryRequestPayload(string? Email);

/// <summary>What a client posts to complete a password reset.</summary>
/// <param name="Token">The value from the recovery link. Never logged.</param>
/// <param name="NewPassword">The proposed password. Never logged, never stored.</param>
public sealed record RecoveryConfirmationPayload(string? Token, string? NewPassword);

/// <summary>
/// The answer to a recovery request. It says the same thing whether or not the address belongs to an
/// account, and carries no field that could differ between the two.
/// </summary>
/// <param name="Message">Fixed wording about checking the inbox.</param>
public sealed record RecoveryAcceptedPayload(string Message);

/// <summary>What completing a reset changed.</summary>
/// <param name="MultiFactorStillRequired">
/// True when the account still holds a confirmed second factor — which after a reset it always does,
/// because a reset changes the password and nothing else. The sign-in that follows still asks.
/// </param>
/// <param name="SessionsRevoked">How many live sessions the reset ended.</param>
/// <param name="MustChangePassword">Whether the holder must change it again at next sign-in.</param>
public sealed record RecoveryCompletedPayload(
    bool MultiFactorStillRequired,
    int SessionsRevoked,
    bool MustChangePassword);

/// <summary>What starting an authenticator enrolment returns.</summary>
/// <remarks>
/// This carries a live credential in two forms. It is returned once, over the response body of an
/// authenticated request, and appears in no log: <c>LogRedaction.SensitivePropertyNames</c> names both
/// fields.
/// </remarks>
/// <param name="OtpAuthUri">
/// The <c>otpauth://</c> link. The screen renders it as the QR code <em>and</em> offers it as
/// <b>Open in authenticator app</b>, which is the only path that works when the authenticator is on the
/// same phone as the browser — a phone cannot photograph its own screen.
/// </param>
/// <param name="ManualEntryKey">The same secret in groups of four, with a <b>Copy</b> control.</param>
/// <param name="Issuer">The name the authenticator lists the account under.</param>
/// <param name="AccountName">The account name shown beneath it.</param>
/// <param name="Digits">Code length, so the screen can size and validate the input.</param>
/// <param name="PeriodSeconds">Step length, so the screen can show how long a code has left.</param>
public sealed record MfaEnrolmentStartedPayload(
    string OtpAuthUri,
    string ManualEntryKey,
    string Issuer,
    string AccountName,
    int Digits,
    int PeriodSeconds);

/// <summary>What a client posts to confirm an authenticator enrolment.</summary>
/// <param name="Code">A code read off the holder's own authenticator. Never logged.</param>
public sealed record MfaEnrolmentConfirmationPayload(string? Code);

/// <summary>The one and only moment the recovery codes exist in a readable form.</summary>
/// <param name="RecoveryCodes">
/// The printed codes, in issue order. Shown once, never stored in readable form, never logged, never
/// emailed. Printing a new sheet destroys the previous one.
/// </param>
public sealed record RecoveryCodesPayload(IReadOnlyList<string> RecoveryCodes);
