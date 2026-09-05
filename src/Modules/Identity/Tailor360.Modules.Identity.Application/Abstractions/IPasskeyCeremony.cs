using Tailor360.Platform.Abstractions.Results;

namespace Tailor360.Modules.Identity.Application.Abstractions;

/// <summary>
/// Runs the two WebAuthn ceremonies — registering a passkey and asserting one — without the
/// application layer naming a WebAuthn library.
/// </summary>
/// <remarks>
/// <para>
/// The port carries JSON in both directions rather than typed credential objects. That is deliberate:
/// the shapes crossing this boundary are defined by the W3C specification and produced verbatim by the
/// browser, so restating them as records here would create a second definition to keep in step with a
/// specification neither side owns, and would put a library's types into the layer that must not know
/// which library is in use.
/// </para>
/// <para>
/// <b>Every ceremony is bound to the request that started it.</b> The challenge is held server-side
/// under a ceremony identifier the caller must present again, and — when the caller had a session — to
/// that session. A challenge issued to one browser cannot be completed by another, which is what stops
/// an attacker from having a victim's authenticator sign a challenge issued to the attacker's session.
/// </para>
/// </remarks>
public interface IPasskeyCeremony
{
    /// <summary>
    /// True when a relying party is configured and ceremonies can run. False makes every passkey
    /// endpoint answer <c>identity.passkeys-unavailable</c> rather than fail in a way a client cannot
    /// interpret.
    /// </summary>
    bool IsAvailable { get; }

    /// <summary>Starts a registration and returns the options the browser needs.</summary>
    /// <param name="user">Who the credential will belong to.</param>
    /// <param name="excludeCredentialIds">
    /// Credentials the account already holds, so the authenticator declines to enrol itself twice
    /// rather than silently creating a duplicate the holder cannot tell apart.
    /// </param>
    /// <param name="boundSessionId">The session the ceremony belongs to, when there is one.</param>
    Result<PasskeyChallenge> BeginRegistration(
        PasskeyUserDescriptor user,
        IReadOnlyList<byte[]> excludeCredentialIds,
        Guid? boundSessionId);

    /// <summary>Verifies a registration response and returns what should be stored.</summary>
    Task<Result<PasskeyRegistration>> CompleteRegistrationAsync(
        string? ceremonyId,
        string? credentialJson,
        Guid? boundSessionId,
        CancellationToken cancellationToken = default);

    /// <summary>Starts an assertion and returns the options the browser needs.</summary>
    /// <param name="allowCredentialIds">
    /// The credentials the browser may use, or an empty list for a sign-in where no account has been
    /// named and the authenticator chooses a discoverable credential itself.
    /// </param>
    /// <param name="boundSessionId">The session the ceremony belongs to, when there is one.</param>
    Result<PasskeyChallenge> BeginAssertion(IReadOnlyList<byte[]> allowCredentialIds, Guid? boundSessionId);

    /// <summary>
    /// Reads the credential identifier out of an assertion response without verifying anything. It is
    /// how an account is found when none was named; nothing is trusted on the strength of it, because
    /// the signature is checked afterwards against the key that identifier resolves to.
    /// </summary>
    Result<byte[]> ReadAssertedCredentialId(string? credentialJson);

    /// <summary>Verifies an assertion against a stored credential.</summary>
    Task<Result<PasskeyAssertion>> CompleteAssertionAsync(
        string? ceremonyId,
        string? credentialJson,
        StoredPasskey stored,
        Guid? boundSessionId,
        CancellationToken cancellationToken = default);
}

/// <summary>How an account is described to an authenticator.</summary>
/// <param name="UserId">The account identifier, which becomes the WebAuthn user handle.</param>
/// <param name="UserName">The sign-in name the authenticator lists the credential under.</param>
/// <param name="DisplayName">The name shown beside it.</param>
public sealed record PasskeyUserDescriptor(Guid UserId, string UserName, string DisplayName);

/// <summary>A started ceremony: what the browser needs, and the handle that ties the answer to it.</summary>
/// <param name="CeremonyId">
/// The opaque handle the caller returns with the response. It is unguessable and single-use.
/// </param>
/// <param name="OptionsJson">The W3C options object, serialised, to be passed to the browser verbatim.</param>
/// <param name="ExpiresAt">When the challenge stops being accepted.</param>
public sealed record PasskeyChallenge(string CeremonyId, string OptionsJson, DateTimeOffset ExpiresAt);

/// <summary>What a verified registration produced, ready for the domain to store.</summary>
/// <param name="CredentialId">The authenticator's credential identifier.</param>
/// <param name="PublicKey">The COSE-encoded public key. Not a secret.</param>
/// <param name="AuthenticatorGuid">The authenticator model identifier.</param>
/// <param name="SignatureCounter">The counter value at registration.</param>
/// <param name="Transports">The transports the authenticator advertised, comma separated.</param>
/// <param name="IsBackupEligible">True when the credential may be synchronised to other devices.</param>
/// <param name="IsBackedUp">True when it currently is.</param>
public sealed record PasskeyRegistration(
    byte[] CredentialId,
    byte[] PublicKey,
    Guid AuthenticatorGuid,
    long SignatureCounter,
    string? Transports,
    bool IsBackupEligible,
    bool IsBackedUp);

/// <summary>The stored half of a credential, supplied so an assertion can be verified against it.</summary>
/// <param name="CredentialId">The credential identifier.</param>
/// <param name="PublicKey">The stored public key.</param>
/// <param name="SignatureCounter">The last counter value accepted.</param>
/// <param name="UserHandle">The account identifier the credential was registered to.</param>
public sealed record StoredPasskey(
    byte[] CredentialId,
    byte[] PublicKey,
    long SignatureCounter,
    Guid UserHandle);

/// <summary>What a verified assertion reported.</summary>
/// <param name="CredentialId">The credential that signed.</param>
/// <param name="SignatureCounter">The counter value it reported, which must have advanced.</param>
/// <param name="IsBackedUp">Whether the credential is currently synchronised to other devices.</param>
public sealed record PasskeyAssertion(byte[] CredentialId, long SignatureCounter, bool IsBackedUp);
