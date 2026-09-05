using Tailor360.Platform.Abstractions.Results;

namespace Tailor360.Modules.Identity.Application.Abstractions;

/// <summary>
/// Wraps and unwraps the one secret this module has to be able to read back: the authenticator's
/// shared key. Everything else it stores — passwords, recovery codes, session and device tokens — is
/// hashed and never recovered, which is why this port exists for exactly one purpose.
/// </summary>
/// <remarks>
/// The shared key cannot be hashed, because generating the expected code requires the key itself. So it
/// is encrypted at rest with a key the application holds rather than the database, which means a copy
/// of the <c>identity</c> schema alone does not let anyone mint codes.
/// <para>
/// <see cref="Unprotect"/> returns a failure rather than throwing, because the realistic reason for it
/// to fail is operational — a key ring that was not persisted, or was replaced — and the account holder
/// should be told to re-enrol rather than shown a server error.
/// </para>
/// </remarks>
public interface ISecretProtector
{
    /// <summary>Wraps a secret for storage. Never log either the input or the output.</summary>
    string Protect(string plaintext);

    /// <summary>Unwraps a stored secret, failing when it cannot be read with the current keys.</summary>
    Result<string> Unprotect(string protectedValue);
}
