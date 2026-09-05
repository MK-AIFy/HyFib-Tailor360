using System.Globalization;
using System.Text;
using OtpNet;
using Tailor360.Modules.Identity.Application.Abstractions;

namespace Tailor360.Modules.Identity.Infrastructure.Security;

/// <summary>
/// Time-based one-time passwords over Otp.NET, in the shape RFC 6238 and the authenticator
/// applications people actually have on their phones expect.
/// </summary>
/// <remarks>
/// Three decisions here are worth stating, because each is the kind that is invisible until it costs
/// somebody their sign-in.
/// <para>
/// <b>The secret is 160 bits.</b> That is the length RFC 4226 requires and what every mainstream
/// authenticator handles; it is also short enough to type off a screen when scanning is impossible.
/// </para>
/// <para>
/// <b>The time comes from the caller.</b> Otp.NET's parameterless overloads read the machine clock,
/// which architecture rule ARCH-014 forbids and which would make the drift window untestable. Every
/// call here passes an instant the caller obtained from <c>IClock</c>.
/// </para>
/// <para>
/// <b>SHA-1 is deliberate.</b> It is what RFC 6238 specifies as the default and what the widely used
/// authenticator applications implement; choosing SHA-256 would produce codes that a person's
/// authenticator computes differently from this server. The construction is HMAC, where SHA-1's
/// collision weakness does not apply, and each code lives for thirty seconds.
/// </para>
/// </remarks>
public sealed class TotpService : ITotpService
{
    /// <summary>The shared-secret length in bytes, per RFC 4226.</summary>
    public const int SecretBytes = 20;

    private const int ManualKeyGroupSize = 4;

    /// <inheritdoc />
    public TotpSecret Create(string issuer, string accountName, int digits, int periodSeconds)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(issuer);
        ArgumentException.ThrowIfNullOrWhiteSpace(accountName);

        var secret = Base32Encoding.ToString(KeyGeneration.GenerateRandomKey(SecretBytes));

        return new TotpSecret(
            secret,
            GroupForTyping(secret),
            BuildOtpAuthUri(issuer, accountName, secret, digits, periodSeconds));
    }

    /// <inheritdoc />
    public TotpVerification Verify(
        string secretBase32,
        string? code,
        DateTimeOffset now,
        int digits,
        int periodSeconds,
        int driftSteps)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(secretBase32);

        var trimmed = code?.Replace(" ", string.Empty, StringComparison.Ordinal);
        if (string.IsNullOrEmpty(trimmed) || trimmed.Length != digits || !trimmed.All(char.IsAsciiDigit))
        {
            return new TotpVerification(false, 0);
        }

        byte[] key;
        try
        {
            key = Base32Encoding.ToBytes(secretBase32);
        }
        catch (ArgumentException)
        {
            // A stored secret that is not base-32 is a corrupt row, not a wrong code. It fails the
            // check like any other wrong answer; the operator sees it through the enrolment being
            // permanently unusable rather than through an exception on the sign-in path.
            return new TotpVerification(false, 0);
        }

        var totp = new Totp(key, periodSeconds, OtpHashMode.Sha1, digits);
        var window = new VerificationWindow(driftSteps, driftSteps);

        var valid = totp.VerifyTotp(now.UtcDateTime, trimmed, out var step, window);
        return new TotpVerification(valid, valid ? step : 0);
    }

    /// <summary>
    /// Builds the <c>otpauth://</c> link by hand rather than through the library's <c>OtpUri</c>,
    /// because the label has to be <c>issuer:account</c> — the form the Key Uri Format specifies and
    /// the form authenticators show in their list — and the library writes the account alone.
    /// </summary>
    private static string BuildOtpAuthUri(
        string issuer,
        string accountName,
        string secret,
        int digits,
        int periodSeconds)
    {
        var label = $"{Uri.EscapeDataString(issuer)}:{Uri.EscapeDataString(accountName)}";

        var query = new StringBuilder()
            .Append("secret=").Append(secret)
            .Append("&issuer=").Append(Uri.EscapeDataString(issuer))
            .Append("&algorithm=SHA1")
            .Append("&digits=").Append(digits.ToString(CultureInfo.InvariantCulture))
            .Append("&period=").Append(periodSeconds.ToString(CultureInfo.InvariantCulture));

        return $"otpauth://totp/{label}?{query}";
    }

    /// <summary>
    /// Breaks the secret into groups of four. Someone reading thirty-two unbroken characters off a
    /// screen loses their place; groups of four are how every other transcribed key is written.
    /// </summary>
    private static string GroupForTyping(string secret)
    {
        var grouped = new StringBuilder(secret.Length + (secret.Length / ManualKeyGroupSize));

        for (var index = 0; index < secret.Length; index++)
        {
            if (index > 0 && index % ManualKeyGroupSize == 0)
            {
                grouped.Append(' ');
            }

            grouped.Append(secret[index]);
        }

        return grouped.ToString();
    }
}
