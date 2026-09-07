using System.Text;
using Tailor360.Platform.Abstractions.Results;

namespace Tailor360.Modules.Customers.Domain.Naming;

/// <summary>
/// A telephone number as the shop stores it: one canonical form, and the tail the counter searches by.
/// </summary>
/// <param name="E164">
/// The number in E.164 — a plus sign, the country calling code and the national number, digits only.
/// This is the form every comparison and every message uses.
/// </param>
/// <param name="LastSix">
/// The last six digits. Reception searches by the tail of a number, because nobody at a counter types
/// thirteen characters while a customer reads them out; storing the tail is what lets that search use
/// an index instead of scanning every row.
/// </param>
/// <remarks>
/// A phone number is <strong>not unique</strong> in this system and no constraint pretends otherwise:
/// a household shares a number, a shop number is given for a relative, and a number is reassigned by
/// the operator a year later. Issue #26 states it directly — normalise and validate the format without
/// assuming the number identifies a person. It is a strong hint for duplicate detection and never a key.
/// </remarks>
public sealed record TelephoneNumber(string E164, string LastSix)
{
    /// <summary>The longest E.164 number, including the plus sign. E.164 allows fifteen digits.</summary>
    public const int MaximumLength = 16;

    /// <summary>How many trailing digits the counter search compares.</summary>
    public const int SearchTailLength = 6;
}

/// <summary>
/// Reads a telephone number as a person would write it and returns the one form the shop stores.
/// </summary>
/// <remarks>
/// <para>
/// Hand-written rather than delegated to a library, because <c>Domain</c> may reference nothing but
/// <c>Platform.Abstractions</c> (ARCH-001 and ARCH-002). The rules below are the ones an Indian
/// counter actually meets, and the deliberate limit of the design is stated in
/// <see cref="TryRead"/>: a number that already carries a country code is accepted on its shape
/// alone, because this module has no business deciding whether a foreign national number is
/// well formed.
/// </para>
/// </remarks>
public static class TelephoneNumbers
{
    /// <summary>The calling code assumed when a number arrives with none. India, per OD-06.</summary>
    public const string DefaultCallingCode = "91";

    /// <summary>How many digits a national number has in the default region.</summary>
    public const int DefaultRegionNationalLength = 10;

    /// <summary>The fewest digits E.164 accepts after the calling code.</summary>
    private const int MinimumDigits = 8;

    /// <summary>The most digits E.164 accepts in total.</summary>
    private const int MaximumDigits = 15;

    /// <summary>
    /// Reads a written number, or fails when it cannot be read as something that could be dialled.
    /// </summary>
    /// <param name="written">
    /// The number as typed. Whitespace, brackets, dots, slashes and dashes are ignored; any other
    /// character is refused rather than deleted, because deleting one turns a typo into a different
    /// number that can be dialled.
    /// </param>
    /// <param name="field">The field name a validation failure is attached to.</param>
    /// <returns>The canonical number, or a validation failure.</returns>
    /// <remarks>
    /// <para>What it accepts, in the order it tries:</para>
    /// <list type="bullet">
    /// <item><description>
    /// <c>00</c> as a written international prefix, which becomes <c>+</c>. A customer reading a
    /// number off a card written for landline dialling gives it this way.
    /// </description></item>
    /// <item><description>
    /// A leading <c>+</c>: the digits after it are taken as they stand. The length is checked against
    /// E.164 and nothing else — a French or Sri Lankan number is not this module's to validate, and
    /// refusing one because it does not look Indian is worse than storing it.
    /// </description></item>
    /// <item><description>
    /// A single leading <c>0</c>, the national trunk prefix, which is dropped.
    /// </description></item>
    /// <item><description>
    /// Ten bare digits, which are assumed to be a number in the default region.
    /// </description></item>
    /// </list>
    /// </remarks>
    public static Result<TelephoneNumber> TryRead(string? written, string field)
    {
        if (string.IsNullOrWhiteSpace(written))
        {
            return Result.Failure<TelephoneNumber>(CustomersErrors.Required(field));
        }

        var trimmed = written.Trim();
        var international = trimmed.StartsWith('+') || trimmed.StartsWith("00", StringComparison.Ordinal);

        if (!TryReadDigits(trimmed, out var digits) || digits.Length == 0)
        {
            return Result.Failure<TelephoneNumber>(CustomersErrors.PhoneNotUnderstood(field));
        }

        if (international)
        {
            // "00" written for international dialling is the plus sign in another costume.
            if (!trimmed.StartsWith('+') && digits.StartsWith("00", StringComparison.Ordinal))
            {
                digits = digits[2..];
            }

            return Canonicalise(digits, field);
        }

        // A single trunk zero, as a number is written within India. Two zeros would be the
        // international prefix and were handled above, so a leading zero here is the trunk prefix.
        if (digits.Length > DefaultRegionNationalLength && digits[0] == '0')
        {
            digits = digits[1..];
        }

        if (digits.Length == DefaultRegionNationalLength)
        {
            return Canonicalise(DefaultCallingCode + digits, field);
        }

        // Long enough to carry a calling code already, written without the plus.
        return digits.Length > DefaultRegionNationalLength
            ? Canonicalise(digits, field)
            : Result.Failure<TelephoneNumber>(CustomersErrors.PhoneNotUnderstood(field));
    }

    private static Result<TelephoneNumber> Canonicalise(string digits, string field)
    {
        if (digits.Length is < MinimumDigits or > MaximumDigits)
        {
            return Result.Failure<TelephoneNumber>(CustomersErrors.PhoneNotUnderstood(field));
        }

        var tail = digits.Length <= TelephoneNumber.SearchTailLength
            ? digits
            : digits[^TelephoneNumber.SearchTailLength..];

        return Result.Success(new TelephoneNumber("+" + digits, tail));
    }

    /// <summary>
    /// Reads the digits out of a written number, refusing anything that is neither a digit nor one of
    /// the separators a person writes a number with.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Refusing rather than deleting is the whole point of this method.</b> Stripping every
    /// non-digit is the obvious implementation and it is wrong: a capital O typed for a zero makes
    /// <c>90000O21174</c> read as the ten-digit number <c>9000021174</c>, which is a different,
    /// dialable number that a customer will then be messaged at; <c>9000021174 ext 45</c> becomes a
    /// twelve-digit international number; and <c>call me on 9000021174</c> is accepted as a telephone
    /// number. None of those is a number anybody typed, and each of them ends with a message sent to
    /// somebody who is not the customer.
    /// </para>
    /// <para>
    /// So the accepted set is exactly what the contract on <see cref="TryRead"/> promises: digits, a
    /// leading plus sign, and the separators a number is written with — whitespace of any kind
    /// (including the non-breaking space a paste brings), brackets, dots, slashes and any of the
    /// dashes a word processor might have substituted for a hyphen. Everything else is a number that
    /// could not be read, which is what the counter is told.
    /// </para>
    /// </remarks>
    /// <param name="value">The trimmed written number.</param>
    /// <param name="digits">The digits, in order, when every other character was a separator.</param>
    /// <returns>False when the value held a character that is not part of a written number.</returns>
    private static bool TryReadDigits(string value, out string digits)
    {
        var builder = new StringBuilder(value.Length);

        foreach (var character in value)
        {
            if (char.IsAsciiDigit(character))
            {
                builder.Append(character);
                continue;
            }

            // The plus sign is the international marker, so it is accepted while no digit has been
            // read — which is what makes "(+91) 90000 21174" work — and refused once one has, because
            // a plus in the middle of a number is one somebody mistyped rather than one to canonicalise.
            var separator = (character == '+' && builder.Length == 0)
                || char.IsWhiteSpace(character)
                || character is '-' or '.' or '(' or ')' or '/'
                || character is '\u2010' or '\u2011' or '\u2012' or '\u2013' or '\u2014' or '\u2212';

            if (!separator)
            {
                digits = string.Empty;
                return false;
            }
        }

        digits = builder.ToString();
        return true;
    }
}
