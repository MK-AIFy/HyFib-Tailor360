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

    /// <summary>
    /// An administrative change arrived without the reason its permission demands.
    /// </summary>
    /// <remarks>
    /// A field error rather than a flat refusal, so the screen can point at the box the person left
    /// empty. The reason is the part of an administrative change that a reviewer reads first — the
    /// change itself is visible in the trail either way — so it is worth refusing the request over.
    /// </remarks>
    public static Error ReasonRequired { get; } = Error.Validation(
        "identity.reason-required",
        "Say why you are making this change. It is recorded in the audit trail with your name.",
        "reason");

    /// <summary>
    /// The reason given is longer than the audit trail stores. Refused rather than truncated: a reason
    /// silently cut in half reads as a complete sentence that says something else.
    /// </summary>
    public static Error ReasonTooLong { get; } = Error.Validation(
        "identity.reason-too-long",
        $"Keep the reason under {Payloads.AdminRequests.MaximumReasonLength} characters.",
        "reason");

    /// <summary>The account status filter named a state that does not exist.</summary>
    public static Error StatusNotRecognised { get; } = Error.Validation(
        "identity.status-not-recognised",
        "Filter by Invited, Active, Suspended or Deactivated.",
        "status");

    /// <summary>The module code names a module this system does not have.</summary>
    public static Error ModuleNotRecognised { get; } = Error.Validation(
        "identity.module-not-recognised",
        "That is not a module of this system. A toggle for one that does not exist would be a switch "
        + "nothing reads.",
        "code");

    /// <summary>
    /// An audit query named a subject identifier without saying what kind of thing it is.
    /// </summary>
    /// <remarks>
    /// Identifiers are unique, so the query would work — and it would use no index, because the trail
    /// is indexed on the pair. Refusing is cheaper than a table scan somebody triggers by accident.
    /// </remarks>
    public static Error AuditSubjectIncomplete { get; } = Error.Validation(
        "identity.audit-subject-incomplete",
        "Say what kind of thing the identifier names, for example StaffUser or Branch.",
        "entityType");

    /// <summary>The named second factor is not one this system offers.</summary>
    public static Error FactorNotRecognised { get; } = Error.Validation(
        "identity.factor-not-recognised",
        "Choose either an authenticator code or a recovery code.",
        "factor");
}
