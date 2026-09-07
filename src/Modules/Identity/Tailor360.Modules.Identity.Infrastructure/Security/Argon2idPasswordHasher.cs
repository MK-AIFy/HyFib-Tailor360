using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Konscious.Security.Cryptography;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using Tailor360.Modules.Identity.Domain.Users;

namespace Tailor360.Modules.Identity.Infrastructure.Security;

/// <summary>
/// Hashes and verifies staff passwords with Argon2id, behind ASP.NET Core Identity's
/// <see cref="IPasswordHasher{TUser}"/> so that the algorithm is a registration away from being
/// replaced.
/// </summary>
/// <remarks>
/// Argon2id is the current recommendation because it is memory-hard: an attacker with a warehouse of
/// graphics cards gains far less against it than against PBKDF2, which is what ASP.NET Core Identity
/// ships by default. The framework's own hasher stays available and interchangeable — this type
/// implements the same interface — so replacing it is a one-line change in the module registration and
/// stored hashes keep verifying under whichever hasher recognises their prefix.
/// <para>
/// Hashes are stored in the PHC string format,
/// <c>$argon2id$v=19$m=19456,t=2,p=1$&lt;salt&gt;$&lt;hash&gt;</c>, so every stored value carries the
/// parameters it was produced with. That is what makes raising the work factor a configuration change
/// rather than a migration: verification reads the parameters out of the stored value and answers
/// <see cref="PasswordVerificationResult.SuccessRehashNeeded"/> when they are below the current
/// setting, and the sign-in path — the only moment the password is known — rehashes.
/// </para>
/// <para>
/// A malformed stored hash is a verification failure, never an exception. A record that has been
/// corrupted must not become a way to crash the sign-in endpoint.
/// </para>
/// </remarks>
/// <param name="options">The configured work factors.</param>
public sealed class Argon2idPasswordHasher(IOptions<Argon2idOptions> options) : IPasswordHasher<StaffUser>
{
    /// <summary>The algorithm token stored alongside the hash.</summary>
    public const string AlgorithmName = "argon2id";

    /// <summary>The Argon2 version this hasher writes, 0x13 as decimal, per the reference implementation.</summary>
    private const int Argon2Version = 19;

    private readonly Argon2idOptions _options =
        options?.Value ?? throw new ArgumentNullException(nameof(options));

    /// <inheritdoc />
    public string HashPassword(StaffUser user, string password)
    {
        ArgumentNullException.ThrowIfNull(password);

        var salt = RandomNumberGenerator.GetBytes(_options.SaltLength);
        var hash = Derive(
            password,
            salt,
            _options.MemoryKibibytes,
            _options.Iterations,
            _options.DegreeOfParallelism,
            _options.HashLength);

        return Encode(
            salt, hash, _options.MemoryKibibytes, _options.Iterations, _options.DegreeOfParallelism);
    }

    /// <inheritdoc />
    public PasswordVerificationResult VerifyHashedPassword(
        StaffUser user,
        string hashedPassword,
        string providedPassword)
    {
        if (string.IsNullOrEmpty(hashedPassword) || providedPassword is null)
        {
            return PasswordVerificationResult.Failed;
        }

        if (!TryDecode(hashedPassword, out var stored))
        {
            return PasswordVerificationResult.Failed;
        }

        var candidate = Derive(
            providedPassword,
            stored.Salt,
            stored.MemoryKibibytes,
            stored.Iterations,
            stored.DegreeOfParallelism,
            stored.Hash.Length);

        if (!CryptographicOperations.FixedTimeEquals(candidate, stored.Hash))
        {
            return PasswordVerificationResult.Failed;
        }

        return NeedsRehash(stored)
            ? PasswordVerificationResult.SuccessRehashNeeded
            : PasswordVerificationResult.Success;
    }

    private bool NeedsRehash(StoredHash stored)
        => stored.MemoryKibibytes < _options.MemoryKibibytes
           || stored.Iterations < _options.Iterations
           || stored.DegreeOfParallelism != _options.DegreeOfParallelism
           || stored.Salt.Length < _options.SaltLength
           || stored.Hash.Length < _options.HashLength;

    private static byte[] Derive(
        string password,
        byte[] salt,
        int memoryKibibytes,
        int iterations,
        int degreeOfParallelism,
        int hashLength)
    {
        using var argon2 = new Argon2id(Encoding.UTF8.GetBytes(password))
        {
            Salt = salt,
            MemorySize = memoryKibibytes,
            Iterations = iterations,
            DegreeOfParallelism = degreeOfParallelism,
        };

        return argon2.GetBytes(hashLength);
    }

    private static string Encode(
        byte[] salt,
        byte[] hash,
        int memoryKibibytes,
        int iterations,
        int degreeOfParallelism)
    {
        var parameters = string.Create(
            CultureInfo.InvariantCulture,
            $"$v={Argon2Version}$m={memoryKibibytes},t={iterations},p={degreeOfParallelism}$");

        return string.Concat(
            "$", AlgorithmName, parameters, ToUnpaddedBase64(salt), "$", ToUnpaddedBase64(hash));
    }

    private static bool TryDecode(string encoded, out StoredHash stored)
    {
        stored = default;

        // $argon2id$v=19$m=19456,t=2,p=1$<salt>$<hash> splits into six, the first being empty.
        var segments = encoded.Split('$');
        if (segments.Length != 6
            || segments[0].Length != 0
            || !string.Equals(segments[1], AlgorithmName, StringComparison.Ordinal))
        {
            return false;
        }

        if (!TryReadTagged(segments[2], "v=", out var version) || version != Argon2Version)
        {
            return false;
        }

        var parameters = segments[3].Split(',');
        if (parameters.Length != 3
            || !TryReadTagged(parameters[0], "m=", out var memory)
            || !TryReadTagged(parameters[1], "t=", out var iterations)
            || !TryReadTagged(parameters[2], "p=", out var parallelism))
        {
            return false;
        }

        if (memory < 8 || iterations < 1 || parallelism < 1)
        {
            return false;
        }

        if (!TryFromUnpaddedBase64(segments[4], out var salt)
            || !TryFromUnpaddedBase64(segments[5], out var hash)
            || salt.Length == 0
            || hash.Length == 0)
        {
            return false;
        }

        stored = new StoredHash(salt, hash, memory, iterations, parallelism);
        return true;
    }

    private static bool TryReadTagged(string segment, string tag, out int value)
    {
        value = 0;
        return segment.StartsWith(tag, StringComparison.Ordinal)
               && int.TryParse(
                   segment.AsSpan(tag.Length),
                   NumberStyles.None,
                   CultureInfo.InvariantCulture,
                   out value);
    }

    private static string ToUnpaddedBase64(byte[] value) => Convert.ToBase64String(value).TrimEnd('=');

    private static bool TryFromUnpaddedBase64(string value, out byte[] bytes)
    {
        bytes = [];

        var padding = (4 - (value.Length % 4)) % 4;
        if (padding == 3)
        {
            return false;
        }

        Span<byte> buffer = new byte[((value.Length + padding) / 4) * 3];
        if (!Convert.TryFromBase64String(value + new string('=', padding), buffer, out var written))
        {
            return false;
        }

        bytes = buffer[..written].ToArray();
        return true;
    }

    private readonly record struct StoredHash(
        byte[] Salt,
        byte[] Hash,
        int MemoryKibibytes,
        int Iterations,
        int DegreeOfParallelism);
}
