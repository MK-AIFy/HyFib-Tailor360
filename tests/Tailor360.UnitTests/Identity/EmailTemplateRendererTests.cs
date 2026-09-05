using Shouldly;
using Tailor360.Modules.Identity.Application.Notifications;
using Tailor360.Modules.Identity.Infrastructure.Email;
using Tailor360.Platform.Abstractions.Ports;

namespace Tailor360.UnitTests.Identity;

/// <summary>
/// What the four messages this module sends actually say, and what they must never say.
/// </summary>
[Trait("Category", "Unit")]
public sealed class EmailTemplateRendererTests
{
    private readonly EmailTemplateRenderer _renderer = new();

    [Fact]
    public void EveryMessageThisModuleSendsHasWording()
    {
        // The mailer names four templates. A key with no wording behind it would fail at the moment
        // somebody needed the message, which for three of these is the worst possible moment.
        EmailTemplateRenderer.TemplateKeys.ShouldBe(
            [
                IdentityMailer.InvitationTemplate,
                IdentityMailer.MfaResetTemplate,
                IdentityMailer.PasswordChangedTemplate,
                IdentityMailer.PasswordRecoveryTemplate,
            ],
            ignoreOrder: true);
    }

    [Fact]
    public void ARecoveryMessageCarriesTheLinkAndNothingSecret()
    {
        var rendered = Render(IdentityMailer.PasswordRecoveryTemplate, new()
        {
            ["displayName"] = "Test User",
            ["link"] = "https://shop.example/recovery/confirm?token=OPAQUE",
            ["minutes"] = "30",
        });

        rendered.PlainTextBody.ShouldContain("https://shop.example/recovery/confirm?token=OPAQUE");
        rendered.PlainTextBody.ShouldContain("30 minutes");
        rendered.Subject.ShouldBe("Set a new Tailor 360 password");
        rendered.Tag.ShouldBe(IdentityMailer.PasswordRecoveryTemplate);

        // Plain text only. An HTML body would let the text of a link differ from its target, which is
        // the exact shape of the phishing message this one will be imitated by.
        rendered.HtmlBody.ShouldBeNull();
    }

    [Fact]
    public void ARecoveryMessageSaysThatTheSecondFactorIsStillComing()
    {
        // A person who resets a password and is then asked for a code thinks something has gone wrong,
        // unless the message that sent them there said it would happen.
        Render(IdentityMailer.PasswordRecoveryTemplate, new()
        {
            ["displayName"] = "Test User",
            ["link"] = "https://shop.example/x",
            ["minutes"] = "30",
        }).PlainTextBody.ShouldContain("still be asked for your authenticator code");
    }

    [Fact]
    public void NoTemplateHasAPlaceholderForAnythingSecret()
    {
        // There is no reason for a password, a recovery code or an authenticator secret to travel by
        // email, so no template may have somewhere to put one.
        foreach (var key in EmailTemplateRenderer.TemplateKeys)
        {
            foreach (var forbidden in new[] { "{password}", "{code}", "{secret}", "{recoveryCode}", "{otpauth}" })
            {
                var supplied = new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["displayName"] = "Test User",
                    ["link"] = "https://shop.example/x",
                    ["minutes"] = "30",
                };

                Render(key, supplied).PlainTextBody.ShouldNotContain(forbidden);
            }
        }
    }

    [Fact]
    public void AMissingValueStopsTheMessageRatherThanSendingHalfOfIt()
    {
        // A message that reached somebody with "{link}" still in it would look exactly like a scam.
        var rendered = _renderer.Render(new EmailTemplateRequest(
            IdentityMailer.PasswordRecoveryTemplate,
            "someone@example.test",
            "en-IN",
            new Dictionary<string, string>(StringComparer.Ordinal) { ["displayName"] = "Test User" }));

        rendered.IsFailure.ShouldBeTrue();
        rendered.Error.ShouldBe(EmailTemplateRenderer.PlaceholderNotSupplied);
    }

    [Fact]
    public void AValueIsNotRescannedForPlaceholders()
    {
        // A display name containing "{link}" must stay those six characters rather than pulling the
        // recovery link into a field it does not belong in.
        var rendered = Render(IdentityMailer.PasswordChangedTemplate, new()
        {
            ["displayName"] = "{link}",
            ["link"] = "https://shop.example/secret",
        });

        rendered.PlainTextBody.ShouldContain("Hello {link},");
        rendered.PlainTextBody.ShouldNotContain("https://shop.example/secret");
    }

    [Fact]
    public void AnUnknownTemplateFails()
        => _renderer.Render(new EmailTemplateRequest(
                "identity.no-such-message", "someone@example.test", "en-IN", new Dictionary<string, string>()))
            .Error.ShouldBe(EmailTemplateRenderer.UnknownTemplate);

    [Fact]
    public void ALanguageWithNoWordingFallsBackRatherThanFailing()
    {
        // The client's Tamil catalogue carries English until a native speaker has reviewed it, and the
        // same gate applies to what a person reads in an email. A message in the wrong language still
        // tells them their password changed; one that was never sent does not.
        var tamil = _renderer.Render(new EmailTemplateRequest(
            IdentityMailer.PasswordChangedTemplate,
            "someone@example.test",
            "ta-IN",
            new Dictionary<string, string>(StringComparer.Ordinal) { ["displayName"] = "Test User" }));

        tamil.IsSuccess.ShouldBeTrue();
        tamil.Value.Subject.ShouldBe("Your Tailor 360 password was changed");
    }

    private EmailMessage Render(string templateKey, Dictionary<string, string> values)
    {
        var rendered = _renderer.Render(
            new EmailTemplateRequest(templateKey, "someone@example.test", "en-IN", values));

        rendered.IsSuccess.ShouldBeTrue();
        return rendered.Value;
    }
}
