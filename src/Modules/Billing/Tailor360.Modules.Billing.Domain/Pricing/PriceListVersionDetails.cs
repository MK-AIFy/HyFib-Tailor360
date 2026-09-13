using Tailor360.Platform.Abstractions.Results;

namespace Tailor360.Modules.Billing.Domain.Pricing;

/// <summary>
/// What a price-list version says about itself: when it applies, where, and the two conventions every
/// calculation on it follows.
/// </summary>
/// <param name="Name">What the version is called.</param>
/// <param name="Notes">Why it exists.</param>
/// <param name="EffectiveFrom">The first business day it applies to.</param>
/// <param name="TaxInclusive">Whether the rates include tax (backed out at calculation) or exclude it (added).</param>
/// <param name="RoundOff">How a document total is rounded on this version.</param>
/// <param name="OverrideThresholdPercent">The variance from the catalogue rate above which an override needs <c>billing.override_price</c>.</param>
/// <param name="BranchIds">The branches this version prices for.</param>
public sealed record PriceListVersionDetails(
    string Name,
    string? Notes,
    DateOnly EffectiveFrom,
    bool TaxInclusive,
    RoundOffRule RoundOff,
    decimal OverrideThresholdPercent,
    IReadOnlyCollection<Guid> BranchIds)
{
    /// <summary>The longest name accepted.</summary>
    public const int MaximumNameLength = 120;

    /// <summary>The longest notes accepted.</summary>
    public const int MaximumNotesLength = 1000;

    /// <summary>Checks the details that need no other record to check.</summary>
    /// <returns>Success, or the first thing wrong.</returns>
    public Result Validate()
    {
        if (string.IsNullOrWhiteSpace(Name))
        {
            return Result.Failure(BillingErrors.Required("name"));
        }

        if (Name.Trim().Length > MaximumNameLength)
        {
            return Result.Failure(BillingErrors.TooLong("name", MaximumNameLength));
        }

        if (Notes is { Length: > MaximumNotesLength })
        {
            return Result.Failure(BillingErrors.TooLong("notes", MaximumNotesLength));
        }

        if (EffectiveFrom == default)
        {
            return Result.Failure(BillingErrors.Required("effectiveFrom"));
        }

        if (!Enum.IsDefined(RoundOff))
        {
            return Result.Failure(BillingErrors.Required("roundOff"));
        }

        if (OverrideThresholdPercent < 0m || OverrideThresholdPercent > 100m
            || decimal.Round(OverrideThresholdPercent, 3) != OverrideThresholdPercent)
        {
            return Result.Failure(BillingErrors.RateOutOfRange("overrideThresholdPercent"));
        }

        if (BranchIds is null)
        {
            return Result.Failure(BillingErrors.Required("branchIds"));
        }

        return BranchIds.Any(branch => branch == Guid.Empty)
            ? Result.Failure(BillingErrors.Required("branchIds"))
            : Result.Success();
    }
}
