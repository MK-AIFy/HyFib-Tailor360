using Tailor360.Modules.Catalog.Domain.Catalogue;
using Tailor360.Platform.Abstractions.Results;

namespace Tailor360.Modules.Catalog.Domain.Design;

/// <summary>Everything an administrator says about one design option.</summary>
/// <param name="Code">The <c>UPPER_SNAKE_CASE</c> code, unique within the group; <c>NONE</c> is reserved and allowed.</param>
/// <param name="Name">The label.</param>
/// <param name="NameTamil">The Tamil label, where confirmed.</param>
/// <param name="HelpText">One sentence saying what the choice means for the garment. Required.</param>
/// <param name="IllustrationKey">The bundled drawing, <c>sheet#group.OPTION</c>, or null until one exists.</param>
/// <param name="IllustrationAlt">The shape in words. Required, for the screen reader and the monochrome print.</param>
/// <param name="PriceListItemCode">The price-list item the option resolves to, or null when it costs nothing extra.</param>
/// <param name="TimeImpactDays">Signed working days added to the service's expected duration.</param>
/// <param name="DisplayOrder">Where the option sits in its group.</param>
/// <param name="Active">Whether the option is offered; a retired option stays known for ever.</param>
public sealed record DesignOptionDetails(
    string Code,
    string Name,
    string? NameTamil,
    string HelpText,
    string? IllustrationKey,
    string IllustrationAlt,
    string? PriceListItemCode,
    int TimeImpactDays,
    int DisplayOrder,
    bool Active)
{
    /// <summary>The longest help text or alt text accepted.</summary>
    public const int MaximumTextLength = 500;

    /// <summary>The largest day impact accepted either way, the service duration's own ceiling.</summary>
    public const int MaximumTimeImpactDays = ServiceTypeDetails.MaximumExpectedDurationDays;

    /// <summary>Checks the details that need no other record to check.</summary>
    /// <returns>Success, or the first thing wrong.</returns>
    public Result Validate()
    {
        if (!DesignCode.IsWellFormedOptionCode(Code))
        {
            return Result.Failure(CatalogErrors.OptionCodeNotWellFormed("code"));
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

        if (string.IsNullOrWhiteSpace(HelpText))
        {
            return Result.Failure(CatalogErrors.Required("helpText"));
        }

        if (HelpText.Length > MaximumTextLength)
        {
            return Result.Failure(CatalogErrors.TooLong("helpText", MaximumTextLength));
        }

        if (string.IsNullOrWhiteSpace(IllustrationAlt))
        {
            return Result.Failure(CatalogErrors.Required("illustrationAlt"));
        }

        if (IllustrationAlt.Length > MaximumTextLength)
        {
            return Result.Failure(CatalogErrors.TooLong("illustrationAlt", MaximumTextLength));
        }

        if (IllustrationKey is not null && !DesignCode.IsWellFormedIllustrationKey(IllustrationKey))
        {
            return Result.Failure(CatalogErrors.IllustrationKeyNotWellFormed("illustrationKey"));
        }

        if (PriceListItemCode is { Length: > ServiceTypeDetails.MaximumPriceListItemCodeLength })
        {
            return Result.Failure(CatalogErrors.TooLong(
                "priceListItemCode", ServiceTypeDetails.MaximumPriceListItemCodeLength));
        }

        if (Math.Abs(TimeImpactDays) > MaximumTimeImpactDays)
        {
            return Result.Failure(CatalogErrors.TimeImpactOutOfRange("timeImpactDays", MaximumTimeImpactDays));
        }

        return DisplayOrder < 0
            ? Result.Failure(CatalogErrors.DisplayOrderNegative("displayOrder"))
            : Result.Success();
    }
}
