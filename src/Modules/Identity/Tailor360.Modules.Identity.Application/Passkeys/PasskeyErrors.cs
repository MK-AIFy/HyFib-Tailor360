using Tailor360.Platform.Abstractions.Results;

namespace Tailor360.Modules.Identity.Application.Passkeys;

/// <summary>
/// The failures the passkey ceremonies report, which the domain has no opinion about because they are
/// about the protocol rather than about the account.
/// </summary>
/// <remarks>
/// Every message here is written for a person standing at a counter with a security key in their hand,
/// and none of them says which part of the verification failed. A ceremony that reported "the origin
/// did not match" would be telling an attacker exactly which of their attempts was closest.
/// </remarks>
public static class PasskeyErrors
{
    /// <summary>No relying party is configured, so no ceremony can run.</summary>
    public static Error Unavailable { get; } = Error.Unavailable(
        "identity.passkeys-unavailable",
        "Passkeys are not configured on this server. Sign in with your password instead.");

    /// <summary>
    /// The ceremony handle is unknown, spent, expired, or belongs to another session. All four report
    /// this, because distinguishing them would tell the holder of a stolen handle which it was.
    /// </summary>
    public static Error CeremonyNotValid { get; } = Error.Forbidden(
        "identity.passkey-ceremony-not-valid",
        "That passkey request is no longer valid. Start again.");

    /// <summary>The response could not be read as a WebAuthn credential.</summary>
    public static Error ResponseNotReadable { get; } = Error.Validation(
        "identity.passkey-response-not-readable",
        "The authenticator's response could not be read.",
        "credential");

    /// <summary>The response did not verify.</summary>
    public static Error VerificationFailed { get; } = Error.Forbidden(
        "identity.passkey-verification-failed",
        "That passkey could not be verified.");
}
