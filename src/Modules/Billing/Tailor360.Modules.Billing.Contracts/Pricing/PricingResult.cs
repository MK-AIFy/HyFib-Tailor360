using Tailor360.Platform.Abstractions.Money;

namespace Tailor360.Modules.Billing.Contracts.Pricing;

/// <summary>What the engine produced: every line's components and the document totals, and the versions they came from.</summary>
/// <param name="PriceListVersionId">The price-list version the rates came from.</param>
/// <param name="TaxConfigurationVersionId">The tax configuration version the rates of tax came from.</param>
/// <param name="GstRegistrationId">The branch registration the scheme was decided against.</param>
/// <param name="Scheme">Intra-state (CGST and SGST) or inter-state (IGST).</param>
/// <param name="TaxInclusive">Whether the version's rates included tax, which the engine then backed out.</param>
/// <param name="CalculatedAt">When it was calculated.</param>
/// <param name="Lines">The lines, in the order they were asked, each under the caller's key.</param>
/// <param name="Totals">The document totals, in the shape an order and an invoice store.</param>
public sealed record PricingResult(
    Guid PriceListVersionId,
    Guid TaxConfigurationVersionId,
    Guid GstRegistrationId,
    SupplyScheme Scheme,
    bool TaxInclusive,
    DateTimeOffset CalculatedAt,
    IReadOnlyList<PricedLine> Lines,
    PricedDocumentTotals Totals);

/// <summary>Which scheme a supply falls under.</summary>
public enum SupplyScheme
{
    /// <summary>The place of supply is the registration's state: CGST and SGST.</summary>
    IntraState = 0,

    /// <summary>The place of supply is another state: IGST.</summary>
    InterState = 1,
}

/// <summary>One priced line.</summary>
/// <param name="LineKey">The caller's key.</param>
/// <param name="ItemCode">The base item.</param>
/// <param name="Description">The base item's description as the version holds it.</param>
/// <param name="Quantity">How many.</param>
/// <param name="CatalogueRate">The version's rate per unit for the base item.</param>
/// <param name="AppliedRate">The rate actually charged per unit: the catalogue rate, or the override.</param>
/// <param name="Base">The base amount: applied rate by quantity.</param>
/// <param name="Surcharges">Each surcharge item and its amount.</param>
/// <param name="Discount">The discount applied, or null.</param>
/// <param name="Gross">Base plus surcharges, before the discount.</param>
/// <param name="TaxableValue">What tax is charged on; net of tax when the rates included it.</param>
/// <param name="TaxCode">The tax code the line is taxed under.</param>
/// <param name="Classification">The HSN or SAC of that code.</param>
/// <param name="TaxCodeKind">Goods or Services.</param>
/// <param name="Taxes">The tax components with their rates and amounts, under one scheme only.</param>
/// <param name="TaxTotal">The sum of the tax components.</param>
/// <param name="LineTotal">Taxable value plus tax.</param>
/// <param name="Variance">What the override changed the line's base by against the catalogue, zero without one.</param>
/// <param name="VariancePercent">The same as a percentage of the catalogue base, zero without an override.</param>
/// <param name="ApprovalExercised">Whether the caller's <c>billing.override_price</c> was needed and used for this line.</param>
public sealed record PricedLine(
    string LineKey,
    string ItemCode,
    string Description,
    decimal Quantity,
    decimal CatalogueRate,
    decimal AppliedRate,
    Money Base,
    IReadOnlyList<PricedSurcharge> Surcharges,
    PricedDiscount? Discount,
    Money Gross,
    Money TaxableValue,
    string TaxCode,
    string Classification,
    string TaxCodeKind,
    IReadOnlyList<PricedTaxComponent> Taxes,
    Money TaxTotal,
    Money LineTotal,
    Money Variance,
    decimal VariancePercent,
    bool ApprovalExercised);

/// <summary>A surcharge on a line.</summary>
/// <param name="ItemCode">The surcharge item.</param>
/// <param name="Description">Its description.</param>
/// <param name="Rate">Its rate per unit.</param>
/// <param name="Amount">Rate by the line's quantity.</param>
public sealed record PricedSurcharge(string ItemCode, string Description, decimal Rate, Money Amount);

/// <summary>A discount on a line.</summary>
/// <param name="RuleCode">The rule it was given under.</param>
/// <param name="Kind">Percentage or Amount.</param>
/// <param name="Value">The percentage or the amount asked.</param>
/// <param name="Amount">What came off the line.</param>
/// <param name="ApprovalExercised">Whether the value was above what the counter may give on its own.</param>
public sealed record PricedDiscount(string RuleCode, string Kind, decimal Value, Money Amount, bool ApprovalExercised);

/// <summary>One tax component of a line.</summary>
/// <param name="Kind"><c>CGST</c>, <c>SGST</c>, <c>IGST</c> or <c>CESS</c>.</param>
/// <param name="RatePercent">The rate applied.</param>
/// <param name="Amount">The amount, rounded to paise once.</param>
public sealed record PricedTaxComponent(string Kind, decimal RatePercent, Money Amount);

/// <summary>The document totals: sums of the rounded lines, never re-rounded, and the explicit round-off.</summary>
/// <param name="Subtotal">The sum of the lines' gross amounts.</param>
/// <param name="DiscountTotal">The sum of the discounts.</param>
/// <param name="TaxableValue">The sum of the taxable values.</param>
/// <param name="CentralTax">The sum of CGST.</param>
/// <param name="StateTax">The sum of SGST.</param>
/// <param name="IntegratedTax">The sum of IGST.</param>
/// <param name="Cess">The sum of cess.</param>
/// <param name="RoundOff">Grand total less the unrounded sum, under the version's rule; zero when the rule is none.</param>
/// <param name="GrandTotal">What is payable.</param>
public sealed record PricedDocumentTotals(
    Money Subtotal,
    Money DiscountTotal,
    Money TaxableValue,
    Money CentralTax,
    Money StateTax,
    Money IntegratedTax,
    Money Cess,
    Money RoundOff,
    Money GrandTotal);
