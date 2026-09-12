using Tailor360.Platform.Abstractions.Money;

namespace Tailor360.Modules.Billing.Domain.Invoicing;

/// <summary>
/// The customer as the invoice names them. Copied at drafting and kept as issued: a tax document names
/// the person it was given to, whatever the customer record later becomes
/// (<c>docs/nfr/data-classification.md</c> section 5.10).
/// </summary>
/// <param name="CustomerNumber">The display number.</param>
/// <param name="DisplayName">The name.</param>
/// <param name="AddressLine">The address, or null when the caller may not read contact details.</param>
/// <param name="Locality">The locality, or null.</param>
/// <param name="Postcode">The postcode, or null.</param>
public sealed record InvoiceCustomer(
    string CustomerNumber,
    string DisplayName,
    string? AddressLine,
    string? Locality,
    string? Postcode)
{
    /// <summary>The longest name or address line kept.</summary>
    public const int MaximumLength = 200;
}

/// <summary>The document totals, in the shape an order stores and an invoice prints.</summary>
public sealed record InvoiceTotals(
    Money Subtotal,
    Money DiscountTotal,
    Money TaxableValue,
    Money CentralTax,
    Money StateTax,
    Money IntegratedTax,
    Money Cess,
    Money RoundOff,
    Money GrandTotal)
{
    private InvoiceTotals()
        : this(Money.Zero, Money.Zero, Money.Zero, Money.Zero, Money.Zero, Money.Zero, Money.Zero, Money.Zero, Money.Zero)
    {
        // The persistence layer materialises instances through this constructor: a complex property
        // cannot be bound through a constructor parameter, so the values are set afterwards.
    }
}

/// <summary>One line as the calculation produced it, ready to be kept on the invoice.</summary>
public sealed record InvoicedLine(
    Guid GarmentJobId,
    string LineKey,
    string ItemCode,
    string Description,
    decimal Quantity,
    decimal CatalogueRate,
    decimal AppliedRate,
    Money Base,
    IReadOnlyList<InvoicedSurcharge> Surcharges,
    string? DiscountRuleCode,
    string? DiscountKind,
    decimal? DiscountValue,
    Money DiscountAmount,
    Money Gross,
    Money TaxableValue,
    string TaxCode,
    string Classification,
    string TaxCodeKind,
    IReadOnlyList<InvoicedTax> Taxes,
    Money TaxTotal,
    Money LineTotal,
    Money Variance);

/// <summary>A surcharge on a line.</summary>
public sealed record InvoicedSurcharge(string ItemCode, string Description, decimal Rate, Money Amount);

/// <summary>A tax component of a line.</summary>
public sealed record InvoicedTax(string Kind, decimal RatePercent, Money Amount);

/// <summary>
/// The configuration a calculation was made on, kept with the invoice (G-10, INV-INV-03) — and the
/// supplier's names as the registration gave them at the time, because the document is rendered later and a
/// registration amended in between must not change what a statutory record says was issued.
/// </summary>
public sealed record InvoiceCalculation(
    string Reference,
    Guid PriceListVersionId,
    Guid TaxConfigurationVersionId,
    Guid GstRegistrationId,
    string Gstin,
    string SupplierStateCode,
    string PlaceOfSupplyStateCode,
    string Scheme,
    bool TaxInclusive,
    string SupplierLegalName,
    string? SupplierTradeName);
