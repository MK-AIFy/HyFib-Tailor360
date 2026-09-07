using Tailor360.Modules.Identity.Domain.Users;

namespace Tailor360.Modules.Identity.Application.Abstractions;

/// <summary>
/// Hashes and verifies passwords, and says which algorithm did it.
/// </summary>
/// <remarks>
/// The port exists so that the application layer never names a hashing library. That keeps two things
/// true at once: the algorithm can be replaced without touching a handler, and the algorithm token
/// stored beside each hash comes from the thing that produced it rather than from a string literal in
/// whichever handler happened to set the password.
/// <para>
/// Implementations must not log the password, the hash or anything derived from either.
/// </para>
/// </remarks>
public interface IPasswordHashingService
{
    /// <summary>The token stored beside a hash this service produces, for example <c>argon2id</c>.</summary>
    string AlgorithmName { get; }

    /// <summary>Hashes a password for storage.</summary>
    string Hash(StaffUser user, string password);

    /// <summary>
    /// Checks a password against a stored hash, and reports when the hash was made with weaker
    /// parameters than the ones now configured so the caller can quietly upgrade it.
    /// </summary>
    PasswordVerification Verify(StaffUser user, string encodedHash, string password);
}

/// <summary>The outcome of checking a password against a stored hash.</summary>
public enum PasswordVerification
{
    /// <summary>The password did not match.</summary>
    Failed = 0,

    /// <summary>The password matched.</summary>
    Succeeded = 1,

    /// <summary>
    /// The password matched a hash made with parameters weaker than the ones now configured. The
    /// caller should rehash and store the result, which is how a work-factor increase reaches existing
    /// accounts without anyone having to change their password.
    /// </summary>
    SucceededButNeedsRehash = 2,
}
