using Tailor360.Modules.Customers.Domain.Naming;
using Tailor360.Platform.Abstractions.Results;

namespace Tailor360.Modules.Customers.Domain.Customers;

/// <summary>
/// Everything about a customer that a person types, validated once.
/// </summary>
/// <remarks>
/// <para>
/// A record rather than a long parameter list on the aggregate, for the reason the Identity module
/// gives for <c>BranchDetails</c>: adding a field later should not change every call site. It also
/// gives validation one home, so creating a customer and correcting one cannot disagree about what a
/// valid name is.
/// </para>
/// <para>
/// Everything here is personal data. Nothing in this type is ever written to a log, a trace attribute,
/// a problem detail or an audit summary (<c>docs/nfr/data-classification.md</c> section 5.2), which is
/// why the failures it produces name fields and never values.
/// </para>
/// </remarks>
/// <param name="DisplayName">The name as the customer gave it, unaltered.</param>
/// <param name="NormalisedName">The folded search key derived from the name.</param>
/// <param name="NativeName">
/// The optional Tamil-script form. Stored and searched directly; never transliterated, because a
/// transliteration is a spelling nobody chose.
/// </param>
/// <param name="Phone">The primary telephone number.</param>
/// <param name="AlternatePhone">A second number, where one was given.</param>
/// <param name="Email">An email address, where one was given.</param>
/// <param name="AddressLine">The street part of the address.</param>
/// <param name="Locality">The area or town.</param>
/// <param name="Postcode">The postal code.</param>
/// <param name="Language">The language the customer is written to in.</param>
public sealed record CustomerDetails(
    string DisplayName,
    string NormalisedName,
    string? NativeName,
    TelephoneNumber Phone,
    TelephoneNumber? AlternatePhone,
    string? Email,
    string? AddressLine,
    string? Locality,
    string? Postcode,
    string Language)
{
    /// <summary>The longest name the column holds.</summary>
    public const int MaximumDisplayNameLength = 160;

    /// <summary>The longest native-script name the column holds.</summary>
    public const int MaximumNativeNameLength = 160;

    /// <summary>The longest email address the column holds. The limit RFC 5321 sets on a path.</summary>
    public const int MaximumEmailLength = 254;

    /// <summary>The longest street line the column holds.</summary>
    public const int MaximumAddressLineLength = 200;

    /// <summary>The longest locality the column holds.</summary>
    public const int MaximumLocalityLength = 120;

    /// <summary>The longest postal code the column holds.</summary>
    public const int MaximumPostcodeLength = 12;

    /// <summary>The longest language tag the column holds.</summary>
    public const int MaximumLanguageLength = 12;

    /// <summary>
    /// The languages a customer may be written to in.
    /// </summary>
    /// <remarks>
    /// The two the product ships (<c>docs/nfr/accessibility-localisation.md</c>). Tamil is a valid
    /// choice for a <em>customer</em> today even though the staff interface is still behind its
    /// translation gate: what a customer is written to in and what a member of staff reads the screen
    /// in are different decisions.
    /// </remarks>
    public static IReadOnlyList<string> SupportedLanguages { get; } = ["en-IN", "ta-IN"];

    /// <summary>The language assumed when nobody chose one.</summary>
    public const string DefaultLanguage = "en-IN";

    /// <summary>
    /// Validates what a person typed and folds it into the form the record holds.
    /// </summary>
    /// <param name="displayName">The name.</param>
    /// <param name="nativeName">The Tamil-script name, if given.</param>
    /// <param name="phone">The primary telephone number, as written.</param>
    /// <param name="alternatePhone">A second number, as written, if given.</param>
    /// <param name="email">An email address, if given.</param>
    /// <param name="addressLine">The street line, if given.</param>
    /// <param name="locality">The area or town, if given.</param>
    /// <param name="postcode">The postal code, if given.</param>
    /// <param name="language">The language, or null for the default.</param>
    /// <returns>The validated details, or the first failure found.</returns>
    public static Result<CustomerDetails> Create(
        string? displayName,
        string? nativeName,
        string? phone,
        string? alternatePhone,
        string? email,
        string? addressLine,
        string? locality,
        string? postcode,
        string? language)
    {
        var name = displayName?.Trim();
        if (string.IsNullOrEmpty(name))
        {
            return Result.Failure<CustomerDetails>(CustomersErrors.Required("displayName"));
        }

        if (name.Length > MaximumDisplayNameLength)
        {
            return Result.Failure<CustomerDetails>(
                CustomersErrors.TooLong("displayName", MaximumDisplayNameLength));
        }

        var native = Blank(nativeName);
        if (native is { Length: > MaximumNativeNameLength })
        {
            return Result.Failure<CustomerDetails>(
                CustomersErrors.TooLong("nativeName", MaximumNativeNameLength));
        }

        var primary = TelephoneNumbers.TryRead(phone, "phone");
        if (primary.IsFailure)
        {
            return Result.Failure<CustomerDetails>(primary.Error);
        }

        TelephoneNumber? second = null;
        if (Blank(alternatePhone) is { } written)
        {
            var read = TelephoneNumbers.TryRead(written, "alternatePhone");
            if (read.IsFailure)
            {
                return Result.Failure<CustomerDetails>(read.Error);
            }

            second = read.Value;
        }

        var address = Blank(addressLine);
        if (address is { Length: > MaximumAddressLineLength })
        {
            return Result.Failure<CustomerDetails>(
                CustomersErrors.TooLong("addressLine", MaximumAddressLineLength));
        }

        var area = Blank(locality);
        if (area is { Length: > MaximumLocalityLength })
        {
            return Result.Failure<CustomerDetails>(
                CustomersErrors.TooLong("locality", MaximumLocalityLength));
        }

        var code = Blank(postcode);
        if (code is { Length: > MaximumPostcodeLength })
        {
            return Result.Failure<CustomerDetails>(
                CustomersErrors.TooLong("postcode", MaximumPostcodeLength));
        }

        var addressEmail = Blank(email);
        if (addressEmail is not null)
        {
            if (addressEmail.Length > MaximumEmailLength)
            {
                return Result.Failure<CustomerDetails>(
                    CustomersErrors.TooLong("email", MaximumEmailLength));
            }

            // Deliberately shallow. An address is checked by sending to it, not by a pattern: a
            // stricter rule here would refuse a valid address and teach the counter to leave the field
            // empty, which loses more than it protects.
            var at = addressEmail.IndexOf('@', StringComparison.Ordinal);
            if (at <= 0 || at == addressEmail.Length - 1 || addressEmail.Contains(' ', StringComparison.Ordinal))
            {
                return Result.Failure<CustomerDetails>(
                    CustomersErrors.Required("email"));
            }
        }

        var tag = Blank(language) ?? DefaultLanguage;
        if (!SupportedLanguages.Contains(tag, StringComparer.OrdinalIgnoreCase))
        {
            return Result.Failure<CustomerDetails>(CustomersErrors.LanguageNotSupported("language"));
        }

        return Result.Success(new CustomerDetails(
            name,
            CustomerNameNormaliser.Normalise(name),
            native,
            primary.Value,
            second,
            addressEmail,
            address,
            area,
            code,
            tag));
    }

    private static string? Blank(string? value)
    {
        var trimmed = value?.Trim();

        return string.IsNullOrEmpty(trimmed) ? null : trimmed;
    }
}
