using System.Text.RegularExpressions;

namespace Tailor360.Platform.Observability.Logging;

/// <summary>
/// Redaction applied to anything that reaches a log sink. Logs are treated as a lower trust store than
/// the database: secrets must never appear, and personal data appears only in a masked form.
/// </summary>
public static partial class LogRedaction
{
    /// <summary>The text substituted for a redacted value.</summary>
    public const string Placeholder = "[redacted]";

    /// <summary>
    /// Property names whose values are never written to a log, in any casing or separator style.
    /// Matching is on a normalised form so that <c>api_key</c>, <c>ApiKey</c> and <c>apikey</c> all match.
    /// </summary>
    public static IReadOnlySet<string> SensitivePropertyNames { get; } = new HashSet<string>(StringComparer.Ordinal)
    {
        "password", "passwordhash", "currentpassword", "newpassword",
        "secret", "clientsecret", "apikey", "apitoken", "token", "accesstoken", "refreshtoken",
        "authorization", "cookie", "setcookie", "sessionticket", "antiforgerytoken",
        "mfasecret", "totpsecret", "recoverycode", "recoverycodes", "otp",
        "connectionstring", "privatekey", "certificate", "certificatepassword",
        "backupkey", "encryptionkey", "dataprotectionkey", "webhooksecret", "signature",
        "aadhaar", "pan", "upiid", "cardnumber", "cvv",
    };

    /// <summary>True when a property with this name must have its value redacted.</summary>
    public static bool IsSensitive(string propertyName)
    {
        if (string.IsNullOrEmpty(propertyName))
        {
            return false;
        }

        Span<char> buffer = stackalloc char[propertyName.Length];
        var length = 0;
        foreach (var c in propertyName)
        {
            if (char.IsAsciiLetterOrDigit(c))
            {
                buffer[length++] = char.ToLowerInvariant(c);
            }
        }

        return SensitivePropertyNames.Contains(new string(buffer[..length]));
    }

    /// <summary>
    /// Masks an Indian mobile number so that a support engineer can still correlate it with a customer
    /// record without the log itself carrying the full number.
    /// </summary>
    public static string MaskPhone(string phone)
        => string.IsNullOrWhiteSpace(phone) || phone.Length < 4
            ? Placeholder
            : string.Concat(new string('*', phone.Length - 4), phone.AsSpan(phone.Length - 4));

    /// <summary>Masks an email address, keeping only the first character and the domain.</summary>
    public static string MaskEmail(string email)
    {
        var at = email.IndexOf('@', StringComparison.Ordinal);
        return at <= 0 ? Placeholder : string.Concat(email.AsSpan(0, 1), "***", email.AsSpan(at));
    }

    /// <summary>
    /// Removes anything in free text that looks like a bearer token or a long secret. This is a safety
    /// net for text the application did not construct, not a substitute for structured logging.
    /// </summary>
    public static string ScrubFreeText(string text)
        => string.IsNullOrEmpty(text) ? text : BearerPattern().Replace(text, $"Bearer {Placeholder}");

    [GeneratedRegex(@"Bearer\s+[A-Za-z0-9\-._~+/]{12,}=*", RegexOptions.IgnoreCase, matchTimeoutMilliseconds: 200)]
    private static partial Regex BearerPattern();
}
