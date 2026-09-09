using Tailor360.Platform.Abstractions.Results;

namespace Tailor360.Modules.Catalog.Domain.Catalogue;

/// <summary>
/// Everything an administrator says about a category, validated as one value.
/// </summary>
/// <remarks>
/// Grouped rather than passed as eight parameters so that the same validation runs whether the
/// category is being added or edited, and so that adding a field is one change rather than four.
/// </remarks>
/// <param name="Code">The machine key. Immutable once its version is published.</param>
/// <param name="Name">The label staff and customers read (en-IN).</param>
/// <param name="NameTamil">The Tamil label, where one is confirmed.</param>
/// <param name="Description">What the category covers, shown at intake and on the job card.</param>
/// <param name="DisplayOrder">Where the category sits among its siblings.</param>
/// <param name="ActiveFrom">The first day the category is offered, or null for always.</param>
/// <param name="ActiveTo">The last day, or null for indefinitely.</param>
/// <param name="FeatureFlagKey">
/// The flag that can switch the category off in an emergency or pilot it at one branch, or null when
/// the category is not behind one. An unknown flag evaluates to off, which is the safe default and is
/// why a mistyped key hides a category rather than exposing one.
/// </param>
/// <param name="BranchIds">The branches that offer it. Empty means nowhere, not everywhere.</param>
public sealed record CategoryDetails(
    string Code,
    string Name,
    string? NameTamil,
    string? Description,
    int DisplayOrder,
    DateOnly? ActiveFrom,
    DateOnly? ActiveTo,
    string? FeatureFlagKey,
    IReadOnlyCollection<Guid> BranchIds)
{
    /// <summary>The longest label the column holds.</summary>
    public const int MaximumNameLength = 120;

    /// <summary>The longest description the column holds.</summary>
    public const int MaximumDescriptionLength = 1000;

    /// <summary>The longest feature-flag key the column holds.</summary>
    public const int MaximumFeatureFlagKeyLength = 120;

    /// <summary>Checks everything that can be checked about a category on its own.</summary>
    /// <remarks>
    /// What is <em>not</em> checked here is everything that depends on the rest of the version: that
    /// the code is unique, that the parent exists, that the hierarchy does not cycle and that the
    /// branches are a subset of the parent's. Those belong to the version and to publish-time
    /// validation, because a single category cannot see them.
    /// </remarks>
    /// <returns>Success, or the first failure.</returns>
    public Result Validate()
    {
        if (!CatalogCode.IsWellFormed(Code))
        {
            return Result.Failure(CatalogErrors.CodeNotWellFormed("code"));
        }

        if (string.IsNullOrWhiteSpace(Name))
        {
            return Result.Failure(CatalogErrors.Required("name"));
        }

        if (Name.Length > MaximumNameLength)
        {
            return Result.Failure(CatalogErrors.TooLong("name", MaximumNameLength));
        }

        if (NameTamil is { Length: > MaximumNameLength })
        {
            return Result.Failure(CatalogErrors.TooLong("nameTamil", MaximumNameLength));
        }

        if (Description is { Length: > MaximumDescriptionLength })
        {
            return Result.Failure(CatalogErrors.TooLong("description", MaximumDescriptionLength));
        }

        if (FeatureFlagKey is { Length: > MaximumFeatureFlagKeyLength })
        {
            return Result.Failure(
                CatalogErrors.TooLong("featureFlagKey", MaximumFeatureFlagKeyLength));
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
