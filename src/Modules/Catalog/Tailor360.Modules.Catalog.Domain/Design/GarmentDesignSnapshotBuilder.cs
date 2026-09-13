using Tailor360.Modules.Catalog.Domain.Catalogue;
using Tailor360.Platform.Abstractions.Results;

namespace Tailor360.Modules.Catalog.Domain.Design;

/// <summary>
/// Builds the immutable copy a confirmed garment carries forward, so a job card renders without a
/// catalogue lookup for ever (<c>docs/prd/design-options.md</c> section 7).
/// </summary>
/// <remarks>
/// <para>
/// A pure function over one version and the selections already validated against it: it holds no state,
/// reads no clock, and two calls with the same inputs give the same answer, byte for byte — which is
/// what makes a snapshot built before a republish identical to one built from the same pinned version
/// afterwards. Nothing here decides whether a selection is <em>allowed</em>; that is
/// <see cref="Catalogue.IDesignSelectionValidator"/>'s question, asked before this is ever called.
/// </para>
/// <para>
/// This is the domain half of what the issue describes as <c>GarmentDesignSnapshot.From(…)</c>. The
/// published value object of that name lives in <c>Catalog.Contracts</c>, which may reference only
/// <c>Platform.Abstractions</c> (ARCH-004) and therefore cannot itself take a <see cref="CatalogVersion"/>
/// as a parameter; the result this method returns is what the application layer's
/// <c>GarmentDesignSnapshotMapper</c> turns into that published shape, exactly as
/// <c>DesignSelectionValidator</c> wraps <see cref="DesignRuleEngine"/>.
/// </para>
/// </remarks>
public static class GarmentDesignSnapshotBuilder
{
    /// <summary>Builds the copy for one garment.</summary>
    /// <param name="version">The catalogue version the selections were validated against.</param>
    /// <param name="categoryId">The category the service type belongs to, in that version.</param>
    /// <param name="serviceTypeId">The service type chosen, in that version.</param>
    /// <param name="selections">What was chosen, already validated.</param>
    /// <param name="conditionalNotes">The standing instructions the rules attached, in rule order.</param>
    /// <param name="instructions">Free-text craft instructions, or null.</param>
    /// <returns>The snapshot, or the reason it could not be built.</returns>
    public static Result<GarmentDesignSnapshotResult> From(
        CatalogVersion version,
        Guid categoryId,
        Guid serviceTypeId,
        IReadOnlyList<DesignSelectionInput> selections,
        IReadOnlyList<string> conditionalNotes,
        string? instructions)
    {
        ArgumentNullException.ThrowIfNull(version);
        ArgumentNullException.ThrowIfNull(selections);
        ArgumentNullException.ThrowIfNull(conditionalNotes);

        if (version.Find(categoryId) is not { } category)
        {
            return Result.Failure<GarmentDesignSnapshotResult>(CatalogErrors.CategoryNotFound);
        }

        if (version.FindService(serviceTypeId) is not { } serviceType)
        {
            return Result.Failure<GarmentDesignSnapshotResult>(CatalogErrors.ServiceTypeNotFound);
        }

        var groupsByCode = version.DesignGroupsOf(categoryId).ToDictionary(group => group.Code, StringComparer.Ordinal);
        var built = new List<GarmentDesignSelectionResult>();

        foreach (var selection in selections)
        {
            if (!groupsByCode.TryGetValue(selection.GroupCode, out var group))
            {
                return Result.Failure<GarmentDesignSnapshotResult>(CatalogErrors.DesignGroupNotFound);
            }

            foreach (var code in selection.OptionCodes)
            {
                if (group.FindOptionByCode(code) is not { } option)
                {
                    return Result.Failure<GarmentDesignSnapshotResult>(CatalogErrors.DesignOptionNotFound);
                }

                built.Add(new GarmentDesignSelectionResult(
                    group.Code,
                    group.Name,
                    group.DisplayOrder,
                    option.Code,
                    option.Name,
                    option.DisplayOrder,
                    option.IllustrationKey,
                    option.IllustrationAlt,
                    option.PriceListItemCode,
                    version.VersionNumber));
            }
        }

        // The order the printed card renders them in: by group, then by option within it — never the
        // order the caller happened to list the groups in, which is how two builds of the same
        // selections would otherwise disagree with each other byte for byte.
        var ordered = built
            .OrderBy(selection => selection.GroupDisplayOrder)
            .ThenBy(selection => selection.GroupCode, StringComparer.Ordinal)
            .ThenBy(selection => selection.OptionDisplayOrder)
            .ThenBy(selection => selection.OptionCode, StringComparer.Ordinal)
            .ToList();

        return Result.Success(new GarmentDesignSnapshotResult(
            version.Id,
            version.VersionNumber,
            category.Code,
            category.Name,
            serviceType.Code,
            serviceType.Name,
            ordered,
            [.. conditionalNotes],
            string.IsNullOrWhiteSpace(instructions) ? null : instructions));
    }
}

/// <summary>The design copy a garment carries forward, as this domain builds it.</summary>
/// <param name="CatalogVersionId">Provenance: the version the selections were validated against (INV-ORD-02).</param>
/// <param name="CatalogVersionNumber">The version's number, printed as "the option version" (section 7).</param>
/// <param name="CategoryCode">The category's code, as the catalogue names it.</param>
/// <param name="CategoryLabel">The category's label as it stood when the snapshot was built.</param>
/// <param name="ServiceTypeCode">The service type's code.</param>
/// <param name="ServiceTypeLabel">The service type's label as it stood when the snapshot was built.</param>
/// <param name="Selections">The choices, in the order the card renders them.</param>
/// <param name="ConditionalNotes">The standing instructions the rules attached.</param>
/// <param name="Instructions">Free-text craft instructions, or null.</param>
public sealed record GarmentDesignSnapshotResult(
    Guid CatalogVersionId,
    int CatalogVersionNumber,
    string CategoryCode,
    string CategoryLabel,
    string ServiceTypeCode,
    string ServiceTypeLabel,
    IReadOnlyList<GarmentDesignSelectionResult> Selections,
    IReadOnlyList<string> ConditionalNotes,
    string? Instructions);

/// <summary>One choice inside a <see cref="GarmentDesignSnapshotResult"/>.</summary>
/// <param name="GroupCode">The group's code.</param>
/// <param name="GroupLabel">The group's label as it stood at the time.</param>
/// <param name="GroupDisplayOrder">Where the group sat on the card.</param>
/// <param name="OptionCode">The chosen option's code.</param>
/// <param name="OptionLabel">The option's label as it stood at the time.</param>
/// <param name="OptionDisplayOrder">Where the option sat within its group.</param>
/// <param name="IllustrationKey">The bundled drawing's reference, or null.</param>
/// <param name="IllustrationAlt">The shape in words.</param>
/// <param name="PriceListItemCode">The price-list item the option resolved to, or null.</param>
/// <param name="OptionVersion">The catalogue version number the option was drawn from.</param>
public sealed record GarmentDesignSelectionResult(
    string GroupCode,
    string GroupLabel,
    int GroupDisplayOrder,
    string OptionCode,
    string OptionLabel,
    int OptionDisplayOrder,
    string? IllustrationKey,
    string IllustrationAlt,
    string? PriceListItemCode,
    int OptionVersion);
