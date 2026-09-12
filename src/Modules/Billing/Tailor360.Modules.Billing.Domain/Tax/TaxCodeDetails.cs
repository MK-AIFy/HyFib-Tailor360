using System.Text.RegularExpressions;
using Tailor360.Platform.Abstractions.Results;

namespace Tailor360.Modules.Billing.Domain.Tax;

/// <summary>
/// Everything an administrator says about a tax code.
/// </summary>
/// <remarks>
/// <para>
/// No statutory rate lives in code. What a component's rate is, whether CGST and SGST are equal
/// halves of IGST, whether a cess applies — the accountant enters all of it, and the publish checks
/// say only what would make a calculation contradict itself. This record checks the shape of what was
/// entered; the arithmetic between components is the version's to check at publication, so that a
/// half-entered code can be saved and finished later.
/// </para>
/// </remarks>
/// <param name="Code">The stable business key, upper snake case, unique in the version.</param>
/// <param name="Description">What the code covers, for the administrator and the invoice line.</param>
/// <param name="Classification">The HSN code (goods) or SAC code (services), digits only.</param>
/// <param name="Kind">Goods or services.</param>
/// <param name="Active">Whether the code may be given to a price-list item. A retired code stays readable.</param>
/// <param name="Rates">The components and their rates. Empty is nil-rated or exempt.</param>
public sealed partial record TaxCodeDetails(
    string Code,
    string Description,
    string Classification,
    TaxCodeKind Kind,
    bool Active,
    IReadOnlyList<TaxRate> Rates)
{
    /// <summary>The longest description accepted.</summary>
    public const int MaximumDescriptionLength = 200;

    /// <summary>The fewest digits an HSN or SAC code carries.</summary>
    public const int MinimumClassificationLength = 4;

    /// <summary>The most digits an HSN or SAC code carries.</summary>
    public const int MaximumClassificationLength = 8;

    /// <summary>The most decimal places a rate carries (<c>numeric(6,3)</c>).</summary>
    public const int RateScale = 3;

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

        if (Classification is null || !ClassificationShape().IsMatch(Classification))
        {
            return Result.Failure(BillingErrors.ClassificationNotWellFormed("classification"));
        }

        if (!Enum.IsDefined(Kind))
        {
            return Result.Failure(BillingErrors.Required("kind"));
        }

        if (Rates is null)
        {
            return Result.Failure(BillingErrors.Required("rates"));
        }

        var seen = new HashSet<TaxComponentKind>();
        foreach (var (rate, index) in Rates.Select((rate, index) => (rate, index)))
        {
            if (rate is null || !Enum.IsDefined(rate.Kind))
            {
                return Result.Failure(BillingErrors.ComponentNotWellFormed($"rates[{index}].kind"));
            }

            if (!seen.Add(rate.Kind))
            {
                return Result.Failure(BillingErrors.ComponentDuplicated($"rates[{index}].kind"));
            }

            if (!IsWellFormedRate(rate.RatePercent))
            {
                return Result.Failure(BillingErrors.RateOutOfRange($"rates[{index}].ratePercent"));
            }
        }

        return Result.Success();
    }

    /// <summary>Whether a rate is a percentage of at most three decimal places between 0 and 100.</summary>
    /// <param name="ratePercent">The candidate.</param>
    /// <returns>True when it is.</returns>
    public static bool IsWellFormedRate(decimal ratePercent)
        => ratePercent >= 0m
           && ratePercent <= 100m
           && decimal.Round(ratePercent, RateScale) == ratePercent;

    /// <summary>The rate of one component, or zero when the code does not carry it.</summary>
    /// <param name="kind">The component.</param>
    /// <returns>The rate as a percentage.</returns>
    public decimal RateOf(TaxComponentKind kind)
        => Rates.FirstOrDefault(rate => rate.Kind == kind)?.RatePercent ?? 0m;

    [GeneratedRegex("^[0-9]{4,8}$")]
    private static partial Regex ClassificationShape();
}
