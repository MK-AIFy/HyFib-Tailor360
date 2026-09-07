using Tailor360.Modules.Identity.Domain.Users;

namespace Tailor360.Modules.Identity.Application.Abstractions;

/// <summary>
/// One account and one password hash that exist solely to be verified against when no real account
/// matches, so that an unknown sign-in name costs the server exactly what a known one costs.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why this is a service and not a field on the handler.</b> Building the decoy means deriving an
/// Argon2id hash, which is tens of milliseconds and a megabyte of memory by design. A decoy built inside
/// the sign-in handler — which is resolved per request — is built once per request, so the unknown-account
/// path pays for two derivations and the known-account path for one. That is a two-to-one difference,
/// tens of milliseconds wide, and it is a better account oracle than anything the wording of the
/// response could have leaked: it says who holds an account and, because the same path answers a
/// locked-out account, which accounts are currently locked out.
/// </para>
/// <para>
/// Registered as a singleton and derived once for the life of the process, so both paths cost one
/// derivation. The password behind the hash is random bytes discarded immediately, so nothing a caller
/// can submit will ever match it.
/// </para>
/// </remarks>
public interface IDecoyCredential
{
    /// <summary>The account the verification runs against. Never persisted and never a real account.</summary>
    StaffUser User { get; }

    /// <summary>The encoded hash to verify against.</summary>
    string EncodedHash { get; }
}
