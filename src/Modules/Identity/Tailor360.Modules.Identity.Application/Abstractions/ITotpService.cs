namespace Tailor360.Modules.Identity.Application.Abstractions;

/// <summary>
/// Generates and checks time-based one-time passwords, and produces the two things an enrolment screen
/// has to show: the <c>otpauth://</c> link and the same secret written out for typing.
/// </summary>
/// <remarks>
/// Both forms are required, and the reason is the ordinary case rather than an edge case. Most staff
/// enrol on the phone that also holds their authenticator, and a phone cannot photograph its own
/// screen. A QR code alone means those people have to find a second device or give up; the
/// <c>otpauth://</c> link hands the secret straight to the authenticator application, and the grouped
/// manual key lets anyone type it in. The screen shows all three (#23).
/// <para>
/// Implementations must not log the secret, the link, the manual key or a submitted code.
/// </para>
/// </remarks>
public interface ITotpService
{
    /// <summary>Creates a new shared secret and the two forms an enrolment screen shows.</summary>
    /// <param name="issuer">The name the authenticator lists the account under.</param>
    /// <param name="accountName">The account, usually the sign-in name or email address.</param>
    /// <param name="digits">Code length.</param>
    /// <param name="periodSeconds">Step length in seconds.</param>
    TotpSecret Create(string issuer, string accountName, int digits, int periodSeconds);

    /// <summary>
    /// Checks a submitted code against a secret and reports which step matched, so the caller can
    /// refuse a step that has already been accepted.
    /// </summary>
    /// <param name="secretBase32">The shared secret, base-32 encoded.</param>
    /// <param name="code">The code the holder typed. Never logged.</param>
    /// <param name="now">The current instant, from <c>IClock</c>.</param>
    /// <param name="digits">Code length.</param>
    /// <param name="periodSeconds">Step length in seconds.</param>
    /// <param name="driftSteps">
    /// How many steps either side of the present are accepted, for clocks that disagree. One step is
    /// the usual setting; every extra step widens the window in which a captured code still works.
    /// </param>
    TotpVerification Verify(
        string secretBase32,
        string? code,
        DateTimeOffset now,
        int digits,
        int periodSeconds,
        int driftSteps);
}

/// <summary>A newly created shared secret in the three forms an enrolment needs.</summary>
/// <param name="SecretBase32">
/// The raw base-32 secret. Sensitive: it is a credential until the enrolment is confirmed and remains
/// one afterwards. Never log it, never store it unprotected, never return it after confirmation.
/// </param>
/// <param name="ManualEntryKey">
/// The same secret in groups of four characters, which is what a person types when they cannot scan.
/// The enrolment screen puts a <b>Copy</b> control beside it.
/// </param>
/// <param name="OtpAuthUri">
/// The <c>otpauth://totp/…</c> link. The screen encodes it as the QR code and also offers it as
/// <b>Open in authenticator app</b>, which is the path that works when the authenticator is on the
/// same phone as the browser.
/// </param>
public sealed record TotpSecret(string SecretBase32, string ManualEntryKey, string OtpAuthUri);

/// <summary>The outcome of checking one submitted code.</summary>
/// <param name="IsValid">True when the code matched a step inside the drift window.</param>
/// <param name="Step">
/// The step that matched, which the enrolment records so the same code cannot be presented twice. Zero
/// when nothing matched.
/// </param>
public readonly record struct TotpVerification(bool IsValid, long Step);
