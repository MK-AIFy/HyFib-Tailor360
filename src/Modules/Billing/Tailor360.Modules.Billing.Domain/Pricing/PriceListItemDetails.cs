using System.Text.RegularExpressions;
using Tailor360.Platform.Abstractions.Results;

namespace Tailor360.Modules.Billing.Domain.Pricing;

/// <summary>
/// Everything an administrator says about a price-list item.
/// </summary>
/// <param name="Code">The stable business key the catalogue's <c>price_list_item_code</c> names; upper snake case.</param>
/// <param name="Description">What is charged for, as the estimate and invoice line read it.</param>
/// <param name="Kind">A service's base charge, a surcharge, or a material.</param>
/// <param name="BaseRate">The rate per unit, to four decimal places, before tax when the version is exclusive and including it when inclusive.</param>
/// <param name="Unit">What one of it is: <c>each</c>, <c>metre</c>, <c>hour</c>.</param>
/// <param name="TaxCode">The tax code of the published tax configuration that classifies it.</param>
/// <param name="Active">Whether the item may be priced. A retired item stays readable.</param>
public sealed partial record PriceListItemDetails(
    string Code,
    string Description,
    PriceItemKind Kind,
    decimal BaseRate,
    string Unit,
    string TaxCode,
    bool Active)
{
    /// <summary>The longest description accepted.</summary>
    public const int MaximumDescriptionLength = 200;

    /// <summary>The longest unit accepted.</summary>
    public const int MaximumUnitLength = 20;

    /// <summary>The most decimal places a rate carries (<c>numeric(18,4)</c>).</summary>
    public const int RateScale = 4;

    /// <summary>The largest rate the column holds.</summary>
    public const decimal MaximumRate = 99_999_999_999_999m;

    /// <summary>Checks the details that need no other record to check.</summary>
    /// <returns>Success, or the first thing wrong.</returns>
    public Result Validate()
    {
        if (!BillingCode.IsWellFormed(Code))
        {
            return Result.Failure(BillingErrors.CodeNotWellFormed("code"));
        }

        if (string.IsNullOrWhiteSpace(Description))
        {
            return Result.Failure(BillingErrors.Required("description"));
        }

        if (Description.Trim().Length > MaximumDescriptionLength)
        {
            return Result.Failure(BillingErrors.TooLong("description", MaximumDescriptionLength));
        }

        if (!Enum.IsDefined(Kind))
        {
            return Result.Failure(BillingErrors.Required("kind"));
        }

        if (!IsWellFormedRate(BaseRate))
        {
            return Result.Failure(BillingErrors.RateNotWellFormed("baseRate"));
        }

        if (Unit is null || !UnitShape().IsMatch(Unit))
        {
            return Result.Failure(BillingErrors.UnitNotWellFormed("unit"));
        }

        return BillingCode.IsWellFormed(TaxCode)
            ? Result.Success()
            : Result.Failure(BillingErrors.CodeNotWellFormed("taxCode"));
    }

    /// <summary>Whether a rate is a non-negative amount of at most four decimal places that the column holds.</summary>
    /// <param name="rate">The candidate.</param>
    /// <returns>True when it is.</returns>
    public static bool IsWellFormedRate(decimal rate)
        => rate >= 0m && rate <= MaximumRate && decimal.Round(rate, RateScale) == rate;

    [GeneratedRegex("^[a-z][a-z0-9_]{0,19}$")]
    private static partial Regex UnitShape();
}
