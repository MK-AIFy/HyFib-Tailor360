using Tailor360.Platform.Abstractions.Results;

namespace Tailor360.Modules.Orders.Domain.Drafts;

/// <summary>
/// Everything one garment section of a draft holds, validated once.
/// </summary>
/// <remarks>
/// <para>
/// A record rather than a long parameter list on the aggregate, for the reason Customers gives for
/// <c>CustomerDetails</c>: adding a field later should not change every call site, and validation has one
/// home, so adding a garment and saving one cannot disagree about what a valid section is.
/// </para>
/// <para>
/// <strong>Nothing here is measurement data.</strong> The section carries the <em>decision</em> about
/// measurements — take now, take later, or reuse a named confirmed version — and, where one is named, the
/// identifier of that version. The values themselves belong to Customers and are copied onto the garment job
/// only at confirmation (INV-JOB-01). The same holds for images: a reference image is a Media identifier and
/// never a URL or an object key, because every object is streamed by an endpoint that re-authorises the
/// request and logs the access (<c>CLAUDE.md</c> security rule 9).
/// </para>
/// </remarks>
public sealed record OrderDraftGarmentContent
{
    /// <summary>
    /// The longest category or service-type key the column holds. Matches Catalog's
    /// <c>CatalogCode.MaximumLength</c>, declared locally because ARCH-001 forbids the reference.
    /// </summary>
    public const int MaximumKeyLength = 40;

    /// <summary>
    /// The longest instruction text the column holds. Free-text craft instructions never price and never move
    /// the due date (<c>docs/prd/design-options.md</c> OD-DES-06).
    /// </summary>
    public const int MaximumInstructionsLength = 2000;

    /// <summary>
    /// The only way to build one, and it is private so that <see cref="Create"/> is the only way in.
    /// </summary>
    /// <remarks>
    /// Not a positional record, and that is the whole point of the shape. A positional record generates a
    /// <em>public</em> constructor, so any caller could have built a section naming a measurement version
    /// under an intent that reuses none — and <see cref="OrderDraftGarment"/> trusts this type and flattens
    /// its fields without revalidating. Every property below is get-only rather than <c>init</c> for the same
    /// reason: it closes the <c>with</c> expression, which would otherwise be a second way past the factory.
    /// </remarks>
    private OrderDraftGarmentContent(
        string categoryKey,
        string serviceTypeKey,
        Guid catalogVersionId,
        Guid? designSelectionDraftId,
        MeasurementIntent measurementIntent,
        Guid? measurementVersionId,
        Guid? measurementTemplateId,
        DateOnly? dueDate,
        string? instructions,
        IReadOnlyList<Guid> referenceMediaIds)
    {
        CategoryKey = categoryKey;
        ServiceTypeKey = serviceTypeKey;
        CatalogVersionId = catalogVersionId;
        DesignSelectionDraftId = designSelectionDraftId;
        MeasurementIntent = measurementIntent;
        MeasurementVersionId = measurementVersionId;
        MeasurementTemplateId = measurementTemplateId;
        DueDate = dueDate;
        Instructions = instructions;
        ReferenceMediaIds = referenceMediaIds;
    }

    /// <summary>The garment category, as a catalogue key.</summary>
    public string CategoryKey { get; }

    /// <summary>The service type within that category, as a catalogue key.</summary>
    public string ServiceTypeKey { get; }

    /// <summary>
    /// The catalogue version the section is pinned to for its life.
    /// </summary>
    /// <remarks>
    /// The pin holds even when the catalogue is republished mid-draft, so the group set never changes under
    /// the person at the counter (<c>docs/prd/design-options.md</c> section 7). Migrating a section to a newer
    /// version is a deliberate act by Reception, and it arrives here as new content rather than as a silent
    /// re-resolution.
    /// </remarks>
    public Guid CatalogVersionId { get; }

    /// <summary>
    /// The Catalog-owned design selection draft, by id. An identifier and nothing else, because only a
    /// module's <c>Contracts</c> project and the platform libraries cross a module boundary (ARCH-004).
    /// </summary>
    public Guid? DesignSelectionDraftId { get; }

    /// <summary>What the section says about its measurements.</summary>
    public MeasurementIntent MeasurementIntent { get; }

    /// <summary>
    /// The confirmed version being reused. Set only when <see cref="MeasurementIntent"/> is
    /// <see cref="Drafts.MeasurementIntent.ReuseVersion"/>.
    /// </summary>
    public Guid? MeasurementVersionId { get; }

    /// <summary>The template the garment will be measured against, where it is known.</summary>
    public Guid? MeasurementTemplateId { get; }

    /// <summary>
    /// The promised date for this garment. A business date, evaluated in the branch timezone against the
    /// branch working calendar by the caller (<c>docs/architecture/conventions.md</c> section 2.2); nothing
    /// here touches a clock (ARCH-014).
    /// </summary>
    public DateOnly? DueDate { get; }

    /// <summary>What Reception typed that is not any option. Printed on the job card; never priced.</summary>
    public string? Instructions { get; }

    /// <summary>Reference and material images, by Media id. Never a URL and never an object key.</summary>
    public IReadOnlyList<Guid> ReferenceMediaIds { get; }

    /// <summary>
    /// Validates what a person chose and folds it into the form a garment section holds.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <strong>The measurement pair is checked in both directions.</strong> A section that says it is reusing
    /// a version without naming one would confirm against nothing, and a section that names a version under
    /// "take later" would quietly confirm against a measurement the counter believed was still to be taken.
    /// Both are refused here rather than at confirmation, because the counter is where the mistake is visible
    /// and where it can still be corrected.
    /// </para>
    /// <para>
    /// An empty identifier is read as "not supplied" for every optional reference, so a client that sends
    /// <c>00000000-0000-0000-0000-000000000000</c> for a field nobody filled in is not stored as though it had
    /// named something.
    /// </para>
    /// </remarks>
    /// <param name="categoryKey">The garment category key.</param>
    /// <param name="serviceTypeKey">The service type key.</param>
    /// <param name="catalogVersionId">The catalogue version the section is pinned to.</param>
    /// <param name="designSelectionDraftId">The Catalog-owned selection draft, where one exists.</param>
    /// <param name="measurementIntent">What the section says about its measurements.</param>
    /// <param name="measurementVersionId">The version being reused, where one is named.</param>
    /// <param name="measurementTemplateId">The template the garment will be measured against, where known.</param>
    /// <param name="dueDate">The promised date for this garment, as a branch-local date.</param>
    /// <param name="instructions">Free-text craft instructions, where any were given.</param>
    /// <param name="referenceMediaIds">Reference and material images, by Media id.</param>
    /// <returns>The validated content, or the first failure found.</returns>
    public static Result<OrderDraftGarmentContent> Create(
        string? categoryKey,
        string? serviceTypeKey,
        Guid catalogVersionId,
        Guid? designSelectionDraftId,
        MeasurementIntent measurementIntent,
        Guid? measurementVersionId,
        Guid? measurementTemplateId,
        DateOnly? dueDate,
        string? instructions,
        IReadOnlyCollection<Guid>? referenceMediaIds)
    {
        var category = categoryKey?.Trim();
        if (string.IsNullOrEmpty(category))
        {
            return Result.Failure<OrderDraftGarmentContent>(OrdersErrors.Required("categoryKey"));
        }

        if (category.Length > MaximumKeyLength)
        {
            return Result.Failure<OrderDraftGarmentContent>(
                OrdersErrors.TooLong("categoryKey", MaximumKeyLength));
        }

        var serviceType = serviceTypeKey?.Trim();
        if (string.IsNullOrEmpty(serviceType))
        {
            return Result.Failure<OrderDraftGarmentContent>(OrdersErrors.Required("serviceTypeKey"));
        }

        if (serviceType.Length > MaximumKeyLength)
        {
            return Result.Failure<OrderDraftGarmentContent>(
                OrdersErrors.TooLong("serviceTypeKey", MaximumKeyLength));
        }

        // Without a pinned catalogue version there is nothing for the selections to be validated against, and
        // a republish mid-draft would change the questions under the person at the counter.
        if (catalogVersionId == Guid.Empty)
        {
            return Result.Failure<OrderDraftGarmentContent>(OrdersErrors.Required("catalogVersionId"));
        }

        // An intent that is none of the four is not a weaker plan but an uninterpretable one: the
        // **Measurements needed** queue is built from this value and confirmation refuses a garment nobody
        // measured by reading it, so an unnamed member would be counted as decided and would be persisted that
        // way. A value outside the enumeration reaches here only from a cast, and a cast is exactly what a
        // deserialiser does with a number it did not recognise.
        if (!Enum.IsDefined(measurementIntent))
        {
            return Result.Failure<OrderDraftGarmentContent>(OrdersErrors.NotUnderstood("measurementIntent"));
        }

        var reusedVersion = Identifier(measurementVersionId);

        if (measurementIntent is MeasurementIntent.ReuseVersion)
        {
            if (reusedVersion is null)
            {
                return Result.Failure<OrderDraftGarmentContent>(OrdersErrors.MeasurementReuseNeedsVersion);
            }
        }
        else if (reusedVersion is not null)
        {
            return Result.Failure<OrderDraftGarmentContent>(OrdersErrors.MeasurementVersionNotExpected);
        }

        var craft = Blank(instructions);
        if (craft is { Length: > MaximumInstructionsLength })
        {
            return Result.Failure<OrderDraftGarmentContent>(
                OrdersErrors.TooLong("instructions", MaximumInstructionsLength));
        }

        // Order is preserved because it is the order Reception attached the images in, and that is the order
        // the picker and the job card render them in. A repeated identifier is dropped rather than refused:
        // attaching the same photograph twice is a slip of the finger on a phone, not a mistake worth
        // stopping somebody to correct.
        var media = new List<Guid>();
        var seenMedia = new HashSet<Guid>();

        foreach (var mediaId in referenceMediaIds ?? [])
        {
            if (mediaId != Guid.Empty && seenMedia.Add(mediaId))
            {
                media.Add(mediaId);
            }
        }

        return Result.Success(new OrderDraftGarmentContent(
            category,
            serviceType,
            catalogVersionId,
            Identifier(designSelectionDraftId),
            measurementIntent,
            reusedVersion,
            Identifier(measurementTemplateId),
            dueDate,
            craft,
            media));
    }

    private static Guid? Identifier(Guid? value)
        => value is null || value.Value == Guid.Empty ? null : value;

    private static string? Blank(string? value)
    {
        var trimmed = value?.Trim();

        return string.IsNullOrEmpty(trimmed) ? null : trimmed;
    }
}
