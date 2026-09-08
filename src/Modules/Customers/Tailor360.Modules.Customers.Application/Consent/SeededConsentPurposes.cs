using Tailor360.Modules.Customers.Domain.Consent;

namespace Tailor360.Modules.Customers.Application.Consent;

/// <summary>
/// The five purposes an installation starts with, and what each one governs.
/// </summary>
/// <remarks>
/// <para>
/// The set and the descriptions are <c>docs/nfr/data-classification.md</c> section 4.2, transcribed —
/// the "Governs" column of that table, which is a factual statement of what agreeing to each purpose
/// allows the shop to do. They are not the notice a customer is read; that is a wording version, it is
/// published by an Owner after review, and nothing here invents one.
/// </para>
/// <para>
/// A shop may add purposes beyond these; the set is configuration. What these five are is the subset
/// the platform's own rules name, so they are seeded rather than left to somebody to remember.
/// </para>
/// </remarks>
public static class SeededConsentPurposes
{
    /// <summary>The purposes, in the order a counter screen presents them.</summary>
    public static IReadOnlyList<SeededConsentPurpose> All { get; } =
    [
        new(
            ConsentPurposeKeys.MeasurementStorage,
            "Keeping measurements",
            "Keeping a confirmed measurement version for reuse after the order is delivered. Refused, "
            + "measurements are used for the order in hand and deleted at the end of the shortened "
            + "retention window."),
        new(
            ConsentPurposeKeys.PhotoCapture,
            "Photographs",
            "Capturing and storing material, reference or garment images that show a person. Refused, "
            + "no image of a person is captured and a reference image of the garment alone is used."),
        new(
            ConsentPurposeKeys.TransactionalMessages,
            "Messages about an order",
            "Order confirmations, ready-for-delivery, dispatch and delivery messages. Refused, the "
            + "customer is told at the counter and collects without messages; the order still proceeds."),
        new(
            ConsentPurposeKeys.MarketingMessages,
            "Offers and news",
            "Anything that is not about an order in hand. Refused or never given, nothing is sent: "
            + "there is no legitimate-interest override in this system."),
        new(
            ConsentPurposeKeys.FeedbackRequests,
            "Feedback invitations",
            "The post-delivery feedback invitation and any service-recovery follow-up. Refused, no "
            + "invitation is sent; feedback given voluntarily at the counter is still recorded."),
    ];
}

/// <summary>One purpose an installation starts with.</summary>
/// <param name="Key">The stable key other modules and consent records name it by.</param>
/// <param name="Name">The name a counter screen shows.</param>
/// <param name="Description">What agreeing allows, and what happens when it is refused.</param>
public sealed record SeededConsentPurpose(string Key, string Name, string Description);
