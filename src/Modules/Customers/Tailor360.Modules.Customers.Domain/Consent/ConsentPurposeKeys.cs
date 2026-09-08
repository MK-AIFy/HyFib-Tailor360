using Tailor360.Platform.Abstractions.Results;

namespace Tailor360.Modules.Customers.Domain.Consent;

/// <summary>
/// The consent purposes this system's own rules name in code.
/// </summary>
/// <remarks>
/// <para>
/// The <em>set</em> of purposes is configuration, not code:
/// <c>docs/prd/configurable-vs-fixed.md</c> row 75 makes purposes and their wording versions
/// configuration data an Owner maintains, and <see cref="ConsentPurpose"/> is the table that holds
/// them. These five constants are the subset the platform itself refers to — the ones named in
/// <c>docs/nfr/data-classification.md</c> section 4.2 and relied on by other modules, which cannot
/// name a purpose that only exists as a row somebody may rename.
/// </para>
/// <para>
/// Nothing here decides whether a purpose is lawful, required or optional. The lawful basis for each
/// is <strong>DC-01</strong>, an open legal-review decision, and the machinery is deliberately built
/// so that whichever answer the adviser gives is configured rather than re-engineered.
/// </para>
/// </remarks>
public static class ConsentPurposeKeys
{
    /// <summary>Keeping a confirmed measurement version for reuse after the order is delivered.</summary>
    public const string MeasurementStorage = "measurement_storage";

    /// <summary>Capturing and storing material, reference or garment images that show a person.</summary>
    /// <remarks>Media (#31) stores the identifier of the record it relied on against the object.</remarks>
    public const string PhotoCapture = "photo_capture";

    /// <summary>Order confirmations, ready-for-delivery, dispatch and delivery messages.</summary>
    public const string TransactionalMessages = "transactional_messages";

    /// <summary>
    /// Anything that is not about an order in hand.
    /// </summary>
    /// <remarks>
    /// The one purpose whose basis is settled: consent, always. <c>data-classification.md</c> section
    /// 4.2 is explicit that there is no legitimate-interest override in this system, so a send under
    /// this purpose without a standing grant is refused rather than justified.
    /// </remarks>
    public const string MarketingMessages = "marketing_messages";

    /// <summary>The post-delivery feedback invitation and any service-recovery follow-up.</summary>
    public const string FeedbackRequests = "feedback_requests";

    /// <summary>The five purposes <c>init-reference-data</c> seeds, in the order they are presented.</summary>
    public static IReadOnlyList<string> Seeded { get; } =
    [
        MeasurementStorage,
        PhotoCapture,
        TransactionalMessages,
        MarketingMessages,
        FeedbackRequests,
    ];

    /// <summary>The longest purpose key the column holds.</summary>
    public const int MaximumLength = 64;

    /// <summary>
    /// Reads a written purpose key, or says why it is not one.
    /// </summary>
    /// <remarks>
    /// One rule in one place, because two callers need it and they must not disagree.
    /// <see cref="ConsentPurpose.Define"/> applies it when a purpose is created, and
    /// <see cref="ConsentRecord.Record"/> applies it to the key written into an evidentiary row — a
    /// record naming a key no purpose could ever have is one that cannot be resolved back to what the
    /// customer was actually asked, which is the one thing the record exists to carry.
    /// </remarks>
    /// <param name="key">The key as written.</param>
    /// <param name="field">The field name a validation failure is attached to.</param>
    /// <returns>The trimmed key, or the reason it was refused.</returns>
    public static Result<string> Read(string? key, string field)
    {
        var trimmed = key?.Trim();

        if (string.IsNullOrEmpty(trimmed))
        {
            return Result.Failure<string>(CustomersErrors.Required(field));
        }

        if (trimmed.Length > MaximumLength)
        {
            return Result.Failure<string>(CustomersErrors.TooLong(field, MaximumLength));
        }

        // Held to a shape that survives a URL, a log line and a configuration file unaltered.
        return trimmed.All(character =>
            char.IsAsciiLetterLower(character) || char.IsAsciiDigit(character) || character == '_')
            ? Result.Success(trimmed)
            : Result.Failure<string>(CustomersErrors.ConsentPurposeKeyNotAllowed(field));
    }
}
