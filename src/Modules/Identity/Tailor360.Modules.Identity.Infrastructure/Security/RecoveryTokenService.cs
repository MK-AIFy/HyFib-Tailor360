using Tailor360.Modules.Identity.Application.Abstractions;
using Tailor360.Modules.Identity.Domain;
using Tailor360.Modules.Identity.Infrastructure.Sessions;

namespace Tailor360.Modules.Identity.Infrastructure.Security;

/// <summary>
/// Mints the value that travels in a recovery link, and reduces one that comes back to its digest.
/// </summary>
/// <remarks>
/// <para>
/// The value is 256 bits of cryptographic randomness written in base64url, which is the same strength
/// and the same encoding as a session ticket, and for the same reason: for as long as it lives, a
/// recovery token is a credential that sets a password. It is URL-safe without escaping, so it survives
/// the link rewriting that mail clients and scanners do.
/// </para>
/// <para>
/// Generation and digesting come from <see cref="SessionTokenFactory"/> rather than being written again
/// here. The three opaque credentials in this module — the session cookie, the remembered-device cookie
/// and this token — have identical properties, and each database check constraint enforces the same
/// shape; a second implementation would be a second place for the entropy, the encoding or the digest
/// form to drift. What this type adds is the validation of a <em>submitted</em> value, which the session
/// path does not need because a session value is only ever looked up, never parsed.
/// </para>
/// <para>
/// Nothing about the account goes into it. A token that encoded a user identifier would tell whoever
/// intercepted the link whose account it was even if they never managed to use it.
/// </para>
/// </remarks>
public sealed class RecoveryTokenService : IRecoveryTokenService
{
    /// <summary>The entropy in one token, in bytes.</summary>
    public const int TokenBytes = SessionTokenFactory.TokenByteLength;

    /// <summary>The length of the base64url text those bytes produce.</summary>
    public const int EncodedLength = 43;

    /// <inheritdoc />
    public IssuedRecoveryToken Issue()
    {
        var value = SessionTokenFactory.CreateToken();
        return new IssuedRecoveryToken(value, Digest(value));
    }

    /// <inheritdoc />
    public string? DigestOf(string? submitted)
    {
        if (submitted is null || submitted.Length != EncodedLength)
        {
            return null;
        }

        foreach (var character in submitted)
        {
            var usable = char.IsAsciiLetterOrDigit(character) || character is '-' or '_';
            if (!usable)
            {
                return null;
            }
        }

        return Digest(submitted);
    }

    private static string Digest(string value)
    {
        var digest = SessionTokenFactory.Digest(value);

        // The domain refuses anything that is not a well-formed digest; asserting it here means a
        // change to the encoding is caught by this type's own tests rather than by a database
        // constraint at three in the morning.
        return HashedSecret.IsWellFormed(digest)
            ? digest
            : throw new InvalidOperationException("The digest produced was not in the stored form.");
    }
}
