using Tailor360.Platform.Abstractions.Results;

namespace Tailor360.Modules.Catalog.Domain.Catalogue;

/// <summary>
/// The fields that may be corrected in place on a published version.
/// </summary>
/// <remarks>
/// <para>
/// <c>docs/prd/category-hierarchy.md</c> section 7 allows exactly this much editing of a published
/// version: label, Tamil label, description and display order, with a reason and an audit entry.
/// Everything else is immutable, and the reason the list is a type rather than four parameters is that
/// the boundary between "presentation" and "behaviour" is the whole safety argument — a caller cannot
/// slip a code or an active date into a correction, because there is nowhere to put one.
/// </para>
/// <para>
/// Nothing downstream reads any of these. A price list, a report, an export and an event payload all
/// refer to the code, so correcting a label changes what is shown and nothing else. That is what makes
/// an in-place correction safe on a version that confirmed orders are pinned to.
/// </para>
/// </remarks>
/// <param name="Name">The label (en-IN).</param>
/// <param name="NameTamil">The Tamil label, where one is confirmed.</param>
/// <param name="Description">What the category or service covers.</param>
/// <param name="DisplayOrder">Where it sits among its siblings.</param>
public sealed record CatalogPresentation(
    string Name,
    string? NameTamil,
    string? Description,
    int DisplayOrder)
{
    /// <summary>Checks the corrected values.</summary>
    /// <returns>Success, or the first failure.</returns>
    public Result Validate()
    {
        if (string.IsNullOrWhiteSpace(Name))
        {
            return Result.Failure(CatalogErrors.Required("name"));
        }

        if (Name.Length > CategoryDetails.MaximumNameLength)
        {
            return Result.Failure(
                CatalogErrors.TooLong("name", CategoryDetails.MaximumNameLength));
        }

        if (NameTamil is { Length: > CategoryDetails.MaximumNameLength })
        {
            return Result.Failure(
                CatalogErrors.TooLong("nameTamil", CategoryDetails.MaximumNameLength));
        }

        if (Description is { Length: > CategoryDetails.MaximumDescriptionLength })
        {
            return Result.Failure(
                CatalogErrors.TooLong("description", CategoryDetails.MaximumDescriptionLength));
        }

        return DisplayOrder < 0
            ? Result.Failure(CatalogErrors.DisplayOrderNegative("displayOrder"))
            : Result.Success();
    }
}
