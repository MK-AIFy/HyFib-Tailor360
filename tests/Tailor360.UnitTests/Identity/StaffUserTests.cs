using Shouldly;
using Tailor360.Modules.Identity.Domain;
using Tailor360.Modules.Identity.Domain.Lockout;
using Tailor360.Modules.Identity.Domain.Users;

namespace Tailor360.UnitTests.Identity;

/// <summary>
/// The staff account's own rules. These are the invariants that must hold whatever the storage is, so
/// they are asserted against the aggregate with no database and no host in sight.
/// </summary>
[Trait("Category", "Unit")]
public sealed class StaffUserTests
{
    [Fact]
    public void AnInvitedAccountCannotAuthenticateUntilItHasAPassword()
    {
        var user = IdentityTestData.Invited();

        user.Status.ShouldBe(UserStatus.Invited);
        user.EnsureCanAuthenticate(IdentityTestData.Now).Error
            .ShouldBe(IdentityErrors.UserNotActivated);
    }

    [Fact]
    public void SettingTheFirstPasswordCompletesTheInvitation()
    {
        var user = IdentityTestData.Active();

        user.Status.ShouldBe(UserStatus.Active);
        user.EnsureCanAuthenticate(IdentityTestData.Now).IsSuccess.ShouldBeTrue();
        user.Password.ShouldNotBeNull();
    }

    [Fact]
    public void TheSignInNameIsFoldedSoTwoCapitalisationsAreOneAccount()
        => StaffUser.Invite(
            IdentityTestData.Id("mixed"),
            IdentityTestData.Organisation,
            "  Priya.Raman  ",
            "priya@example.test",
            "Priya Raman",
            IdentityTestData.Now).Value.UserName.ShouldBe("priya.raman");

    [Theory]
    [InlineData("no-at-sign")]
    [InlineData("no@domain")]
    [InlineData("spaced address@example.test")]
    [InlineData("@example.test")]
    public void AnUndeliverableAddressIsRefused(string email)
        => StaffUser.Invite(
            IdentityTestData.Id(email),
            IdentityTestData.Organisation,
            "user",
            email,
            "Test User",
            IdentityTestData.Now).Error.ShouldBe(IdentityErrors.EmailNotUsable);

    [Fact]
    public void ADeactivatedAccountCannotAuthenticate()
    {
        var user = IdentityTestData.Active();
        user.Deactivate(IdentityTestData.Now, null);

        user.EnsureCanAuthenticate(IdentityTestData.Now).Error.ShouldBe(IdentityErrors.UserDeactivated);
    }

    [Fact]
    public void DeactivationForgetsEveryRememberedDevice()
    {
        // A device that could skip the second-factor challenge for a departed member of staff is the
        // exact thing an offboarding process is for.
        var user = IdentityTestData.Enrolled();
        user.RememberDevice(
            IdentityTestData.Id("device"),
            IdentityTestData.Digest("device-token"),
            "Counter tablet",
            IdentityTestData.Now,
            TimeSpan.FromDays(30));

        user.Deactivate(IdentityTestData.Now, null);

        user.FindUsableDevice(IdentityTestData.Digest("device-token"), IdentityTestData.Now)
            .ShouldBeNull();
    }

    [Fact]
    public void ADeactivatedAccountCannotBeSuspendedBackIntoUse()
    {
        var user = IdentityTestData.Active();
        user.Deactivate(IdentityTestData.Now, null);

        user.Suspend(IdentityTestData.Now, null).IsFailure.ShouldBeTrue();
    }

    [Fact]
    public void ReactivationRequiresAReasonAndStartsTheCredentialsAgain()
    {
        var user = IdentityTestData.Enrolled();
        user.Deactivate(IdentityTestData.Now, null);

        user.Reactivate(IdentityTestData.Now, IdentityTestData.Id("admin"), " ").Error
            .ShouldBe(IdentityErrors.ReasonRequired);

        user.Reactivate(IdentityTestData.Now, IdentityTestData.Id("admin"), "Returned after leave")
            .IsSuccess.ShouldBeTrue();

        user.Status.ShouldBe(UserStatus.Invited);
        user.Password.ShouldBeNull();
        user.Totp.ShouldBeNull();
        user.MustChangePassword.ShouldBeTrue();
        user.MfaEnrolment.ShouldBe(MfaEnrolmentState.ResetRequired);
    }

    [Fact]
    public void NothingLocksTheAccountUntilTheThresholdIsPassed()
    {
        var user = IdentityTestData.Active();
        var policy = LockoutPolicy.Default;

        for (var attempt = 0; attempt < policy.Threshold - 1; attempt++)
        {
            user.RecordFailedSignIn(policy, IdentityTestData.Now);
        }

        user.IsLockedOut(IdentityTestData.Now).ShouldBeFalse();
        user.EnsureCanAuthenticate(IdentityTestData.Now).IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public void TheLockoutGrowsWithEachFurtherFailure()
    {
        var user = IdentityTestData.Active();
        var policy = LockoutPolicy.Default;

        for (var attempt = 0; attempt < policy.Threshold; attempt++)
        {
            user.RecordFailedSignIn(policy, IdentityTestData.Now);
        }

        var first = user.LockedOutUntil.ShouldNotBeNull();
        first.ShouldBe(IdentityTestData.Now + policy.BaseDuration);

        user.RecordFailedSignIn(policy, IdentityTestData.Now);
        user.LockedOutUntil.ShouldNotBeNull().ShouldBe(IdentityTestData.Now + (policy.BaseDuration * 2));

        user.EnsureCanAuthenticate(IdentityTestData.Now).Error.ShouldBe(IdentityErrors.UserLockedOut);
    }

    [Fact]
    public void ASuccessfulSignInClearsTheLockout()
    {
        var user = IdentityTestData.Active();
        for (var attempt = 0; attempt < 8; attempt++)
        {
            user.RecordFailedSignIn(LockoutPolicy.Default, IdentityTestData.Now);
        }

        user.RecordSuccessfulSignIn(IdentityTestData.Now);

        user.FailedSignInCount.ShouldBe(0);
        user.LockedOutUntil.ShouldBeNull();
        user.LastSignInAt.ShouldBe(IdentityTestData.Now);
    }

    [Fact]
    public void ChangingThePasswordNeverClearsTheSecondFactor()
    {
        // Recovery must not become a way round multi-factor: after a reset the holder still has to
        // complete their authenticator challenge.
        var user = IdentityTestData.Enrolled();

        user.SetPassword(
            IdentityTestData.Id("new-credential"),
            IdentityTestData.EncodedHash,
            "argon2id",
            IdentityTestData.Now,
            null).IsSuccess.ShouldBeTrue();

        user.Totp.ShouldNotBeNull().IsConfirmed.ShouldBeTrue();
        user.MfaEnrolment.ShouldBe(MfaEnrolmentState.Enrolled);
    }

    [Fact]
    public void ResettingTheSecondFactorNeedsAReasonAndForcesReEnrolment()
    {
        var user = IdentityTestData.Enrolled();
        user.IssueRecoveryCodes(IdentityTestData.TenCodes(), IdentityTestData.Now);

        user.ResetMfa(IdentityTestData.Now, IdentityTestData.Id("admin"), null).Error
            .ShouldBe(IdentityErrors.ReasonRequired);

        user.ResetMfa(IdentityTestData.Now, IdentityTestData.Id("admin"), "Lost phone, identity verified in person")
            .IsSuccess.ShouldBeTrue();

        user.Totp.ShouldBeNull();
        user.UnusedRecoveryCodeCount.ShouldBe(0);
        user.MfaEnrolment.ShouldBe(MfaEnrolmentState.ResetRequired);
    }

    [Fact]
    public void APasswordCannotBeSetOnADeactivatedAccount()
    {
        var user = IdentityTestData.Active();
        user.Deactivate(IdentityTestData.Now, null);

        user.SetPassword(
            IdentityTestData.Id("credential"),
            IdentityTestData.EncodedHash,
            "argon2id",
            IdentityTestData.Now,
            null).Error.ShouldBe(IdentityErrors.UserDeactivated);
    }

    [Fact]
    public void AValueThatIsNotAnEncodedHashIsRefusedAsACredential()
        => IdentityTestData.Invited().SetPassword(
            IdentityTestData.Id("credential"),
            "correct horse battery staple",
            "argon2id",
            IdentityTestData.Now,
            null).Error.ShouldBe(IdentityErrors.CredentialNotEncoded);
}
