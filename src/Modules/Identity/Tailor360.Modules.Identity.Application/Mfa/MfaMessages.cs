namespace Tailor360.Modules.Identity.Application.Mfa;

/// <summary>Which second factor a challenge is being answered with.</summary>
public enum MfaFactor
{
    /// <summary>A code from an authenticator application.</summary>
    Totp = 0,

    /// <summary>One of the printed single-use recovery codes.</summary>
    RecoveryCode = 1,
}

/// <summary>What an enrolment screen needs in order to be usable on one device.</summary>
/// <param name="OtpAuthUri">
/// The <c>otpauth://</c> link. The screen renders it as the QR code <em>and</em> offers it as
/// <b>Open in authenticator app</b>, which is the only path that works when the authenticator is on
/// the same phone as the browser — a phone cannot photograph its own screen.
/// </param>
/// <param name="ManualEntryKey">
/// The same secret in groups of four, shown beside the QR code with a <b>Copy</b> control, for anyone
/// whose authenticator will not take a link.
/// </param>
/// <param name="Issuer">The name the authenticator lists the account under.</param>
/// <param name="AccountName">The account name shown beneath it.</param>
/// <param name="Digits">Code length, so the screen can size and validate the input.</param>
/// <param name="PeriodSeconds">Step length, so the screen can show how long a code has left.</param>
/// <remarks>
/// This record carries a live credential in two forms. It is returned once, over the response body of
/// an authenticated request, and never logged: see <c>LogRedaction.SensitivePropertyNames</c>, which
/// names both fields.
/// </remarks>
public sealed record TotpEnrolmentStarted(
    string OtpAuthUri,
    string ManualEntryKey,
    string Issuer,
    string AccountName,
    int Digits,
    int PeriodSeconds);

/// <summary>The one and only moment the recovery codes exist in a readable form.</summary>
/// <param name="RecoveryCodes">
/// The printed codes, in issue order. Shown once, never stored, never logged, never sent by email.
/// </param>
/// <param name="Session">
/// The replacement session, when confirming an enrolment earned one. A correct code off the holder's
/// own authenticator is a second factor satisfied, so the session is rotated to record it; the caller
/// must write the new value to the cookie, or the browser keeps a ticket the server has just revoked.
/// Null when the request carried no session or the rotation could not be applied.
/// </param>
public sealed record TotpEnrolmentConfirmed(
    IReadOnlyList<string> RecoveryCodes,
    Sessions.IssuedSession? Session = null);

/// <summary>The outcome of answering a multi-factor challenge.</summary>
/// <param name="Factor">Which factor answered it, which the audit entry records.</param>
/// <param name="RemainingRecoveryCodes">How many unspent codes the holder has left.</param>
/// <param name="ShouldReissueRecoveryCodes">
/// True when the holder is close enough to running out that the interface should press them to print a
/// new sheet. Someone who reaches zero without noticing has no way back in when they lose their phone.
/// </param>
public sealed record MfaChallengeOutcome(
    MfaFactor Factor,
    int RemainingRecoveryCodes,
    bool ShouldReissueRecoveryCodes);
