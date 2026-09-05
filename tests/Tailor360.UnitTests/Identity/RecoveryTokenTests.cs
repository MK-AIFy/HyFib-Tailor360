using Shouldly;
using Tailor360.Modules.Identity.Domain;
using Tailor360.Modules.Identity.Domain.Recovery;
using Tailor360.Modules.Identity.Infrastructure.Security;

namespace Tailor360.UnitTests.Identity;

/// <summary>
/// The recovery token itself: single use, expiring, purpose-bound, and stored only as a digest.
/// </summary>
[Trait("Category", "Unit")]
public sealed class RecoveryTokenTests
{
    private static readonly DateTimeOffset Now = IdentityTestData.Now;
    private static readonly TimeSpan HalfHour = TimeSpan.FromMinutes(30);

    private readonly RecoveryTokenService _service = new();

    [Fact]
    public void AMintedTokenIsUrlSafeRandomnessAndIsStoredOnlyAsItsDigest()
    {
        var issued = _service.Issue();

        issued.Value.Length.ShouldBe(RecoveryTokenService.EncodedLength);
        issued.Value.ShouldAllBe(character => char.IsAsciiLetterOrDigit(character) || character == '-' || character == '_');
        HashedSecret.IsWellFormed(issued.TokenHash).ShouldBeTrue();
        issued.TokenHash.ShouldNotContain(issued.Value, Case.Insensitive);

        _service.DigestOf(issued.Value).ShouldBe(issued.TokenHash);
        _service.Issue().Value.ShouldNotBe(issued.Value);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("too-short")]
    [InlineData("has spaces in it aaaaaaaaaaaaaaaaaaaaaaaaa")]
    public void AValueThatCannotBeATokenDigestsToNothing(string? submitted)
        => _service.DigestOf(submitted).ShouldBeNull();

    [Fact]
    public void ATokenCannotBeIssuedFromAnythingButADigest()
        => RecoveryToken.Issue(
                IdentityTestData.Id("token"),
                IdentityTestData.Id("user"),
                "PLAINTEXT-LINK-VALUE",
                RecoveryPurpose.PasswordReset,
                Now,
                HalfHour)
            .Error.Code.ShouldBe("identity.token-not-hashed");

    [Fact]
    public void ALifetimeLongerThanAnHourIsRefusedHoweverItWasConfigured()
    {
        // A reset link is a password with a timer on it. The ceiling is a constant rather than a
        // setting, so a configuration mistake cannot turn a link into a password valid for a day.
        RecoveryToken.MaximumLifetime.ShouldBe(TimeSpan.FromHours(1));

        Issue(TimeSpan.FromHours(2)).Error.Code.ShouldBe("identity.recovery-token-lifetime-invalid");
        Issue(TimeSpan.FromMinutes(1)).Error.Code.ShouldBe("identity.recovery-token-lifetime-invalid");
        Issue(RecoveryToken.MaximumLifetime).IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public void ATokenIsSpentExactlyOnce()
    {
        var token = Issue(HalfHour).Value;

        token.Consume(RecoveryPurpose.PasswordReset, Now).IsSuccess.ShouldBeTrue();
        token.IsConsumed.ShouldBeTrue();
        token.Consume(RecoveryPurpose.PasswordReset, Now).Error.ShouldBe(IdentityErrors.RecoveryTokenInvalid);
    }

    [Fact]
    public void ATokenIsBoundToThePurposeItWasIssuedFor()
    {
        // An invitation link and a reset link are both "a link in an email". Binding the purpose into
        // the row is what stops one being replayed against the other's endpoint.
        var token = Issue(HalfHour, RecoveryPurpose.Invitation).Value;

        token.Consume(RecoveryPurpose.PasswordReset, Now).Error.ShouldBe(IdentityErrors.RecoveryTokenInvalid);
        token.IsConsumed.ShouldBeFalse();
        token.Consume(RecoveryPurpose.Invitation, Now).IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public void ATokenStopsWorkingWhenItExpires()
    {
        var token = Issue(HalfHour).Value;

        token.IsUsable(Now.Add(HalfHour).AddSeconds(-1)).ShouldBeTrue();
        token.IsUsable(Now.Add(HalfHour)).ShouldBeFalse();
        token.Consume(RecoveryPurpose.PasswordReset, Now.Add(HalfHour))
            .Error.ShouldBe(IdentityErrors.RecoveryTokenInvalid);
    }

    [Fact]
    public void AWithdrawnTokenStaysWithdrawnAndASpentOneIsNotWithdrawnOverTheTop()
    {
        var withdrawn = Issue(HalfHour).Value;
        withdrawn.Invalidate(Now);
        withdrawn.InvalidatedAt.ShouldBe(Now);
        withdrawn.Invalidate(Now.AddMinutes(5));
        withdrawn.InvalidatedAt.ShouldBe(Now);
        withdrawn.Consume(RecoveryPurpose.PasswordReset, Now)
            .Error.ShouldBe(IdentityErrors.RecoveryTokenInvalid);

        var spent = Issue(HalfHour).Value;
        spent.Consume(RecoveryPurpose.PasswordReset, Now);
        spent.Invalidate(Now.AddMinutes(1));
        spent.InvalidatedAt.ShouldBeNull();
    }

    [Fact]
    public void ADigestFromAnotherTokenDoesNotMatch()
    {
        var token = Issue(HalfHour).Value;

        token.Matches(_service.Issue().TokenHash).ShouldBeFalse();
        token.Matches(null).ShouldBeFalse();
    }

    private Tailor360.Platform.Abstractions.Results.Result<RecoveryToken> Issue(
        TimeSpan lifetime,
        RecoveryPurpose purpose = RecoveryPurpose.PasswordReset)
        => RecoveryToken.Issue(
            IdentityTestData.Id("token"),
            IdentityTestData.Id("user"),
            _service.Issue().TokenHash,
            purpose,
            Now,
            lifetime);
}
