using Tailor360.Platform.Abstractions.Results;

namespace Tailor360.Modules.Orders.Domain.Snapshots;

/// <summary>
/// The design copy frozen onto a garment job at confirmation.
/// </summary>
/// <remarks>
/// <para>
/// INV-JOB-01 and <c>docs/prd/design-options.md</c> section 7: the snapshot is a copy, not a reference.
/// Republishing the catalogue, retiring a group or renaming an option never changes a confirmed job,
/// and there is no mutator on this type at all. What the customer agreed to is what the tailor makes.
/// </para>
/// <para>
/// <see cref="CatalogVersionId"/> is the provenance INV-ORD-02 requires: the published catalogue version
/// the selections were made and validated against. It is what makes a confirmed job re-derivable — a
/// reader a year later can say which rules were in force when the choices were taken, even though the
/// rules have moved on since.
/// </para>
/// </remarks>
public sealed record DesignSnapshot
{
    /// <summary>The longest key the column holds. Matches Catalog's <c>CatalogCode.MaximumLength</c>.</summary>
    public const int MaximumKeyLength = 40;

    /// <summary>The longest label the column holds.</summary>
    public const int MaximumLabelLength = 200;

    /// <summary>
    /// The longest free-text craft instruction the column holds.
    /// </summary>
    /// <remarks>
    /// Instructions are what Reception typed that is not any option. They are printed on the job card
    /// and they never price and never move the due date (OD-DES-06); anything that does either is a
    /// design option or an alteration, both of which have their own route.
    /// </remarks>
    public const int MaximumInstructionsLength = 2000;

    /// <summary>The longest standing instruction a conditional-note rule attaches.</summary>
    public const int MaximumNoteLength = 500;

    /// <summary>
    /// The only way to build one, and it is private so that <see cref="Create"/> is the only way in.
    /// </summary>
    /// <remarks>
    /// Not a positional record, and the properties are get-only rather than <c>init</c>, so <c>with</c>
    /// cannot rewrite a frozen snapshot past the factory.
    /// </remarks>
    private DesignSnapshot(
        Guid catalogVersionId,
        string categoryKey,
        string categoryLabel,
        string serviceTypeKey,
        string serviceTypeLabel,
        IReadOnlyList<DesignSelection> selections,
        string? garmentInstructions,
        IReadOnlyList<string> conditionalNotes,
        DateTimeOffset frozenAt)
    {
        CatalogVersionId = catalogVersionId;
        CategoryKey = categoryKey;
        CategoryLabel = categoryLabel;
        ServiceTypeKey = serviceTypeKey;
        ServiceTypeLabel = serviceTypeLabel;
        Selections = selections;
        GarmentInstructions = garmentInstructions;
        ConditionalNotes = conditionalNotes;
        FrozenAt = frozenAt;
    }

    /// <summary>Provenance. The published catalogue version the selections were made against (INV-ORD-02).</summary>
    public Guid CatalogVersionId { get; }

    /// <summary>The category's code, as the catalogue names it.</summary>
    public string CategoryKey { get; }

    /// <summary>The category's label as it stood at confirmation.</summary>
    public string CategoryLabel { get; }

    /// <summary>The service type's code, as the catalogue names it.</summary>
    public string ServiceTypeKey { get; }

    /// <summary>The service type's label as it stood at confirmation.</summary>
    public string ServiceTypeLabel { get; }

    /// <summary>The choices as agreed, in the order the card renders them.</summary>
    public IReadOnlyList<DesignSelection> Selections { get; }

    /// <summary>
    /// What Reception typed that is not any option. Printed on the job card; never priced (OD-DES-06).
    /// </summary>
    public string? GarmentInstructions { get; }

    /// <summary>Standing instructions a conditional-note rule attached at selection.</summary>
    public IReadOnlyList<string> ConditionalNotes { get; }

    /// <summary>When the copy was frozen onto the job, in UTC.</summary>
    public DateTimeOffset FrozenAt { get; }

    /// <summary>
    /// Takes the copy.
    /// </summary>
    /// <remarks>
    /// An empty selection list is accepted: a service type that offers no design group is ordinary, and
    /// a plain alteration has nothing to choose. Two selections answering one group is
    /// <see cref="OrdersErrors.DuplicateDesignGroup"/>, because a card that shows a garment with two
    /// collars is a card a tailor has to guess from.
    /// </remarks>
    /// <param name="catalogVersionId">The published catalogue version the selections were made against.</param>
    /// <param name="categoryKey">The category's code.</param>
    /// <param name="categoryLabel">The category's label as it stands now.</param>
    /// <param name="serviceTypeKey">The service type's code.</param>
    /// <param name="serviceTypeLabel">The service type's label as it stands now.</param>
    /// <param name="selections">The choices as agreed.</param>
    /// <param name="garmentInstructions">Free-text craft instructions, where any were given.</param>
    /// <param name="conditionalNotes">Standing instructions the rules attached, where any were.</param>
    /// <param name="frozenAt">The instant of the confirmation, from <c>IClock</c>.</param>
    /// <returns>The snapshot, or the first failure found.</returns>
    public static Result<DesignSnapshot> Create(
        Guid catalogVersionId,
        string? categoryKey,
        string? categoryLabel,
        string? serviceTypeKey,
        string? serviceTypeLabel,
        IReadOnlyCollection<DesignSelection> selections,
        string? garmentInstructions,
        IReadOnlyCollection<string>? conditionalNotes,
        DateTimeOffset frozenAt)
    {
        ArgumentNullException.ThrowIfNull(selections);

        if (catalogVersionId == Guid.Empty)
        {
            return Result.Failure<DesignSnapshot>(
                OrdersErrors.ConfigurationVersionMissing("catalogVersionId"));
        }

        var category = Required(categoryKey, "categoryKey", MaximumKeyLength);

        if (category.IsFailure)
        {
            return Result.Failure<DesignSnapshot>(category.Error);
        }

        var categoryName = Required(categoryLabel, "categoryLabel", MaximumLabelLength);

        if (categoryName.IsFailure)
        {
            return Result.Failure<DesignSnapshot>(categoryName.Error);
        }

        var serviceType = Required(serviceTypeKey, "serviceTypeKey", MaximumKeyLength);

        if (serviceType.IsFailure)
        {
            return Result.Failure<DesignSnapshot>(serviceType.Error);
        }

        var serviceTypeName = Required(serviceTypeLabel, "serviceTypeLabel", MaximumLabelLength);

        if (serviceTypeName.IsFailure)
        {
            return Result.Failure<DesignSnapshot>(serviceTypeName.Error);
        }

        var groups = new HashSet<string>(selections.Count, StringComparer.Ordinal);

        foreach (var selection in selections)
        {
            // A hole in the list is answered with a refusal rather than with a NullReferenceException
            // thrown from inside the factory: a Create that returns a Result returns one for everything
            // it was handed (convention [5]), which is the shape Order.Confirm uses for a null garment.
            if (selection is null)
            {
                return Result.Failure<DesignSnapshot>(OrdersErrors.Required("selections"));
            }

            if (!groups.Add(selection.GroupCode))
            {
                return Result.Failure<DesignSnapshot>(
                    OrdersErrors.DuplicateDesignGroup(selection.GroupCode));
            }
        }

        var instructions = Blank(garmentInstructions);

        if (instructions is { Length: > MaximumInstructionsLength })
        {
            return Result.Failure<DesignSnapshot>(
                OrdersErrors.TooLong("garmentInstructions", MaximumInstructionsLength));
        }

        var notes = new List<string>();
        var seenNotes = new HashSet<string>(StringComparer.Ordinal);

        foreach (var note in conditionalNotes ?? [])
        {
            var trimmed = Blank(note);

            if (trimmed is null)
            {
                continue;
            }

            if (trimmed.Length > MaximumNoteLength)
            {
                return Result.Failure<DesignSnapshot>(
                    OrdersErrors.TooLong("conditionalNotes", MaximumNoteLength));
            }

            // Two rules can attach the same standing instruction — "press on the reverse" hangs off both
            // the fabric and the lining — and printing it twice makes the card look like it is asking
            // for two different things.
            if (seenNotes.Add(trimmed))
            {
                notes.Add(trimmed);
            }
        }

        return Result.Success(new DesignSnapshot(
            catalogVersionId,
            category.Value,
            categoryName.Value,
            serviceType.Value,
            serviceTypeName.Value,
            [.. selections],
            instructions,
            notes,
            frozenAt));
    }

    private static Result<string> Required(string? value, string field, int maximum)
    {
        var trimmed = value?.Trim();

        if (string.IsNullOrEmpty(trimmed))
        {
            return Result.Failure<string>(OrdersErrors.Required(field));
        }

        return trimmed.Length > maximum
            ? Result.Failure<string>(OrdersErrors.TooLong(field, maximum))
            : Result.Success(trimmed);
    }

    private static string? Blank(string? value)
    {
        var trimmed = value?.Trim();

        return string.IsNullOrEmpty(trimmed) ? null : trimmed;
    }
}
