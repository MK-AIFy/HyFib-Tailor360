using System.Security.Cryptography;
using System.Text;
using Tailor360.Modules.Identity.Domain.Users;

namespace Tailor360.UnitTests.Identity;

/// <summary>
/// Shared construction for the Identity domain tests. Every value here is synthetic: no test in this
/// repository may carry a real name, address or phone number, in a fixture or anywhere else.
/// </summary>
internal static class IdentityTestData
{
    /// <summary>A fixed instant, so every assertion about time is exact rather than approximate.</summary>
    public static DateTimeOffset Now { get; } = new(2026, 9, 5, 10, 0, 0, TimeSpan.Zero);

    /// <summary>The organisation these tests run in.</summary>
    public static Guid Organisation { get; } = new("0192f3c1-0000-7000-8000-000000000001");

    /// <summary>An identifier derived from a name, so a failure names which one it was.</summary>
    public static Guid Id(string name)
    {
        Span<byte> bytes = stackalloc byte[16];
        SHA256.HashData(Encoding.UTF8.GetBytes(name)).AsSpan(0, 16).CopyTo(bytes);
        return new Guid(bytes);
    }

    /// <summary>The lower-case hexadecimal SHA-256 digest of a value, as the domain stores it.</summary>
    public static string Digest(string value)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

    /// <summary>An invited account.</summary>
    public static StaffUser Invited(string userName = "test.user")
        => StaffUser.Invite(
            Id(userName),
            Organisation,
            userName,
            $"{userName}@example.test",
            "Test User",
            Now).Value;

    /// <summary>An active account with a password set.</summary>
    public static StaffUser Active(string userName = "test.user")
    {
        var user = Invited(userName);
        user.SetPassword(Id($"{userName}-credential"), EncodedHash, "argon2id", Now, null);
        return user;
    }

    /// <summary>An active account with a confirmed authenticator.</summary>
    public static StaffUser Enrolled(string userName = "test.user")
    {
        var user = Active(userName);
        user.BeginTotpEnrolment(Id($"{userName}-totp"), "protected-secret", Now);
        user.ConfirmTotpEnrolment(1_800_000L, Now);
        return user;
    }

    /// <summary>Ten distinct recovery-code digests, the shape the aggregate accepts.</summary>
    public static IReadOnlyCollection<(Guid Id, string CodeHash)> TenCodes(string prefix = "code")
        => [.. Enumerable.Range(0, 10).Select(i => (Id($"{prefix}-{i}"), Digest($"{prefix}-{i}")))];

    /// <summary>A value shaped like an encoded hash, which is all the domain checks.</summary>
    public const string EncodedHash = "$argon2id$v=19$m=19456,t=2,p=1$c2FsdHNhbHRzYWx0c2E$aGFzaGhhc2hoYXNoaGFzaA";
}
