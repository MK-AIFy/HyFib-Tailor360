using Tailor360.Platform.Abstractions.Results;

namespace Tailor360.Modules.Billing.Domain.Pricing;

/// <summary>
/// Everything an administrator says about a discount rule: what kind of discount it permits, how much
/// of it anyone may give, and how much of it needs <c>billing.override_price</c>.
/// </summary>
/// <param name="Code">The stable business key a line's discount names; upper snake case.</param>
/// <param name="Description">What the discount is for.</param>
/// <param name="Kind">A percentage of the line, or an amount off it.</param>
/// <param name="MaximumWithoutApproval">The largest value the counter may give on its own.</param>
/// <param name="Maximum">The largest value anyone may give, with approval.</param>
/// <param name="Active">Whether the rule may be applied to a new line.</param>
public sealed record DiscountRuleDetails(
    string Code,
    string Description,
    DiscountKind Kind,
    decimal MaximumWithoutApproval,
    decimal Maximum,
    bool Active)
{
    /// <summary>The longest description accepted.</summary>
    public const int MaximumDescriptionLength = 200;

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

        if (!IsWellFormed(Kind, MaximumWithoutApproval))
        {
            return Result.Failure(Kind == DiscountKind.Percentage
                ? BillingErrors.RateOutOfRange("maximumWithoutApproval")
                : BillingErrors.AmountNotWellFormed("maximumWithoutApproval"));
        }

        if (!IsWellFormed(Kind, Maximum))
        {
            return Result.Failure(Kind == DiscountKind.Percentage
                ? BillingErrors.RateOutOfRange("maximum")
                : BillingErrors.AmountNotWellFormed("maximum"));
        }

        return MaximumWithoutApproval <= Maximum
            ? Result.Success()
            : Result.Failure(BillingErrors.DiscountBoundsNotOrdered("maximumWithoutApproval"));
    }

    private static bool IsWellFormed(DiscountKind kind, decimal value)
        => kind == DiscountKind.Percentage
            ? value >= 0m && value <= 100m && decimal.Round(value, 3) == value
            : value >= 0m && value <= PriceListItemDetails.MaximumRate && decimal.Round(value, 2) == value;
}
