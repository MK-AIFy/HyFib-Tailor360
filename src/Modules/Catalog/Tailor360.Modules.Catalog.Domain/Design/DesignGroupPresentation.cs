using Tailor360.Modules.Catalog.Domain.Catalogue;
using Tailor360.Platform.Abstractions.Results;

namespace Tailor360.Modules.Catalog.Domain.Design;

/// <summary>The fields of a published group that may be corrected in place: what people read, and where.</summary>
public sealed record DesignGroupPresentation(string Name, string? NameTamil, int DisplayOrder)
{
    /// <summary>Checks the correction.</summary>
    /// <returns>Success, or the first thing wrong.</returns>
    public Result Validate()
    {
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

        return DisplayOrder < 0
            ? Result.Failure(CatalogErrors.DisplayOrderNegative("displayOrder"))
            : Result.Success();
    }
}

/// <summary>The fields of a published option that may be corrected in place.</summary>
/// <remarks>
/// The help text and the alt text are words for people, like the label, and a clearer sentence
/// changes nothing that was priced or worked to. The illustration reference is not here: the drawing
/// the customer was shown is part of what they agreed to.
/// </remarks>
public sealed record DesignOptionPresentation(
    string Name,
    string? NameTamil,
    string HelpText,
    string IllustrationAlt,
    int DisplayOrder)
{
    /// <summary>Checks the correction.</summary>
    /// <returns>Success, or the first thing wrong.</returns>
    public Result Validate()
    {
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

        if (string.IsNullOrWhiteSpace(HelpText))
        {
            return Result.Failure(CatalogErrors.Required("helpText"));
        }

        if (HelpText.Length > DesignOptionDetails.MaximumTextLength)
        {
            return Result.Failure(CatalogErrors.TooLong("helpText", DesignOptionDetails.MaximumTextLength));
        }

        if (string.IsNullOrWhiteSpace(IllustrationAlt))
        {
            return Result.Failure(CatalogErrors.Required("illustrationAlt"));
        }

        if (IllustrationAlt.Length > DesignOptionDetails.MaximumTextLength)
        {
            return Result.Failure(
                CatalogErrors.TooLong("illustrationAlt", DesignOptionDetails.MaximumTextLength));
        }

        return DisplayOrder < 0
            ? Result.Failure(CatalogErrors.DisplayOrderNegative("displayOrder"))
            : Result.Success();
    }
}
