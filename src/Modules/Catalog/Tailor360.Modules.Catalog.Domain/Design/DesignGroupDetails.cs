using Tailor360.Modules.Catalog.Domain.Catalogue;
using Tailor360.Platform.Abstractions.Results;

namespace Tailor360.Modules.Catalog.Domain.Design;

/// <summary>Everything an administrator says about a design option group.</summary>
/// <param name="Code">The <c>lower_snake_case</c> code, unique within the category.</param>
/// <param name="Name">The label.</param>
/// <param name="NameTamil">The Tamil label, where confirmed.</param>
/// <param name="SelectionMode">Single or multiple.</param>
/// <param name="Required">Whether a garment may be confirmed with nothing chosen here.</param>
/// <param name="DisplayOrder">Where the group sits in the picker.</param>
/// <param name="ActiveFrom">The first day it is offered, or null for immediately.</param>
/// <param name="ActiveTo">The last day, or null for indefinitely.</param>
/// <param name="BranchIds">The branches that offer it; a subset of the category's, checked at publication.</param>
public sealed record DesignGroupDetails(
    string Code,
    string Name,
    string? NameTamil,
    DesignSelectionMode SelectionMode,
    bool Required,
    int DisplayOrder,
    DateOnly? ActiveFrom,
    DateOnly? ActiveTo,
    IReadOnlyCollection<Guid> BranchIds)
{
    /// <summary>Checks the details that need no other record to check.</summary>
    /// <returns>Success, or the first thing wrong.</returns>
    public Result Validate()
    {
        if (!DesignCode.IsWellFormedGroupCode(Code))
        {
            return Result.Failure(CatalogErrors.GroupCodeNotWellFormed("code"));
        }

        if (string.IsNullOrWhiteSpace(Name))
        {
            return Result.Failure(CatalogErrors.Required("name"));
        }

        if (Name.Length > CategoryDetails.MaximumNameLength)
        {
            return Result.Failure(CatalogErrors.TooLong("name", CategoryDetails.MaximumNameLength));
        }

        if (NameTamil is { Length: > CategoryDetails.MaximumNameLength })
        {
            return Result.Failure(CatalogErrors.TooLong("nameTamil", CategoryDetails.MaximumNameLength));
        }

        if (!Enum.IsDefined(SelectionMode))
        {
            return Result.Failure(CatalogErrors.Required("selectionMode"));
        }

        if (DisplayOrder < 0)
        {
            return Result.Failure(CatalogErrors.DisplayOrderNegative("displayOrder"));
        }

        return ActiveFrom is { } from && ActiveTo is { } to && to < from
            ? Result.Failure(CatalogErrors.ActiveDatesReversed("activeTo"))
            : Result.Success();
    }
}
