using Shouldly;
using Tailor360.Modules.Identity.Domain;
using Tailor360.Modules.Identity.Domain.Credentials;

namespace Tailor360.UnitTests.Identity;

/// <summary>
/// The password rules. The point of the design is that length carries the weight and composition rules
/// do not exist, so these tests assert what is rejected and — just as deliberately — what is not.
/// </summary>
[Trait("Category", "Unit")]
public sealed class PasswordPolicyTests
{
    private static readonly PasswordContext Account =
        new("priya.raman", "priya.raman@example.test", "Priya Raman");

    [Fact]
    public void TheFloorIsTwelveCharactersAndConfigurationCannotLowerIt()
    {
        PasswordPolicy.AbsoluteMinimumLength.ShouldBe(12);
        PasswordPolicy.Create(8, 256).IsFailure.ShouldBeTrue();
        PasswordPolicy.Default.MinimumLength.ShouldBe(12);
    }

    [Fact]
    public void ElevenCharactersIsRefusedAndTwelveIsAccepted()
    {
        PasswordPolicy.Default.Check("qwrtypsdfghj", PasswordContext.None).IsAcceptable.ShouldBeTrue();
        PasswordPolicy.Default.Check("qwrtypsdfgh", PasswordContext.None).Failures
            .ShouldContain(IdentityErrors.PasswordTooShort(12));
    }

    [Fact]
    public void NoCompositionRuleIsImposed()
    {
        // Requiring a capital and a symbol pushes people towards Password1!, which is on every list an
        // attacker owns. A long passphrase of plain lower-case words is a better password and is
        // accepted as one.
        PasswordPolicy.Default
            .Check("correct horse battery staple", PasswordContext.None)
            .IsAcceptable.ShouldBeTrue();
    }

    [Fact]
    public void ALongRunOfOneCharacterIsRefusedEvenThoughItIsLongEnough()
        => PasswordPolicy.Default.Check("aaaaaaaaaaaaaaaa", PasswordContext.None).Failures
            .ShouldContain(IdentityErrors.PasswordTooRepetitive(PasswordPolicy.MinimumDistinctCharacters));

    [Theory]
    [InlineData("priya.raman-2026")]
    [InlineData("my PRIYA password")]
    [InlineData("raman-is-my-password")]
    public void APasswordThatRepeatsTheAccountsOwnTextIsRefused(string candidate)
        => PasswordPolicy.Default.Check(candidate, Account).Failures
            .ShouldContain(IdentityErrors.PasswordContainsAccountDetail);

    [Fact]
    public void AShortWordFromTheDisplayNameDoesNotTriggerTheCheck()
    {
        // Otherwise an account whose holder is called "Jo" could not use a password containing "jo",
        // which would rule out most English words.
        var shortName = new PasswordContext("jo", "jo@example.test", "Jo Li");

        PasswordPolicy.Default.Check("enjoyable weather today", shortName).IsAcceptable.ShouldBeTrue();
    }

    [Fact]
    public void EveryBrokenRuleIsReportedAtOnce()
    {
        // Revealing one rule per attempt teaches people to guess at the rules and produces weaker
        // passwords, as well as being a worse experience.
        var result = PasswordPolicy.Default.Check("priyapriya", Account);

        result.Failures.Count.ShouldBeGreaterThan(1);
        result.Failures.ShouldContain(IdentityErrors.PasswordTooShort(12));
        result.Failures.ShouldContain(IdentityErrors.PasswordContainsAccountDetail);
    }

    [Fact]
    public void AnEmptyCandidateIsAMissingValueRatherThanALengthFailure()
        => PasswordPolicy.Default.Check("   ", PasswordContext.None).Failures
            .ShouldHaveSingleItem().Code.ShouldBe("identity.value-required");

    [Fact]
    public void AnOverLongCandidateIsRefusedBeforeItIsScanned()
    {
        // The hasher must never be handed an unbounded string: memory-hard by design means expensive
        // by design, and that is a denial-of-service lever if the length is not capped first.
        var result = PasswordPolicy.Default.Check(new string('x', 1000), PasswordContext.None);

        result.Failures.ShouldHaveSingleItem().Code.ShouldBe("identity.password-too-long");
    }
}
