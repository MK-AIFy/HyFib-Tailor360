using System.Text.Json;
using Tailor360.Modules.Identity.Application.Authentication;

namespace Tailor360.Modules.Identity.Api.Payloads;

/// <summary>What a client posts to sign in.</summary>
/// <param name="Identifier">The sign-in name or email address. Never logged.</param>
/// <param name="Password">The password. Never logged, never echoed, never stored.</param>
/// <param name="CaptchaResponse">
/// The human-verification response, when the previous answer asked for one and a provider is
/// configured. Ignored otherwise.
/// </param>
public sealed record SignInRequest(string? Identifier, string? Password, string? CaptchaResponse = null);

/// <summary>What a client posts to answer a second-factor challenge.</summary>
/// <param name="Factor">
/// <c>totp</c> for a code from an authenticator, <c>recoveryCode</c> for one of the printed codes.
/// </param>
/// <param name="Code">The code as typed. Never logged.</param>
/// <param name="RememberDevice">
/// True when the holder asked for this device to be remembered so that later sign-ins need only the
/// password. Honoured only where the deployment allows it.
/// </param>
public sealed record MultiFactorChallengeRequest(
    string? Factor,
    string? Code,
    bool RememberDevice = false);

/// <summary>
/// What a sign-in answered. It carries no token: the session travels in a cookie script cannot read,
/// and the only thing script is ever given is the anti-forgery request token.
/// </summary>
/// <param name="Step">
/// <c>complete</c>, <c>multiFactorRequired</c> or <c>multiFactorEnrolmentRequired</c> — what the client
/// must do next.
/// </param>
/// <param name="UserId">The account.</param>
/// <param name="DisplayName">The name to greet the holder by.</param>
/// <param name="MustChangePassword">True when the holder must set a new password before working.</param>
/// <param name="Factors">Which challenges the interface may offer.</param>
/// <param name="Session">When this session ends, so the client can warn before it does.</param>
public sealed record SignInResponse(
    string Step,
    Guid UserId,
    string DisplayName,
    bool MustChangePassword,
    FactorAvailabilityPayload Factors,
    SessionExpiryPayload Session)
{
    /// <summary>Builds the payload from what the handler returned.</summary>
    public static SignInResponse From(SignInSucceeded succeeded, SessionExpiryPayload session)
    {
        ArgumentNullException.ThrowIfNull(succeeded);

        return new SignInResponse(
            Describe(succeeded.Step),
            succeeded.UserId,
            succeeded.DisplayName,
            succeeded.MustChangePassword,
            new FactorAvailabilityPayload(
                succeeded.Factors.Authenticator,
                succeeded.Factors.RecoveryCode,
                succeeded.Factors.Passkey),
            session);
    }

    private static string Describe(SignInStep step) => step switch
    {
        SignInStep.MultiFactorRequired => "multiFactorRequired",
        SignInStep.MultiFactorEnrolmentRequired => "multiFactorEnrolmentRequired",
        _ => "complete",
    };
}

/// <summary>Which second factors the account can answer a challenge with.</summary>
/// <param name="Authenticator">True when a confirmed authenticator is enrolled.</param>
/// <param name="RecoveryCode">True when at least one unspent recovery code remains.</param>
/// <param name="Passkey">True when at least one passkey is registered.</param>
public sealed record FactorAvailabilityPayload(bool Authenticator, bool RecoveryCode, bool Passkey);

/// <summary>
/// When the current session ends, so the client can show the two-minute warning dialog in time for
/// someone to answer it (WCAG 2.2.1).
/// </summary>
/// <param name="IdleExpiresAt">When it ends if nothing further uses it.</param>
/// <param name="AbsoluteExpiresAt">When it ends however active it is.</param>
/// <param name="WarningLeadSeconds">How long before the inactivity deadline to warn.</param>
/// <param name="MfaSatisfied">True when a second factor has been satisfied on this session.</param>
public sealed record SessionExpiryPayload(
    DateTimeOffset IdleExpiresAt,
    DateTimeOffset AbsoluteExpiresAt,
    int WarningLeadSeconds,
    bool MfaSatisfied);

/// <summary>What answering a challenge returned.</summary>
/// <param name="RemainingRecoveryCodes">How many unspent codes the holder has left.</param>
/// <param name="ShouldReissueRecoveryCodes">True when the holder should print a new sheet now.</param>
/// <param name="DeviceRemembered">True when the device was remembered.</param>
/// <param name="Session">When this session ends.</param>
public sealed record MultiFactorChallengeResponse(
    int RemainingRecoveryCodes,
    bool ShouldReissueRecoveryCodes,
    bool DeviceRemembered,
    SessionExpiryPayload Session);

/// <summary>What signing out of every device reported.</summary>
/// <param name="SessionsEnded">How many sessions were ended.</param>
public sealed record SignOutEverywhereResponse(int SessionsEnded);

/// <summary>A started WebAuthn ceremony.</summary>
/// <param name="CeremonyId">The handle to return with the authenticator's response.</param>
/// <param name="Options">The W3C options object, to be passed to the browser verbatim.</param>
/// <param name="ExpiresAt">When the challenge stops being accepted.</param>
public sealed record PasskeyChallengeResponse(string CeremonyId, JsonElement Options, DateTimeOffset ExpiresAt);

/// <summary>What a client posts to finish registering a passkey.</summary>
/// <param name="CeremonyId">The handle the challenge was issued under.</param>
/// <param name="Credential">The authenticator's response, verbatim.</param>
/// <param name="Label">What the holder wants to call this passkey.</param>
public sealed record PasskeyRegistrationRequest(string? CeremonyId, JsonElement Credential, string? Label);

/// <summary>What a client posts to sign in with a passkey.</summary>
/// <param name="CeremonyId">The handle the challenge was issued under.</param>
/// <param name="Credential">The authenticator's response, verbatim.</param>
public sealed record PasskeyAssertionRequest(string? CeremonyId, JsonElement Credential);

/// <summary>One registered passkey, as its holder sees it.</summary>
/// <param name="PasskeyId">Identity of the registration, which a removal names.</param>
/// <param name="Label">The name the holder gave it.</param>
/// <param name="CreatedAt">When it was registered.</param>
/// <param name="LastUsedAt">When it was last used.</param>
/// <param name="IsBackedUp">True when it is synchronised to the holder's other devices.</param>
public sealed record PasskeyPayload(
    Guid PasskeyId,
    string Label,
    DateTimeOffset CreatedAt,
    DateTimeOffset? LastUsedAt,
    bool IsBackedUp);
