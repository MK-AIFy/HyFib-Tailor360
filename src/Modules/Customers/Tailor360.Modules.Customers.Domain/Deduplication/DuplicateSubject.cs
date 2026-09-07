namespace Tailor360.Modules.Customers.Domain.Deduplication;

/// <summary>
/// The few facts about a customer that duplicate detection compares.
/// </summary>
/// <remarks>
/// Deliberately not the aggregate. Scoring runs over candidates a query returned, and a query that
/// loaded whole customer records to compare six fields would read every address and every alias in a
/// branch to answer one question at a counter. It also keeps the scoring a pure function, which is
/// what makes it testable without a database.
/// </remarks>
/// <param name="NormalisedName">The folded search key of the name.</param>
/// <param name="NativeName">The Tamil-script name, where there is one.</param>
/// <param name="PhoneE164">The primary telephone number.</param>
/// <param name="AlternatePhoneE164">The second telephone number, where there is one.</param>
/// <param name="Locality">The area or town.</param>
/// <param name="Postcode">The postal code.</param>
public sealed record DuplicateSubject(
    string NormalisedName,
    string? NativeName,
    string PhoneE164,
    string? AlternatePhoneE164,
    string? Locality,
    string? Postcode);
