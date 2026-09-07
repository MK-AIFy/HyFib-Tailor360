using Shouldly;
using Tailor360.Modules.Identity.Domain;
using Tailor360.Modules.Identity.Domain.Mfa;
using Tailor360.Modules.Identity.Domain.Users;

namespace Tailor360.UnitTests.Identity;

/// <summary>
/// Recovery codes are the fallback for a lost authenticator, which makes them a credential in their own
/// right. Two properties carry the whole of their security, and both are asserted here rather than left
/// to a convention a caller could forget: they are stored only as digests, and each one works once.
/// </summary>
[Trait("Category", "Unit")]
public sealed class RecoveryCodeTests
{
    [Fact]
    public void ACodeThatIsNotADigestIsRefused()
        => RecoveryCode.Create(
            IdentityTestData.Id("code"),
            IdentityTestData.Id("user"),
            "3QK7-9F2M-XZ4B",
            IdentityTestData.Now).Error.ShouldBe(IdentityErrors.RecoveryCodeNotHashed);

    [Fact]
    public void AnUppercaseDigestIsRefusedSoTheStoredFormIsExact()
        => RecoveryCode.Create(
            IdentityTestData.Id("code"),
            IdentityTestData.Id("user"),
            IdentityTestData.Digest("code-0").ToUpperInvariant(),
            IdentityTestData.Now).Error.ShouldBe(IdentityErrors.RecoveryCodeNotHashed);

    [Fact]
    public void ACodeIsSpentExactlyOnce()
    {
        var code = RecoveryCode.Create(
            IdentityTestData.Id("code"),
            IdentityTestData.Id("user"),
            IdentityTestData.Digest("code-0"),
            IdentityTestData.Now).Value;

        code.Consume(IdentityTestData.Now).IsSuccess.ShouldBeTrue();
        code.IsConsumed.ShouldBeTrue();
        code.Consume(IdentityTestData.Now).IsFailure.ShouldBeTrue();
    }

    [Fact]
    public void CodesAreOnlyIssuedOnceASecondFactorIsConfirmed()
    {
        // A recovery code for a factor that does not exist is simply a second password, and one the
        // holder is told to write down.
        var user = IdentityTestData.Active();

        user.IssueRecoveryCodes(IdentityTestData.TenCodes(), IdentityTestData.Now).Error
            .ShouldBe(IdentityErrors.MfaNotEnrolled);
    }

    [Fact]
    public void ASheetOutsideTheAllowedSizeIsRefused()
    {
        var user = IdentityTestData.Enrolled();

        user.IssueRecoveryCodes(
            [.. IdentityTestData.TenCodes().Take(3)],
            IdentityTestData.Now).Error.Code.ShouldBe("identity.recovery-code-count-out-of-range");
    }

    [Fact]
    public void TwoIdenticalCodesInOneSheetAreRefused()
    {
        var user = IdentityTestData.Enrolled();
        var duplicate = IdentityTestData.Digest("same");

        var codes = Enumerable.Range(0, 10)
            .Select(i => (IdentityTestData.Id($"dup-{i}"), duplicate))
            .ToArray();

        user.IssueRecoveryCodes(codes, IdentityTestData.Now).Error
            .ShouldBe(IdentityErrors.RecoveryCodesNotDistinct);
    }

    [Fact]
    public void RedeemingSpendsExactlyOneCodeAndNeverThatCodeAgain()
    {
        var user = IdentityTestData.Enrolled();
        user.IssueRecoveryCodes(IdentityTestData.TenCodes(), IdentityTestData.Now);

        user.RedeemRecoveryCode(IdentityTestData.Digest("code-4"), IdentityTestData.Now)
            .IsSuccess.ShouldBeTrue();
        user.UnusedRecoveryCodeCount.ShouldBe(9);

        user.RedeemRecoveryCode(IdentityTestData.Digest("code-4"), IdentityTestData.Now).Error
            .ShouldBe(IdentityErrors.RecoveryCodeInvalid);
        user.UnusedRecoveryCodeCount.ShouldBe(9);
    }

    [Fact]
    public void AnUnknownCodeAndASpentCodeFailIdentically()
    {
        // The two must be indistinguishable, or an attacker holding a photographed sheet learns which
        // of its codes are still worth trying.
        var user = IdentityTestData.Enrolled();
        user.IssueRecoveryCodes(IdentityTestData.TenCodes(), IdentityTestData.Now);
        user.RedeemRecoveryCode(IdentityTestData.Digest("code-0"), IdentityTestData.Now);

        var spent = user.RedeemRecoveryCode(IdentityTestData.Digest("code-0"), IdentityTestData.Now);
        var unknown = user.RedeemRecoveryCode(IdentityTestData.Digest("never-issued"), IdentityTestData.Now);

        spent.Error.ShouldBe(unknown.Error);
    }

    [Fact]
    public void IssuingAFreshSheetDestroysThePreviousOne()
    {
        // Reprinting is what a holder does when they think the old sheet has been seen. It has to mean
        // the old sheet stops working.
        var user = IdentityTestData.Enrolled();
        user.IssueRecoveryCodes(IdentityTestData.TenCodes("old"), IdentityTestData.Now);
        user.IssueRecoveryCodes(IdentityTestData.TenCodes("new"), IdentityTestData.Now);

        user.RedeemRecoveryCode(IdentityTestData.Digest("old-2"), IdentityTestData.Now).Error
            .ShouldBe(IdentityErrors.RecoveryCodeInvalid);
        user.RedeemRecoveryCode(IdentityTestData.Digest("new-2"), IdentityTestData.Now)
            .IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public void AMalformedCandidateIsRejectedWithTheSameAnswerAsAWrongOne()
        => IdentityTestData.Enrolled()
            .RedeemRecoveryCode("not-a-digest", IdentityTestData.Now).Error
            .ShouldBe(IdentityErrors.RecoveryCodeInvalid);

    [Fact]
    public void ResettingTheSecondFactorTakesTheCodesWithIt()
    {
        var user = IdentityTestData.Enrolled();
        user.IssueRecoveryCodes(IdentityTestData.TenCodes(), IdentityTestData.Now);

        user.ResetMfa(IdentityTestData.Now, IdentityTestData.Id("admin"), "Lost authenticator");

        user.RecoveryCodes.ShouldBeEmpty();
        user.MfaEnrolment.ShouldBe(MfaEnrolmentState.ResetRequired);
    }
}
