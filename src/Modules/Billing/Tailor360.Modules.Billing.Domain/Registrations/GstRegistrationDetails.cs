using System.Text.RegularExpressions;
using Tailor360.Platform.Abstractions.Results;

namespace Tailor360.Modules.Billing.Domain.Registrations;

/// <summary>
/// Everything an administrator says about a branch's GST registration.
/// </summary>
/// <param name="BranchId">The branch the registration belongs to. The branch record itself is Identity's.</param>
/// <param name="Gstin">The registration number, as printed on the certificate.</param>
/// <param name="StateCode">The two-digit state code of the place of business; what place of supply is compared against.</param>
/// <param name="LegalName">The registered legal name, as it must appear on an invoice.</param>
/// <param name="TradeName">The trade name, where it differs.</param>
/// <param name="EffectiveFrom">The first day the registration applies to.</param>
/// <param name="EffectiveTo">The last day, or null while it is open-ended.</param>
public sealed partial record GstRegistrationDetails(
    Guid BranchId,
    string Gstin,
    string StateCode,
    string LegalName,
    string? TradeName,
    DateOnly EffectiveFrom,
    DateOnly? EffectiveTo)
{
    /// <summary>The longest name accepted.</summary>
    public const int MaximumNameLength = 200;

    /// <summary>Checks the details that need no other record to check.</summary>
    /// <returns>Success, or the first thing wrong.</returns>
    public Result Validate()
    {
        if (BranchId == Guid.Empty)
        {
            return Result.Failure(BillingErrors.Required("branchId"));
        }

        if (!Registrations.Gstin.IsWellFormed(Gstin))
        {
            return Result.Failure(BillingErrors.GstinNotWellFormed("gstin"));
        }

        if (StateCode is null || !StateCodeShape().IsMatch(StateCode) || StateCode == "00")
        {
            return Result.Failure(BillingErrors.StateCodeNotWellFormed("stateCode"));
        }

        if (!string.Equals(Registrations.Gstin.StateCodeOf(Gstin), StateCode, StringComparison.Ordinal))
        {
            return Result.Failure(BillingErrors.GstinStateMismatch("stateCode"));
        }

        if (string.IsNullOrWhiteSpace(LegalName))
        {
            return Result.Failure(BillingErrors.Required("legalName"));
        }

        if (LegalName.Length > MaximumNameLength)
        {
            return Result.Failure(BillingErrors.TooLong("legalName", MaximumNameLength));
        }

        if (TradeName is { Length: > MaximumNameLength })
        {
            return Result.Failure(BillingErrors.TooLong("tradeName", MaximumNameLength));
        }

        if (EffectiveFrom == default)
        {
            return Result.Failure(BillingErrors.Required("effectiveFrom"));
        }

        return EffectiveTo is { } to && to < EffectiveFrom
            ? Result.Failure(BillingErrors.DatesNotOrdered("effectiveTo"))
            : Result.Success();
    }

    [GeneratedRegex("^[0-9]{2}$")]
    private static partial Regex StateCodeShape();
}
