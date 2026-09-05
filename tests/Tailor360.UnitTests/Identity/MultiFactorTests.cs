using Shouldly;
using Tailor360.Modules.Identity.Domain;
using Tailor360.Modules.Identity.Domain.Mfa;
using Tailor360.Modules.Identity.Domain.Passkeys;
using Tailor360.Modules.Identity.Domain.Sessions;
using Tailor360.Modules.Identity.Domain.Users;

namespace Tailor360.UnitTests.Identity;

/// <summary>
/// The second factor: authenticator enrolment, passkey registration and the remembered-device
/// concession for shared counter tablets.
/// </summary>
[Trait("Category", "Unit")]
public sealed class MultiFactorTests
{
    [Fact]
    public void AnAuthenticatorIsNotUsableUntilACodeConfirmsIt()
    {
        var user = IdentityTestData.Active();
        user.BeginTotpEnrolment(IdentityTestData.Id("totp"), "protected-secret", IdentityTestData.Now);

        user.MfaEnrolment.ShouldBe(MfaEnrolmentState.PendingConfirmation);
        user.HasConfirmedSecondFactor.ShouldBeFalse();
        user.Totp.ShouldNotBeNull().AcceptStep(1, IdentityTestData.Now).Error
            .ShouldBe(IdentityErrors.MfaNotConfirmed);
    }

    [Fact]
    public void ACodeFromAStepAlreadyUsedIsRefused()
    {
        // A time-based code is valid for a whole step, so one seen over a shoulder or captured by a
        // proxy can be replayed inside that window unless the step is remembered.
        var user = IdentityTestData.Enrolled();
        var totp = user.Totp.ShouldNotBeNull();

        totp.LastAcceptedStep.ShouldBe(1_800_000L);
        totp.AcceptStep(1_800_000L, IdentityTestData.Now).Error.ShouldBe(IdentityErrors.TotpCodeReplayed);
        totp.AcceptStep(1_799_999L, IdentityTestData.Now).Error.ShouldBe(IdentityErrors.TotpCodeReplayed);
        totp.AcceptStep(1_800_001L, IdentityTestData.Now).IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public void EnrolmentCannotBeRestartedOverAConfirmedAuthenticator()
        => IdentityTestData.Enrolled()
            .BeginTotpEnrolment(IdentityTestData.Id("second"), "another-secret", IdentityTestData.Now)
            .Error.ShouldBe(IdentityErrors.MfaAlreadyEnrolled);

    [Fact]
    public void APasskeyWhoseCounterDidNotAdvanceIsRefused()
    {
        // A counter that repeats or goes backwards is the documented signal of a cloned authenticator.
        var passkey = Passkey(signatureCounter: 12);

        passkey.RecordUse(12, IdentityTestData.Now, isBackedUp: false).Error
            .ShouldBe(IdentityErrors.PasskeyCounterWentBackwards);
        passkey.RecordUse(11, IdentityTestData.Now, isBackedUp: false).IsFailure.ShouldBeTrue();
        passkey.RecordUse(13, IdentityTestData.Now, isBackedUp: false).IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public void AnAuthenticatorThatKeepsNoCounterStillWorks()
    {
        // Platform authenticators commonly report zero for ever; refusing them would rule out passkeys
        // on most phones.
        var passkey = Passkey(signatureCounter: 0);

        passkey.RecordUse(0, IdentityTestData.Now, isBackedUp: true).IsSuccess.ShouldBeTrue();
        passkey.RecordUse(0, IdentityTestData.Now, isBackedUp: true).IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public void TheSamePasskeyCannotBeRegisteredTwice()
    {
        var user = IdentityTestData.Active();
        user.RegisterPasskey(Passkey(), IdentityTestData.Now).IsSuccess.ShouldBeTrue();

        user.RegisterPasskey(Passkey(), IdentityTestData.Now).Error
            .ShouldBe(IdentityErrors.PasskeyAlreadyRegistered);
    }

    [Fact]
    public void TheLastFactorCannotBeRemovedFromAnAccountThatHasOne()
    {
        // Otherwise a cashier removes their only passkey and locks the shop out of its own takings —
        // or somebody holding their password does it for them, one passkey at a time, and the next
        // sign-in asks for nothing but the password.
        //
        // The rule is "not to zero", not "not to zero when a policy says so". It used to take the
        // policy's answer, every caller answered "not required" because roles are not modelled yet,
        // and the guard therefore never fired at all.
        var user = IdentityTestData.Active();
        var passkey = Passkey();
        user.RegisterPasskey(passkey, IdentityTestData.Now);

        user.RemovePasskey(passkey.Id, IdentityTestData.Now).Error
            .ShouldBe(IdentityErrors.LastFactorCannotBeRemoved);

        // Still there, still the account's protection.
        user.Passkeys.ShouldHaveSingleItem();
        user.MfaEnrolment.ShouldBe(MfaEnrolmentState.Enrolled);
    }

    [Fact]
    public void APasskeyCanBeRemovedWhileAnAuthenticatorRemains()
    {
        var user = IdentityTestData.Enrolled();
        var passkey = Passkey();
        user.RegisterPasskey(passkey, IdentityTestData.Now);

        user.RemovePasskey(passkey.Id, IdentityTestData.Now).IsSuccess.ShouldBeTrue();
        user.MfaEnrolment.ShouldBe(MfaEnrolmentState.Enrolled);
    }

    [Fact]
    public void ADeviceIsRememberedOnlyOnceASecondFactorExists()
        => IdentityTestData.Active().RememberDevice(
            IdentityTestData.Id("device"),
            IdentityTestData.Digest("device-token"),
            "Counter tablet",
            IdentityTestData.Now,
            TimeSpan.FromDays(30)).Error.ShouldBe(IdentityErrors.MfaNotEnrolled);

    [Fact]
    public void ADeviceCannotBeRememberedForLongerThanThirtyDays()
        => IdentityTestData.Enrolled().RememberDevice(
            IdentityTestData.Id("device"),
            IdentityTestData.Digest("device-token"),
            "Counter tablet",
            IdentityTestData.Now,
            TimeSpan.FromDays(31)).Error
            .ShouldBe(IdentityErrors.TrustedDeviceLifetimeTooLong(TrustedDevice.MaximumLifetimeDays));

    [Fact]
    public void ARememberedDeviceLapsesAndCanBeForgottenBeforeThat()
    {
        var user = IdentityTestData.Enrolled();
        var device = user.RememberDevice(
            IdentityTestData.Id("device"),
            IdentityTestData.Digest("device-token"),
            "Counter tablet",
            IdentityTestData.Now,
            TimeSpan.FromDays(30)).Value;

        user.FindUsableDevice(IdentityTestData.Digest("device-token"), IdentityTestData.Now)
            .ShouldNotBeNull();
        user.FindUsableDevice(
            IdentityTestData.Digest("device-token"), IdentityTestData.Now.AddDays(31)).ShouldBeNull();

        user.ForgetDevice(device.Id, IdentityTestData.Now).IsSuccess.ShouldBeTrue();
        user.FindUsableDevice(IdentityTestData.Digest("device-token"), IdentityTestData.Now).ShouldBeNull();
    }

    [Fact]
    public void ARememberedDeviceDoesNotCountAsAStrongAuthentication()
    {
        // Skipping the challenge is a convenience for a shared counter; it must never satisfy a
        // step-up on administration or billing.
        var session = Session.Start(
            IdentityTestData.Id("session"),
            IdentityTestData.Id("user"),
            IdentityTestData.Organisation,
            null,
            IdentityTestData.Digest("ticket"),
            "Counter tablet",
            IdentityTestData.Now,
            SessionLifetime.Default).Value;

        session.AttachTrustedDevice(IdentityTestData.Id("device"));

        session.MfaSatisfied.ShouldBeFalse();
        session.IsStepUpFresh(IdentityTestData.Now, TimeSpan.FromMinutes(5)).ShouldBeFalse();
    }

    private static PasskeyCredential Passkey(long signatureCounter = 0)
        => PasskeyCredential.Register(
            IdentityTestData.Id("passkey"),
            IdentityTestData.Id("user"),
            [1, 2, 3, 4],
            [9, 8, 7, 6],
            IdentityTestData.Id("authenticator"),
            "Workroom laptop",
            signatureCounter,
            IdentityTestData.Now).Value;
}
