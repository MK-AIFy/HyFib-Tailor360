using Tailor360.Modules.Billing.Application.Abstractions;
using Tailor360.Modules.Billing.Domain.Invoicing;
using Tailor360.Platform.Abstractions.Money;

namespace Tailor360.Modules.Billing.Api.Payloads;

/// <summary>An invoice with its lines. Every amount is in <paramref name="Currency"/>, to paise.</summary>
public sealed record InvoicePayload(
    Guid InvoiceId,
    Guid BranchId,
    Guid CustomerId,
    Guid OrderId,
    string OrderNumber,
    string Status,
    int Revision,
    InvoiceCustomerPayload Customer,
    InvoiceCalculationPayload Calculation,
    string Currency,
    IReadOnlyList<InvoiceLinePayload> Lines,
    InvoiceTotalsPayload Totals,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    DateTimeOffset? DiscardedAt,
    string? DiscardReason)
{
    /// <summary>Projects an invoice.</summary>
    public static InvoicePayload From(Invoice invoice)
    {
        ArgumentNullException.ThrowIfNull(invoice);

        return new InvoicePayload(
            invoice.Id,
            invoice.BranchId,
            invoice.CustomerId,
            invoice.OrderId,
            invoice.OrderNumber,
            invoice.Status.ToString(),
            invoice.Revision,
            new InvoiceCustomerPayload(invoice.Customer.CustomerNumber, invoice.Customer.DisplayName, invoice.Customer.AddressLine, invoice.Customer.Locality, invoice.Customer.Postcode),
            new InvoiceCalculationPayload(
                invoice.Calculation.Reference, invoice.Calculation.PriceListVersionId, invoice.Calculation.TaxConfigurationVersionId,
                invoice.Calculation.GstRegistrationId, invoice.Calculation.Gstin, invoice.Calculation.SupplierStateCode,
                invoice.Calculation.PlaceOfSupplyStateCode, invoice.Calculation.Scheme, invoice.Calculation.TaxInclusive),
            invoice.Totals.GrandTotal.Currency,
            [.. invoice.Lines.Select(InvoiceLinePayload.From)],
            InvoiceTotalsPayload.From(invoice.Totals),
            invoice.CreatedAt,
            invoice.UpdatedAt,
            invoice.DiscardedAt,
            invoice.DiscardReason);
    }
}

/// <summary>The customer as the document names them.</summary>
public sealed record InvoiceCustomerPayload(string CustomerNumber, string DisplayName, string? AddressLine, string? Locality, string? Postcode);

/// <summary>The configuration the lines were calculated on.</summary>
public sealed record InvoiceCalculationPayload(
    string Reference,
    Guid PriceListVersionId,
    Guid TaxConfigurationVersionId,
    Guid GstRegistrationId,
    string Gstin,
    string SupplierStateCode,
    string PlaceOfSupplyStateCode,
    string Scheme,
    bool TaxInclusive);

/// <summary>One line.</summary>
public sealed record InvoiceLinePayload(
    int LineNumber,
    Guid GarmentJobId,
    string ItemCode,
    string Description,
    decimal Quantity,
    decimal CatalogueRate,
    decimal AppliedRate,
    decimal Base,
    IReadOnlyList<InvoiceLineSurchargePayload> Surcharges,
    string? DiscountRuleCode,
    string? DiscountKind,
    decimal? DiscountValue,
    decimal DiscountAmount,
    decimal Gross,
    decimal TaxableValue,
    string TaxCode,
    string Classification,
    string TaxCodeKind,
    IReadOnlyList<InvoiceTaxComponentPayload> Taxes,
    decimal TaxTotal,
    decimal LineTotal,
    decimal Variance)
{
    /// <summary>Projects a line.</summary>
    public static InvoiceLinePayload From(InvoiceLine line)
    {
        ArgumentNullException.ThrowIfNull(line);

        return new InvoiceLinePayload(
            line.LineNumber,
            line.GarmentJobId,
            line.ItemCode,
            line.Description,
            line.Quantity,
            line.CatalogueRate,
            line.AppliedRate,
            line.Base.Amount,
            [.. line.Surcharges.Select(surcharge => new InvoiceLineSurchargePayload(surcharge.ItemCode, surcharge.Description, surcharge.Rate, surcharge.Amount.Amount))],
            line.DiscountRuleCode,
            line.DiscountKind,
            line.DiscountValue,
            line.DiscountAmount.Amount,
            line.Gross.Amount,
            line.TaxableValue.Amount,
            line.TaxCode,
            line.Classification,
            line.TaxCodeKind,
            [.. line.Taxes.Select(tax => new InvoiceTaxComponentPayload(tax.Kind, tax.RatePercent, tax.Amount.Amount))],
            line.TaxTotal.Amount,
            line.LineTotal.Amount,
            line.Variance.Amount);
    }
}

/// <summary>A surcharge printed under a line.</summary>
public sealed record InvoiceLineSurchargePayload(string ItemCode, string Description, decimal Rate, decimal Amount);

/// <summary>One tax component of a line.</summary>
public sealed record InvoiceTaxComponentPayload(string Kind, decimal RatePercent, decimal Amount);

/// <summary>The document totals.</summary>
public sealed record InvoiceTotalsPayload(
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
    public static InvoiceTotalsPayload From(InvoiceTotals totals)
    {
        ArgumentNullException.ThrowIfNull(totals);

        return new InvoiceTotalsPayload(
            totals.Subtotal.Amount, totals.DiscountTotal.Amount, totals.TaxableValue.Amount, totals.CentralTax.Amount,
            totals.StateTax.Amount, totals.IntegratedTax.Amount, totals.Cess.Amount, totals.RoundOff.Amount, totals.GrandTotal.Amount);
    }
}

/// <summary>An invoice in a list: what a screen shows before opening it.</summary>
public sealed record InvoiceSummaryPayload(
    Guid InvoiceId,
    Guid CustomerId,
    Guid OrderId,
    string OrderNumber,
    string Status,
    string CustomerDisplayName,
    decimal GrandTotal,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt)
{
    /// <summary>Projects a summary.</summary>
    public static InvoiceSummaryPayload From(Invoice invoice)
    {
        ArgumentNullException.ThrowIfNull(invoice);

        return new InvoiceSummaryPayload(
            invoice.Id, invoice.CustomerId, invoice.OrderId, invoice.OrderNumber, invoice.Status.ToString(),
            invoice.Customer.DisplayName, invoice.Totals.GrandTotal.Amount, invoice.CreatedAt, invoice.UpdatedAt);
    }
}

/// <summary>One page of invoices.</summary>
/// <param name="Invoices">The invoices, newest first.</param>
/// <param name="NextCursor">Where the next page starts, or null at the end.</param>
public sealed record InvoicePagePayload(IReadOnlyList<InvoiceSummaryPayload> Invoices, string? NextCursor)
{
    /// <summary>Projects a page.</summary>
    public static InvoicePagePayload From(InvoicePage page)
    {
        ArgumentNullException.ThrowIfNull(page);

        return new InvoicePagePayload([.. page.Invoices.Select(InvoiceSummaryPayload.From)], page.NextCursor);
    }
}
