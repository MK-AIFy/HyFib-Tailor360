using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using Shouldly;
using Tailor360.Modules.Identity.Domain.Users;
using Tailor360.Modules.Identity.Infrastructure.Security;

namespace Tailor360.UnitTests.Identity;

/// <summary>
/// The password hasher. It has no database and no host behind it, so it belongs in the unit tier
/// despite living in the infrastructure project.
/// </summary>
/// <remarks>
/// The work factors are lowered to the smallest the options allow, because these tests assert the
/// encoding and the decisions around it, not the cost of the function. Production values are set in
/// configuration and validated at startup.
/// </remarks>
[Trait("Category", "Unit")]
public sealed class Argon2idPasswordHasherTests
{
    private const string Password = "correct horse battery staple";

    private static readonly StaffUser User = IdentityTestData.Invited();

    private static Argon2idPasswordHasher Hasher(int memoryKibibytes = 8192, int iterations = 1)
        => new(Options.Create(new Argon2idOptions
        {
            MemoryKibibytes = memoryKibibytes,
            Iterations = iterations,
            DegreeOfParallelism = 1,
            SaltLength = 16,
            HashLength = 32,
        }));

    [Fact]
    public void AHashVerifiesTheSamePassword()
    {
        var hasher = Hasher();
        var hash = hasher.HashPassword(User, Password);

        hasher.VerifyHashedPassword(User, hash, Password).ShouldBe(PasswordVerificationResult.Success);
    }

    [Fact]
    public void ADifferentPasswordDoesNotVerify()
    {
        var hasher = Hasher();
        var hash = hasher.HashPassword(User, Password);

        hasher.VerifyHashedPassword(User, hash, "correct horse battery stapl")
            .ShouldBe(PasswordVerificationResult.Failed);
    }

    [Fact]
    public void TheSamePasswordHashesDifferentlyEveryTime()
    {
        // A per-password salt is what stops one precomputed table from breaking every account at once,
        // and stops two people with the same password being visibly the same in the table.
        var hasher = Hasher();

        hasher.HashPassword(User, Password).ShouldNotBe(hasher.HashPassword(User, Password));
    }

    [Fact]
    public void TheStoredValueCarriesItsOwnParameters()
    {
        // This is what makes raising the work factor a configuration change rather than a migration.
        var hash = Hasher(memoryKibibytes: 16384, iterations: 3).HashPassword(User, Password);

        hash.ShouldStartWith("$argon2id$v=19$m=16384,t=3,p=1$");
        hash.Split('$').Length.ShouldBe(6);
    }

    [Fact]
    public void RaisingTheWorkFactorAsksForARehashWithoutRefusingTheSignIn()
    {
        // The password is only known during a successful sign-in, so that is the one moment an upgrade
        // can happen. Refusing the sign-in instead would lock everyone out on a configuration change.
        var hash = Hasher(memoryKibibytes: 8192, iterations: 1).HashPassword(User, Password);

        Hasher(memoryKibibytes: 32768, iterations: 3)
            .VerifyHashedPassword(User, hash, Password)
            .ShouldBe(PasswordVerificationResult.SuccessRehashNeeded);
    }

    [Fact]
    public void LoweringTheWorkFactorDoesNotAskForARehash()
    {
        var hash = Hasher(memoryKibibytes: 32768, iterations: 3).HashPassword(User, Password);

        Hasher(memoryKibibytes: 8192, iterations: 1)
            .VerifyHashedPassword(User, hash, Password)
            .ShouldBe(PasswordVerificationResult.Success);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-a-hash")]
    [InlineData("$argon2i$v=19$m=8192,t=1,p=1$c2FsdHNhbHRzYWx0c2E$aGFzaGhhc2hoYXNoaGFzaA")]
    [InlineData("$argon2id$v=16$m=8192,t=1,p=1$c2FsdHNhbHRzYWx0c2E$aGFzaGhhc2hoYXNoaGFzaA")]
    [InlineData("$argon2id$v=19$m=8192,t=1$c2FsdHNhbHRzYWx0c2E$aGFzaGhhc2hoYXNoaGFzaA")]
    [InlineData("$argon2id$v=19$m=8192,t=1,p=1$!!!!$aGFzaGhhc2hoYXNoaGFzaA")]
    [InlineData("$argon2id$v=19$m=8192,t=1,p=1$$")]
    public void AMalformedStoredValueFailsVerificationRatherThanThrowing(string stored)
    {
        // A corrupted or hand-edited row must not become a way to crash the sign-in endpoint.
        var hasher = Hasher();

        Should.NotThrow(() => hasher.VerifyHashedPassword(User, stored, Password))
            .ShouldBe(PasswordVerificationResult.Failed);
    }

    [Fact]
    public void ATamperedHashDoesNotVerify()
    {
        var hasher = Hasher();
        var hash = hasher.HashPassword(User, Password);
        var tampered = hash[..^2] + (hash[^2] == 'A' ? "BA" : "AA");

        hasher.VerifyHashedPassword(User, tampered, Password)
            .ShouldBe(PasswordVerificationResult.Failed);
    }
}
