using Tailor360.Platform.Abstractions.Results;

namespace Tailor360.Modules.Identity.Api;

/// <summary>
/// Failures the HTTP surface reports that the domain has no opinion about, because they are about the
/// request rather than about the account.
/// </summary>
public static class IdentityApiErrors
{
    /// <summary>The code returned when a throttle refuses an attempt.</summary>
    public const string TooManyAttemptsCode = "identity.too-many-attempts";

    /// <summary>A human check is being asked for and was not answered, or was answered wrongly.</summary>
    public static Error CaptchaRequired { get; } = Error.Validation(
        "identity.captcha-required",
        "Please complete the check that confirms you are not an automated client, and try again.",
        "captchaResponse");

    /// <summary>The request needs a session and did not carry a usable one.</summary>
    public static Error SessionRequired { get; } = Error.Forbidden(
        "identity.session-required",
        "This request needs a signed-in session. Sign in and try again.");

    /// <summary>The named second factor is not one this system offers.</summary>
    public static Error FactorNotRecognised { get; } = Error.Validation(
        "identity.factor-not-recognised",
        "Choose either an authenticator code or a recovery code.",
        "factor");
}
