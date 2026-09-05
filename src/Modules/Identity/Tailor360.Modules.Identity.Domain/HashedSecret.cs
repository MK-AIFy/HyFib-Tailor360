using System.Security.Cryptography;
using System.Text;

namespace Tailor360.Modules.Identity.Domain;

/// <summary>
/// The one shape every stored secret comparison value takes in this module: the lower-case hexadecimal
/// SHA-256 digest of the secret, sixty-four characters long.
/// </summary>
/// <remarks>
/// Session tickets, trusted-device tokens and recovery codes are all high-entropy values the server
/// generated, so a plain digest is the right primitive — there is nothing to slow down, only something
/// to make unreadable if the table is stolen. Passwords are the opposite case and go through the
/// password hasher instead, never through here.
/// <para>
/// The shape is validated on the way in rather than trusted, because "we always hash before storing"
/// is a convention that survives exactly until the first call site that forgets. A sixty-four character
/// lower-case hex string is not something a raw recovery code or cookie value looks like by accident.
/// </para>
/// </remarks>
public static class HashedSecret
{
    /// <summary>The length of a hexadecimal SHA-256 digest.</summary>
    public const int Length = 64;

    /// <summary>True when the value is a lower-case hexadecimal SHA-256 digest.</summary>
    public static bool IsWellFormed(string? value)
    {
        if (value is null || value.Length != Length)
        {
            return false;
        }

        foreach (var character in value)
        {
            var isDigit = character is >= '0' and <= '9';
            var isLowerHex = character is >= 'a' and <= 'f';

            if (!isDigit && !isLowerHex)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Compares two digests in time that does not depend on how many leading characters agree. The
    /// candidate is supplied by whoever is trying to sign in, so an early-exit comparison would leak
    /// how much of a guess was right.
    /// </summary>
    public static bool Matches(string? stored, string? candidate)
    {
        if (!IsWellFormed(stored) || !IsWellFormed(candidate))
        {
            return false;
        }

        return CryptographicOperations.FixedTimeEquals(
            Encoding.ASCII.GetBytes(stored!),
            Encoding.ASCII.GetBytes(candidate!));
    }
}
