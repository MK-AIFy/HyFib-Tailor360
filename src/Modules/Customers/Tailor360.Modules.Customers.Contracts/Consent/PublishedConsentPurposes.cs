namespace Tailor360.Modules.Customers.Contracts.Consent;

/// <summary>
/// The consent purpose keys another module is allowed to name.
/// </summary>
/// <remarks>
/// A re-export of the one member of <c>Tailor360.Modules.Customers.Domain.Consent.ConsentPurposeKeys</c>
/// a consumer across the module boundary actually needs — <c>Domain</c> is not a project Media (or
/// anything else outside Customers) may reference (ARCH-004), so a caller that needs the literal
/// <c>"photo_capture"</c> gets it from here rather than typing the string by hand, where a typo would
/// compile and silently ask the wrong purpose. Named differently from its Domain counterpart on
/// purpose: <c>Customers.Application</c> code that imports both
/// <c>Customers.Domain.Consent</c> and <c>Customers.Contracts.Consent</c> in the same file would
/// otherwise hit an ambiguous reference between the two. Kept to exactly this one key: the others are
/// Customers' and Notifications' own concern today, and are added here only when another module
/// genuinely needs to name one.
/// </remarks>
public static class PublishedConsentPurposes
{
    /// <summary>Capturing and storing material, reference or garment images that show a person. Media (issue #592 and onward) stores the identifier of the record it relied on against the object.</summary>
    public const string PhotoCapture = "photo_capture";
}
