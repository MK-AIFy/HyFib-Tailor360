using System.Globalization;
using Tailor360.Modules.Billing.Contracts.Pricing;
using Tailor360.Modules.Billing.Domain;
using Tailor360.Modules.Billing.Domain.Pricing;
using Tailor360.Modules.Billing.Domain.Registrations;
using Tailor360.Modules.Billing.Domain.Tax;
using Tailor360.Platform.Abstractions.Money;
using Tailor360.Platform.Abstractions.Results;

namespace Tailor360.Modules.Billing.Application.Pricing;

/// <summary>
/// The calculation (#147): pure, static and deterministic over a request and the three pieces of
/// configuration it is priced against. It reads no store, no clock and no caller; what it needs is
/// handed to it, which is what lets a stored snapshot be recomputed and compared years later.
/// </summary>
/// <remarks>
/// <para>
/// The arithmetic is <c>docs/architecture/conventions.md</c> section 1.2 (plan D10). Each printed
/// component of a line — the base, each surcharge, the discount, each tax component — is rounded half
/// away from zero to paise exactly once, from the unrounded product that produced it, and everything
/// after that is exact arithmetic on rounded paise: gross is the sum of the rounded base and surcharges,
/// the taxable value is gross less the rounded discount, the line total is the taxable value plus the
/// rounded tax. What is printed therefore adds up. The document totals are sums of those rounded values
/// and are never re-rounded; the document round-off under the version's rule is recorded explicitly,
/// never absorbed.
/// </para>
/// <para>
/// A line is taxed under one code — its base item's — so a surcharge taxed differently is refused
/// rather than averaged in; the place of supply against the registration's state decides the scheme, and
/// a line never carries both (<c>INV-INV-04</c>). Exclusive rates have the tax added to them. Inclusive
/// rates have it backed out: each component is taken from the net inclusive amount and rounded once, and
/// the taxable value is the inclusive amount less those components, so the customer pays the price the
/// list quoted, to the paisa (OD-05). No statutory rate lives here: every rate comes from the tax
/// configuration version, every amount from the price-list version.
/// </para>
/// </remarks>
public static class PricingEngine
{
    private const string CgstKind = "CGST";
    private const string SgstKind = "SGST";
    private const string IgstKind = "IGST";
    private const string CessKind = "CESS";

    /// <summary>The largest quantity a line may carry; beyond it the arithmetic could overflow, and no garment order is near it.</summary>
    public const decimal MaximumQuantity = 1_000_000m;

    /// <summary>Prices a request.</summary>
    /// <param name="request">What to price.</param>
    /// <param name="version">The price-list version to price on; it must cover the branch.</param>
    /// <param name="taxConfiguration">The tax configuration version to take rates of tax from.</param>
    /// <param name="registration">The branch's GST registration in force on the day.</param>
    /// <param name="callerMayOverride">Whether the caller holds <c>billing.override_price</c>.</param>
    /// <param name="now">When the calculation is made; recorded, never used in the arithmetic.</param>
    /// <returns>The result, or the first refusal.</returns>
    public static Result<PricingResult> Calculate(
        PricingRequest request,
        PriceListVersion version,
        TaxConfigurationVersion taxConfiguration,
        GstRegistration registration,
        bool callerMayOverride,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(version);
        ArgumentNullException.ThrowIfNull(taxConfiguration);
        ArgumentNullException.ThrowIfNull(registration);

        if (request.Lines is not { Count: > 0 })
        {
            return Result.Failure<PricingResult>(BillingErrors.LinesRequired);
        }

        if (!Gstin.IsWellFormedStateCode(request.PlaceOfSupplyStateCode))
        {
            return Result.Failure<PricingResult>(BillingErrors.StateCodeNotWellFormed("placeOfSupplyStateCode"));
        }

        if (!version.Covers(request.BranchId))
        {
            return Result.Failure<PricingResult>(BillingErrors.ConfigurationMissing(
                "The price-list version does not price the branch.", "branchId"));
        }

        var scheme = string.Equals(registration.StateCode, request.PlaceOfSupplyStateCode, StringComparison.Ordinal)
            ? SupplyScheme.IntraState
            : SupplyScheme.InterState;

        var keys = new HashSet<string>(StringComparer.Ordinal);
        var lines = new List<PricedLine>(request.Lines.Count);
        foreach (var line in request.Lines)
        {
            if (string.IsNullOrWhiteSpace(line.LineKey))
            {
                return Result.Failure<PricingResult>(BillingErrors.Required("lines[].lineKey"));
            }

            if (!keys.Add(line.LineKey))
            {
                return Result.Failure<PricingResult>(BillingErrors.LineKeyDuplicated(Target(line, "lineKey")));
            }

            var priced = PriceLine(line, version, taxConfiguration, scheme, callerMayOverride);
            if (priced.IsFailure)
            {
                return Result.Failure<PricingResult>(priced.Error);
            }

            lines.Add(priced.Value);
        }

        return Result.Success(new PricingResult(
            version.Id,
            taxConfiguration.Id,
            registration.Id,
            scheme,
            version.TaxInclusive,
            now,
            lines,
            Totals(lines, version.RoundOff)));
    }

    private static Result<PricedLine> PriceLine(
        PricingLineRequest line,
        PriceListVersion version,
        TaxConfigurationVersion taxConfiguration,
        SupplyScheme scheme,
        bool callerMayOverride)
    {
        if (line.Quantity <= 0m || line.Quantity > MaximumQuantity || decimal.Round(line.Quantity, 4) != line.Quantity)
        {
            return Result.Failure<PricedLine>(BillingErrors.QuantityNotPositive(Target(line, "quantity")));
        }

        var item = version.FindItemByCode(line.ItemCode ?? string.Empty);
        if (item is null || !item.Active)
        {
            return Result.Failure<PricedLine>(BillingErrors.ItemNotPriced(line.ItemCode ?? string.Empty, Target(line, "itemCode")));
        }

        var taxCode = taxConfiguration.FindTaxCodeByCode(item.TaxCode);
        if (taxCode is null || !taxCode.Active)
        {
            return Result.Failure<PricedLine>(BillingErrors.ConfigurationMissing(
                $"Item '{item.Code}' names tax code '{item.TaxCode}', which the published tax configuration does not hold.",
                Target(line, "itemCode")));
        }

        // The rate charged: the catalogue's, or the override's — which always needs a reason, and needs the
        // permission once it moves the line further from the catalogue than the version tolerates.
        var catalogueRate = item.BaseRate;
        var appliedRate = catalogueRate;
        var approvalExercised = false;
        if (line.Override is { } @override)
        {
            if (@override.Rate < 0m || decimal.Round(@override.Rate, 4) != @override.Rate)
            {
                return Result.Failure<PricedLine>(BillingErrors.OverrideRateNotWellFormed(Target(line, "override.rate")));
            }

            var reasoned = CheckReason(@override.Reason, Target(line, "override.reason"));
            if (reasoned.IsFailure)
            {
                return Result.Failure<PricedLine>(reasoned.Error);
            }

            appliedRate = @override.Rate;

            // Compared unrounded: the threshold is a fraction of a percent, and the percentage the line
            // reports is rounded for reading, not for judging.
            if (Math.Abs(VariancePercent(catalogueRate, appliedRate, rounded: false)) > version.OverrideThresholdPercent)
            {
                if (!callerMayOverride)
                {
                    return Result.Failure<PricedLine>(BillingErrors.ApprovalRequired with { Target = Target(line, "override.rate") });
                }

                approvalExercised = true;
            }
        }

        // Every component is rounded once, from its own product; from here on the line is exact paise.
        var baseAmount = Rupees(appliedRate * line.Quantity);
        var catalogueBase = Rupees(catalogueRate * line.Quantity);

        var surcharges = new List<PricedSurcharge>();
        var surchargeTotal = Money.Zero;
        foreach (var (code, index) in (line.SurchargeItemCodes ?? []).Select((code, index) => (code, index)))
        {
            var surcharge = version.FindItemByCode(code ?? string.Empty);
            if (surcharge is null || !surcharge.Active)
            {
                return Result.Failure<PricedLine>(BillingErrors.ItemNotPriced(code ?? string.Empty, Target(line, $"surchargeItemCodes[{index}]")));
            }

            if (!string.Equals(surcharge.TaxCode, item.TaxCode, StringComparison.Ordinal))
            {
                return Result.Failure<PricedLine>(BillingErrors.SurchargeTaxedDifferently(code!, Target(line, $"surchargeItemCodes[{index}]")));
            }

            var amount = Rupees(surcharge.BaseRate * line.Quantity);
            surchargeTotal += amount;
            surcharges.Add(new PricedSurcharge(surcharge.Code, surcharge.Description, surcharge.BaseRate, amount));
        }

        var gross = baseAmount + surchargeTotal;

        PricedDiscount? discount = null;
        var discountAmount = Money.Zero;
        if (line.Discount is { } asked)
        {
            var rule = version.FindDiscountRuleByCode(asked.RuleCode ?? string.Empty);
            if (rule is null || !rule.Active)
            {
                return Result.Failure<PricedLine>(BillingErrors.DiscountRuleNotInForce(asked.RuleCode ?? string.Empty, Target(line, "discount.ruleCode")));
            }

            if (asked.Value < 0m || decimal.Round(asked.Value, 4) != asked.Value)
            {
                return Result.Failure<PricedLine>(BillingErrors.AmountNotWellFormed(Target(line, "discount.value")));
            }

            if (asked.Value > rule.Maximum)
            {
                return Result.Failure<PricedLine>(BillingErrors.DiscountAboveMaximum(Target(line, "discount.value")));
            }

            var discountApproved = false;
            if (asked.Value > rule.MaximumWithoutApproval)
            {
                if (!callerMayOverride)
                {
                    return Result.Failure<PricedLine>(BillingErrors.ApprovalRequired with { Target = Target(line, "discount.value") });
                }

                var reasoned = CheckReason(asked.Reason, Target(line, "discount.reason"));
                if (reasoned.IsFailure)
                {
                    return Result.Failure<PricedLine>(reasoned.Error);
                }

                discountApproved = true;
                approvalExercised = true;
            }

            discountAmount = Rupees(rule.Kind == DiscountKind.Percentage ? gross.Amount * asked.Value / 100m : asked.Value);
            if (discountAmount > gross)
            {
                return Result.Failure<PricedLine>(BillingErrors.DiscountExceedsLine(Target(line, "discount.value")));
            }

            discount = new PricedDiscount(rule.Code, rule.Kind.ToString(), asked.Value, discountAmount, discountApproved);
        }

        var net = gross - discountAmount;

        // The scheme's components and their rates, from the code. A nil-rated code has none.
        var components = scheme == SupplyScheme.IntraState
            ? new[] { (CgstKind, taxCode.RateOf(TaxComponentKind.Cgst)), (SgstKind, taxCode.RateOf(TaxComponentKind.Sgst)) }
            : [(IgstKind, taxCode.RateOf(TaxComponentKind.Igst))];
        var cess = taxCode.RateOf(TaxComponentKind.Cess);
        var rates = components.Concat(cess > 0m ? [(CessKind, cess)] : []).Where(component => component.Item2 > 0m).ToArray();
        var totalRate = rates.Sum(component => component.Item2);

        // Exclusive rates are the taxable value as they stand and the tax is added. Inclusive rates carry
        // the tax inside them: each component is taken from the net inclusive amount and rounded once, and
        // the taxable value is what is left, so the line total is the inclusive price to the paisa.
        var taxBase = version.TaxInclusive ? net.Amount / (1m + totalRate / 100m) : net.Amount;
        var taxes = rates
            .Select(component => new PricedTaxComponent(component.Item1, component.Item2, Rupees(taxBase * component.Item2 / 100m)))
            .ToArray();
        var taxTotal = taxes.Aggregate(Money.Zero, (sum, component) => sum + component.Amount);
        var taxableRounded = version.TaxInclusive ? net - taxTotal : net;
        var variance = baseAmount - catalogueBase;

        return Result.Success(new PricedLine(
            line.LineKey,
            item.Code,
            item.Description,
            line.Quantity,
            catalogueRate,
            appliedRate,
            baseAmount,
            surcharges,
            discount,
            gross,
            taxableRounded,
            taxCode.Code,
            taxCode.Classification,
            taxCode.Kind.ToString(),
            taxes,
            taxTotal,
            taxableRounded + taxTotal,
            variance,
            VariancePercent(catalogueRate, appliedRate, rounded: true),
            approvalExercised));
    }

    private static PricedDocumentTotals Totals(IReadOnlyList<PricedLine> lines, RoundOffRule rule)
    {
        var subtotal = Sum(lines, line => line.Gross);
        var discountTotal = Sum(lines, line => line.Discount?.Amount ?? Money.Zero);
        var taxable = Sum(lines, line => line.TaxableValue);
        var central = SumTax(lines, CgstKind);
        var state = SumTax(lines, SgstKind);
        var integrated = SumTax(lines, IgstKind);
        var cess = SumTax(lines, CessKind);

        // The sum of rounded lines is never re-rounded; the round-off is the one explicit step after it.
        var unrounded = taxable + central + state + integrated + cess;
        var grand = rule == RoundOffRule.NearestRupee
            ? Money.Rupees(decimal.Round(unrounded.Amount, 0, MidpointRounding.AwayFromZero))
            : unrounded;

        return new PricedDocumentTotals(subtotal, discountTotal, taxable, central, state, integrated, cess, grand - unrounded, grand);
    }

    private static Money Sum(IEnumerable<PricedLine> lines, Func<PricedLine, Money> amount)
        => lines.Aggregate(Money.Zero, (sum, line) => sum + amount(line));

    private static Money SumTax(IEnumerable<PricedLine> lines, string kind)
        => lines.SelectMany(line => line.Taxes)
            .Where(component => component.Kind == kind)
            .Aggregate(Money.Zero, (sum, component) => sum + component.Amount);

    /// <summary>Half away from zero to paise, exactly once, at the end of the line.</summary>
    private static Money Rupees(decimal amount)
        => Money.Rupees(decimal.Round(amount, Money.DocumentScale, MidpointRounding.AwayFromZero));

    /// <summary>How far the applied rate is from the catalogue's, as a percentage of the catalogue's.</summary>
    private static decimal VariancePercent(decimal catalogueRate, decimal appliedRate, bool rounded)
    {
        if (catalogueRate == appliedRate)
        {
            return 0m;
        }

        // A free-of-charge catalogue item priced at anything is wholly a variance.
        if (catalogueRate == 0m)
        {
            return 100m;
        }

        var percent = (appliedRate - catalogueRate) / catalogueRate * 100m;

        return rounded ? decimal.Round(percent, 2, MidpointRounding.AwayFromZero) : percent;
    }

    /// <summary>A reason as the version checks it, and holding nothing a JSON document cannot carry.</summary>
    private static Result CheckReason(string? reason, string target)
    {
        var checkedReason = PriceListVersion.CheckReason(reason);
        if (checkedReason.IsFailure)
        {
            return Result.Failure(checkedReason.Error with { Target = target });
        }

        return reason!.Any(char.IsControl)
            ? Result.Failure(BillingErrors.ReasonNotWellFormed(target))
            : Result.Success();
    }

    private static string Target(PricingLineRequest line, string field)
        => string.Create(CultureInfo.InvariantCulture, $"lines[{line.LineKey}].{field}");
}
