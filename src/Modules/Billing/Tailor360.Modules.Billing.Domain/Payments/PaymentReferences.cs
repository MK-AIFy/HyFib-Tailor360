using System.Text.RegularExpressions;
using Tailor360.Platform.Abstractions.Results;

namespace Tailor360.Modules.Billing.Domain.Payments;

/// <summary>
/// The external reference a payment may carry — a UPI transaction identifier, a card terminal's
/// authorisation code, a bank transfer's reference — and the one thing it must never be: a card number.
/// Card PAN, CVV and track data are never stored or logged (plan section 4.5, INV-PAY-07); a value that
/// looks like a card number, thirteen to nineteen digits once the spaces and hyphens a keypad adds are
/// taken out, is refused before it reaches a row or a log line.
/// </summary>
public static partial class PaymentReferences
{
    /// <summary>The longest reference a payment carries.</summary>
    public const int MaximumLength = 100;

    /// <summary>
    /// Checks a reference: trimmed, bounded, printable, and not a card number. Null or blank is returned
    /// as null; whether the mode requires one is the mode's rule, not this one.
    /// </summary>
    /// <param name="reference">What the cashier typed or the terminal printed.</param>
    /// <param name="field">The field, for the error.</param>
    public static Result<string?> Check(string? reference, string field)
    {
        var trimmed = reference?.Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            return Result.Success<string?>(null);
        }

        if (trimmed.Length > MaximumLength)
        {
            return Result.Failure<string?>(BillingErrors.TooLong(field, MaximumLength));
        }

        if (trimmed.Any(char.IsControl))
        {
            return Result.Failure<string?>(BillingErrors.ReferenceNotWellFormed(field));
        }

        if (LooksLikeACardNumber(trimmed))
        {
            return Result.Failure<string?>(BillingErrors.ReferenceLooksLikeACard(field));
        }

        return Result.Success<string?>(trimmed);
    }

    /// <summary>Whether a value carries a run of thirteen to nineteen digits once spaces and hyphens are removed.</summary>
    public static bool LooksLikeACardNumber(string value)
    {
        ArgumentNullException.ThrowIfNull(value);

        var compact = Separators().Replace(value, string.Empty);
        return CardLikeRun().IsMatch(compact);
    }

    [GeneratedRegex(@"[\s\-]")]
    private static partial Regex Separators();

    [GeneratedRegex(@"(?<!\d)\d{13,19}(?!\d)")]
    private static partial Regex CardLikeRun();
}
