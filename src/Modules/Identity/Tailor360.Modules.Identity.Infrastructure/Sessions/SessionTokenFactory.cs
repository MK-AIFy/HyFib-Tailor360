using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;

namespace Tailor360.Modules.Identity.Infrastructure.Sessions;

/// <summary>
/// Produces the opaque value that goes in the session cookie, and the digest that is stored instead
/// of it.
/// </summary>
/// <remarks>
/// <para>
/// The value is 256 bits from the operating system's cryptographic generator, encoded base64url so
/// that it is safe in a cookie without escaping. It carries no structure — not the user, not the
/// session identifier, not a timestamp — because anything it carried would be something the server had
/// to either trust or verify, and a value that names nothing needs neither.
/// </para>
/// <para>
/// Only the digest is stored. A plain SHA-256 is the right primitive here and a password hash would be
/// the wrong one: the input is 256 bits of server-generated entropy, so there is no guessing to slow
/// down, only a stolen table to make useless. The digest is lower-case hexadecimal to match
/// <c>HashedSecret</c>, which is the shape the database check constraint enforces.
/// </para>
/// </remarks>
public static class SessionTokenFactory
{
    /// <summary>The entropy in a session value, in bytes.</summary>
    public const int TokenByteLength = 32;

    /// <summary>Creates a new session value. The caller places it in the cookie and then forgets it.</summary>
    public static string CreateToken()
        => Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(TokenByteLength));

    /// <summary>Returns the stored form of a session or device value.</summary>
    public static string Digest(string token)
    {
        ArgumentException.ThrowIfNullOrEmpty(token);

        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
    }
}
