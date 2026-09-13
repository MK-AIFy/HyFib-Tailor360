using Tailor360.Modules.Billing.Contracts.Pricing;
using Tailor360.Platform.Abstractions.Money;

namespace Tailor360.Modules.Billing.Api.Payloads;

/// <summary>Price a request against a named price-list version, without storing the result.</summary>
/// <param name="PriceListVersionId">The version to price on; a draft is the point of a preview.</param>
/// <param name="TaxConfigurationVersionId">The tax configuration version to take rates from, or null for the published one.</param>
/// <param name="BranchId">The branch the work would be taken at; the version must price it.</param>
/// <param name="On">The branch-local date, deciding which registration is in force.</param>
/// <param name="PlaceOfSupplyStateCode">The two-digit state code of the place of supply.</param>
/// <param name="Lines">The lines.</param>
public sealed record PricingPreviewRequest(
    Guid? PriceListVersionId,
    Guid? TaxConfigurationVersionId,
    Guid? BranchId,
    DateOnly? On,
    string? PlaceOfSupplyStateCode,
    IReadOnlyList<PricingLineRequestPayload?>? Lines)
{
    /// <summary>The request as the contract reads it; an omitted value becomes one the engine refuses.</summary>
    public PricingRequest ToRequest(Guid organisationId)
        => new(
            organisationId,
            BranchId ?? Guid.Empty,
            On ?? default,
            PlaceOfSupplyStateCode ?? string.Empty,
            null,
            [.. (Lines ?? []).Select(line => line?.ToLine() ?? new PricingLineRequest(string.Empty, string.Empty, 0m, [], null, null))]);
}

/// <summary>One line to price.</summary>
public sealed record PricingLineRequestPayload(
    string? LineKey,
    string? ItemCode,
    decimal? Quantity,
    IReadOnlyList<string?>? SurchargeItemCodes,
    PricingDiscountRequestPayload? Discount,
    PricingOverrideRequestPayload? Override)
{
    /// <summary>The line as the contract reads it.</summary>
    public PricingLineRequest ToLine()
        => new(
            LineKey ?? string.Empty,
            ItemCode ?? string.Empty,
            Quantity ?? 0m,
            [.. (SurchargeItemCodes ?? []).Select(code => code ?? string.Empty)],
            Discount is null ? null : new PricingDiscountRequest(Discount.RuleCode ?? string.Empty, Discount.Value ?? -1m, Discount.Reason),
            Override is null ? null : new PricingOverrideRequest(Override.Rate ?? -1m, Override.Reason ?? string.Empty));
}

/// <summary>A discount on a line.</summary>
public sealed record PricingDiscountRequestPayload(string? RuleCode, decimal? Value, string? Reason);

/// <summary>A rate in place of the catalogue rate.</summary>
public sealed record PricingOverrideRequestPayload(decimal? Rate, string? Reason);

/// <summary>What the engine produced. Every amount is in <paramref name="Currency"/>, to paise.</summary>
public sealed record PricingResultPayload(
    Guid PriceListVersionId,
    Guid TaxConfigurationVersionId,
    Guid GstRegistrationId,
    string Scheme,
    bool TaxInclusive,
    string Currency,
    DateTimeOffset CalculatedAt,
    IReadOnlyList<PricedLinePayload> Lines,
    PricedDocumentTotalsPayload Totals)
{
    /// <summary>Projects a result.</summary>
    public static PricingResultPayload From(PricingResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        return new PricingResultPayload(
            result.PriceListVersionId,
            result.TaxConfigurationVersionId,
            result.GstRegistrationId,
            result.Scheme.ToString(),
            result.TaxInclusive,
            Money.IndianRupee,
            result.CalculatedAt,
            [.. result.Lines.Select(PricedLinePayload.From)],
            PricedDocumentTotalsPayload.From(result.Totals));
    }
}

/// <summary>One priced line.</summary>
public sealed record PricedLinePayload(
    string LineKey,
    string ItemCode,
    string Description,
    decimal Quantity,
    decimal CatalogueRate,
    decimal AppliedRate,
    decimal Base,
    IReadOnlyList<PricedSurchargePayload> Surcharges,
    PricedDiscountPayload? Discount,
    decimal Gross,
    decimal TaxableValue,
    string TaxCode,
    string Classification,
    string TaxCodeKind,
    IReadOnlyList<PricedTaxComponentPayload> Taxes,
    decimal TaxTotal,
    decimal LineTotal,
    decimal Variance,
    decimal VariancePercent,
    bool ApprovalExercised)
{
    /// <summary>Projects a line.</summary>
    public static PricedLinePayload From(PricedLine line)
    {
        ArgumentNullException.ThrowIfNull(line);

        return new PricedLinePayload(
            line.LineKey,
            line.ItemCode,
            line.Description,
            line.Quantity,
            line.CatalogueRate,
            line.AppliedRate,
            line.Base.Amount,
            [.. line.Surcharges.Select(surcharge => new PricedSurchargePayload(surcharge.ItemCode, surcharge.Description, surcharge.Rate, surcharge.Amount.Amount))],
            line.Discount is null ? null : new PricedDiscountPayload(line.Discount.RuleCode, line.Discount.Kind, line.Discount.Value, line.Discount.Amount.Amount, line.Discount.ApprovalExercised),
            line.Gross.Amount,
            line.TaxableValue.Amount,
            line.TaxCode,
            line.Classification,
            line.TaxCodeKind,
            [.. line.Taxes.Select(component => new PricedTaxComponentPayload(component.Kind, component.RatePercent, component.Amount.Amount))],
            line.TaxTotal.Amount,
            line.LineTotal.Amount,
            line.Variance.Amount,
            line.VariancePercent,
            line.ApprovalExercised);
    }
}

/// <summary>A surcharge on a line.</summary>
public sealed record PricedSurchargePayload(string ItemCode, string Description, decimal Rate, decimal Amount);

/// <summary>A discount on a line.</summary>
public sealed record PricedDiscountPayload(string RuleCode, string Kind, decimal Value, decimal Amount, bool ApprovalExercised);

/// <summary>One tax component of a line.</summary>
public sealed record PricedTaxComponentPayload(string Kind, decimal RatePercent, decimal Amount);

/// <summary>The document totals.</summary>
public sealed record PricedDocumentTotalsPayload(
    decimal Subtotal,
    decimal DiscountTotal,
    decimal TaxableValue,
    decimal CentralTax,
    decimal StateTax,
    decimal IntegratedTax,
    decimal Cess,
    decimal RoundOff,
    decimal GrandTotal)
{
    /// <summary>Projects the totals.</summary>
    public static PricedDocumentTotalsPayload From(PricedDocumentTotals totals)
    {
        ArgumentNullException.ThrowIfNull(totals);

        return new PricedDocumentTotalsPayload(
            totals.Subtotal.Amount,
            totals.DiscountTotal.Amount,
            totals.TaxableValue.Amount,
            totals.CentralTax.Amount,
            totals.StateTax.Amount,
            totals.IntegratedTax.Amount,
            totals.Cess.Amount,
            totals.RoundOff.Amount,
            totals.GrandTotal.Amount);
    }
}
