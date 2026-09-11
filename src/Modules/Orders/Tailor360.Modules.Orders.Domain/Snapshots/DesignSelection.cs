using Tailor360.Platform.Abstractions.Results;

namespace Tailor360.Modules.Orders.Domain.Snapshots;

/// <summary>
/// One design choice inside a frozen design snapshot.
/// </summary>
/// <remarks>
/// <para>
/// It embeds everything the job card and its printed fallback need without a catalogue lookup: the
/// group and option codes <em>and their labels</em>, both display orders, the option version, the
/// price-list item the option's impact resolved to, and the illustration with its alternative text.
/// <c>docs/prd/design-options.md</c> section 7 lists exactly that set, and the reason is stated there:
/// a reprint a year later may find the group retired or reordered, and it still has to render the
/// garment that was agreed.
/// </para>
/// <para>
/// Codes are what a price list, a report and an export refer to; labels are what people read. Both are
/// copied, because renaming an option is a label change that must break nothing — including a job card
/// printed before the rename.
/// </para>
/// </remarks>
public sealed record DesignSelection
{
    /// <summary>The longest code the column holds. Matches Catalog's <c>CatalogCode.MaximumLength</c>.</summary>
    public const int MaximumCodeLength = 40;

    /// <summary>The longest label the column holds.</summary>
    public const int MaximumLabelLength = 200;

    /// <summary>The longest alternative text the column holds.</summary>
    public const int MaximumAlternativeTextLength = 300;

    /// <summary>
    /// The only way to build one, and it is private so that <see cref="Create"/> is the only way in.
    /// </summary>
    /// <remarks>
    /// Not a positional record, and the properties are get-only rather than <c>init</c>, so <c>with</c>
    /// cannot put an illustration back without its alternative text or drop a label the printed
    /// fallback depends on.
    /// </remarks>
    private DesignSelection(
        string groupCode,
        string groupLabel,
        int groupDisplayOrder,
        string optionCode,
        string optionLabel,
        int optionDisplayOrder,
        int optionVersion,
        string? priceListItemCode,
        Guid? illustrationMediaId,
        string? illustrationAlternativeText)
    {
        GroupCode = groupCode;
        GroupLabel = groupLabel;
        GroupDisplayOrder = groupDisplayOrder;
        OptionCode = optionCode;
        OptionLabel = optionLabel;
        OptionDisplayOrder = optionDisplayOrder;
        OptionVersion = optionVersion;
        PriceListItemCode = priceListItemCode;
        IllustrationMediaId = illustrationMediaId;
        IllustrationAlternativeText = illustrationAlternativeText;
    }

    /// <summary>The design option group's code.</summary>
    public string GroupCode { get; }

    /// <summary>
    /// The group's label as it stood at confirmation. Renaming the group never changes a confirmed job.
    /// </summary>
    public string GroupLabel { get; }

    /// <summary>Where the group sits on the card, as the catalogue ordered it at confirmation.</summary>
    public int GroupDisplayOrder { get; }

    /// <summary>The chosen option's code.</summary>
    public string OptionCode { get; }

    /// <summary>The chosen option's label as it stood at confirmation.</summary>
    public string OptionLabel { get; }

    /// <summary>Where the option sits within its group, as the catalogue ordered it at confirmation.</summary>
    public int OptionDisplayOrder { get; }

    /// <summary>The option version chosen, so a later redraw of the same option is distinguishable.</summary>
    public int OptionVersion { get; }

    /// <summary>The price-list item the option's impact resolved to, where it had one.</summary>
    public string? PriceListItemCode { get; }

    /// <summary>
    /// The illustration, by Media id.
    /// </summary>
    /// <remarks>
    /// An identifier and never a URL or an object key: every object is streamed by an endpoint that
    /// re-authorises the request and logs the access (<c>CLAUDE.md</c> section 4 rule 9).
    /// </remarks>
    public Guid? IllustrationMediaId { get; }

    /// <summary>The illustration's alternative text. Present whenever an illustration is carried.</summary>
    public string? IllustrationAlternativeText { get; }

    /// <summary>
    /// Validates one selection of a snapshot.
    /// </summary>
    /// <remarks>
    /// An illustration without alternative text is
    /// <see cref="OrdersErrors.IllustrationAlternativeTextRequired"/>. Alternative text that arrives
    /// with no illustration is dropped rather than stored, because it describes a picture that is not
    /// there and a card that reads out a description of nothing is worse than one that stays quiet.
    /// </remarks>
    /// <param name="groupCode">The design option group's code.</param>
    /// <param name="groupLabel">The group's label as it stands now.</param>
    /// <param name="groupDisplayOrder">Where the group sits on the card.</param>
    /// <param name="optionCode">The chosen option's code.</param>
    /// <param name="optionLabel">The option's label as it stands now.</param>
    /// <param name="optionDisplayOrder">Where the option sits within its group.</param>
    /// <param name="optionVersion">The option version chosen.</param>
    /// <param name="priceListItemCode">The price-list item the impact resolved to, where there was one.</param>
    /// <param name="illustrationMediaId">The illustration, by Media id.</param>
    /// <param name="illustrationAlternativeText">The illustration's alternative text.</param>
    /// <returns>The selection, or the first failure found.</returns>
    public static Result<DesignSelection> Create(
        string? groupCode,
        string? groupLabel,
        int groupDisplayOrder,
        string? optionCode,
        string? optionLabel,
        int optionDisplayOrder,
        int optionVersion,
        string? priceListItemCode,
        Guid? illustrationMediaId,
        string? illustrationAlternativeText)
    {
        var group = Required(groupCode, "groupCode", MaximumCodeLength);

        if (group.IsFailure)
        {
            return Result.Failure<DesignSelection>(group.Error);
        }

        var groupName = Required(groupLabel, "groupLabel", MaximumLabelLength);

        if (groupName.IsFailure)
        {
            return Result.Failure<DesignSelection>(groupName.Error);
        }

        var option = Required(optionCode, "optionCode", MaximumCodeLength);

        if (option.IsFailure)
        {
            return Result.Failure<DesignSelection>(option.Error);
        }

        var optionName = Required(optionLabel, "optionLabel", MaximumLabelLength);

        if (optionName.IsFailure)
        {
            return Result.Failure<DesignSelection>(optionName.Error);
        }

        // Option versions are numbered from one, so a zero is an int nobody filled in. A revision that
        // cannot say which drawing of the option was agreed cannot be compared against the next one.
        if (optionVersion < 1)
        {
            return Result.Failure<DesignSelection>(OrdersErrors.Required("optionVersion"));
        }

        var priceListItem = Blank(priceListItemCode);

        if (priceListItem is { Length: > MaximumCodeLength })
        {
            return Result.Failure<DesignSelection>(
                OrdersErrors.TooLong("priceListItemCode", MaximumCodeLength));
        }

        // An empty identifier is no illustration rather than a broken one, so the alternative-text rule
        // below asks about a picture that is really there.
        var illustration = illustrationMediaId == Guid.Empty ? null : illustrationMediaId;
        var alternativeText = Blank(illustrationAlternativeText);

        if (illustration is null)
        {
            alternativeText = null;
        }
        else if (alternativeText is null)
        {
            return Result.Failure<DesignSelection>(OrdersErrors.IllustrationAlternativeTextRequired);
        }
        else if (alternativeText.Length > MaximumAlternativeTextLength)
        {
            return Result.Failure<DesignSelection>(
                OrdersErrors.TooLong("illustrationAlternativeText", MaximumAlternativeTextLength));
        }

        return Result.Success(new DesignSelection(
            group.Value,
            groupName.Value,
            groupDisplayOrder,
            option.Value,
            optionName.Value,
            optionDisplayOrder,
            optionVersion,
            priceListItem,
            illustration,
            alternativeText));
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
