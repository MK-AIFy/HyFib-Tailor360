using Tailor360.Modules.Catalog.Contracts.Catalogue;
using Tailor360.Modules.Catalog.Domain.Design;

namespace Tailor360.Modules.Catalog.Application.Catalogue;

/// <summary>
/// Turns the domain's <see cref="GarmentDesignSnapshotResult"/> into the published
/// <see cref="GarmentDesignSnapshot"/> (#30, issue #140).
/// </summary>
/// <remarks>
/// The seam <c>GarmentDesignSnapshotBuilder</c>'s own remarks describe: the builder is pure domain and
/// cannot reference <c>Catalog.Contracts</c> (ARCH-001), so the translation into the shape another module
/// is allowed to see happens here, exactly as this handler translates <c>DesignEvaluationResult</c> into
/// the published <c>DesignEvaluation</c>.
/// </remarks>
internal static class GarmentDesignSnapshotMapper
{
    /// <summary>Maps one built snapshot.</summary>
    /// <param name="result">The domain result.</param>
    /// <returns>The published shape.</returns>
    public static GarmentDesignSnapshot ToContract(GarmentDesignSnapshotResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        return new GarmentDesignSnapshot(
            result.CatalogVersionId,
            result.CatalogVersionNumber,
            result.CategoryCode,
            result.CategoryLabel,
            result.ServiceTypeCode,
            result.ServiceTypeLabel,
            [.. result.Selections.Select(ToContract)],
            result.ConditionalNotes,
            result.Instructions);
    }

    private static GarmentDesignSelectionSnapshot ToContract(GarmentDesignSelectionResult selection)
        => new(
            selection.GroupCode,
            selection.GroupLabel,
            selection.GroupDisplayOrder,
            selection.OptionCode,
            selection.OptionLabel,
            selection.OptionDisplayOrder,
            selection.IllustrationKey,
            selection.IllustrationAlt,
            selection.PriceListItemCode,
            selection.OptionVersion);
}
