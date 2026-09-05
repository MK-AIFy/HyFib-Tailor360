using OtpNet;
using Shouldly;
using Tailor360.Modules.Identity.Infrastructure.Security;

namespace Tailor360.UnitTests.Identity;

/// <summary>
/// The authenticator itself: the two forms an enrolment screen shows, and what the verifier accepts.
/// </summary>
[Trait("Category", "Unit")]
public sealed class TotpServiceTests
{
    private static readonly DateTimeOffset Now = IdentityTestData.Now;

    private readonly TotpService _service = new();

    [Fact]
    public void EnrolmentOffersBothTheLinkAndAKeyThatCanBeTyped()
    {
        // A person enrolling on the phone that also holds the authenticator cannot photograph their own
        // screen, so the QR code alone is not enough. The link hands the secret over directly and the
        // grouped key can be typed; #23 requires both beside the code.
        var secret = _service.Create("HyFib Tailor 360", "asha.counter", 6, 30);

        secret.OtpAuthUri.ShouldStartWith("otpauth://totp/HyFib%20Tailor%20360:asha.counter?");
        secret.OtpAuthUri.ShouldContain("issuer=HyFib%20Tailor%20360");
        secret.OtpAuthUri.ShouldContain("algorithm=SHA1");
        secret.OtpAuthUri.ShouldContain("digits=6");
        secret.OtpAuthUri.ShouldContain("period=30");

        secret.ManualEntryKey.ShouldContain(" ");
        secret.ManualEntryKey.Replace(" ", string.Empty, StringComparison.Ordinal)
            .ShouldBe(secret.SecretBase32);
    }

    [Fact]
    public void TheSecretIsOneHundredAndSixtyBitsAndDiffersEveryTime()
    {
        var first = _service.Create("Issuer", "user", 6, 30);
        var second = _service.Create("Issuer", "user", 6, 30);

        Base32Encoding.ToBytes(first.SecretBase32).Length.ShouldBe(TotpService.SecretBytes);
        first.SecretBase32.ShouldNotBe(second.SecretBase32);
    }

    [Fact]
    public void ACodeFromTheAuthenticatorIsAcceptedAndReportsItsStep()
    {
        var secret = _service.Create("Issuer", "user", 6, 30);
        var code = new Totp(Base32Encoding.ToBytes(secret.SecretBase32)).ComputeTotp(Now.UtcDateTime);

        var verified = _service.Verify(secret.SecretBase32, code, Now, 6, 30, 1);

        verified.IsValid.ShouldBeTrue();

        // The step is what the enrolment stores so the same code cannot be presented twice.
        verified.Step.ShouldBe(Now.ToUnixTimeSeconds() / 30);
    }

    [Fact]
    public void ACodeFromTheStepBeforeIsAcceptedWithinTheDriftWindowAndRejectedOutsideIt()
    {
        // Phone clocks disagree with servers. One step either way covers ordinary drift; every extra
        // step lengthens the window in which a code seen over a shoulder still works, so the window is
        // a setting and this test pins both ends of it.
        var secret = _service.Create("Issuer", "user", 6, 30);
        var key = Base32Encoding.ToBytes(secret.SecretBase32);
        var oneStepAgo = new Totp(key).ComputeTotp(Now.AddSeconds(-30).UtcDateTime);
        var threeStepsAgo = new Totp(key).ComputeTotp(Now.AddSeconds(-90).UtcDateTime);

        _service.Verify(secret.SecretBase32, oneStepAgo, Now, 6, 30, 1).IsValid.ShouldBeTrue();
        _service.Verify(secret.SecretBase32, oneStepAgo, Now, 6, 30, 0).IsValid.ShouldBeFalse();
        _service.Verify(secret.SecretBase32, threeStepsAgo, Now, 6, 30, 1).IsValid.ShouldBeFalse();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("12345")]
    [InlineData("1234567")]
    [InlineData("abcdef")]
    [InlineData("12 34 5a")]
    public void AnythingThatIsNotSixDigitsIsRefusedWithoutComputingAnything(string? code)
    {
        var secret = _service.Create("Issuer", "user", 6, 30);

        var verified = _service.Verify(secret.SecretBase32, code, Now, 6, 30, 1);

        verified.IsValid.ShouldBeFalse();
        verified.Step.ShouldBe(0);
    }

    [Fact]
    public void SpacesTypedRoundTheCodeAreIgnored()
    {
        // Some authenticators display "123 456" and people copy what they see.
        var secret = _service.Create("Issuer", "user", 6, 30);
        var code = new Totp(Base32Encoding.ToBytes(secret.SecretBase32)).ComputeTotp(Now.UtcDateTime);
        var spaced = string.Concat(code.AsSpan(0, 3), " ", code.AsSpan(3));

        _service.Verify(secret.SecretBase32, spaced, Now, 6, 30, 1).IsValid.ShouldBeTrue();
    }

    [Fact]
    public void ASecretThatIsNotBase32FailsTheCheckRatherThanTheRequest()
    {
        // A corrupt stored secret is an operator problem. It must not become an exception on the
        // sign-in path, where it would be a five-hundred rather than a refusal.
        var verified = _service.Verify("not base32 at all!!", "123456", Now, 6, 30, 1);

        verified.IsValid.ShouldBeFalse();
    }
}
