using System.Globalization;
using Tailor360.Modules.Billing.Domain.Invoicing;

namespace Tailor360.Modules.Billing.Application.Invoicing;

/// <summary>
/// The model a document is rendered from, as the dictionary <c>IPdfRenderer</c> takes: every key the
/// template reads is written here and nowhere else, so the rendering adapter never sees a Billing type
/// (ADR-0012). Dates are the branch's, amounts are the document's to the paisa, and the customer's details
/// are the ones the document carries — no other personal data goes near a rendering.
/// </summary>
public static class DocumentModels
{
    /// <summary>The model of an invoice.</summary>
    /// <param name="invoice">The posted invoice.</param>
    /// <param name="branchName">The issuing branch's display name.</param>
    /// <param name="terms">The terms the business prints, or null.</param>
    public static IReadOnlyDictionary<string, object?> Invoice(Invoice invoice, string branchName, string? terms)
    {
        ArgumentNullException.ThrowIfNull(invoice);

        var model = Common(invoice, branchName, invoice.InvoiceNumber ?? string.Empty, invoice.PostedOn, invoice.PostedAt ?? invoice.UpdatedAt);
        model["kind"] = DocumentKind.Invoice.ToString();
        model["orderNumber"] = invoice.OrderNumber;
        model["barcodePayload"] = invoice.BarcodePayload;
        model["cancelled"] = invoice.IsCancelled;
        model["lines"] = invoice.Lines.OrderBy(line => line.LineNumber).Select(line => (object?)new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["lineNumber"] = line.LineNumber.ToString(CultureInfo.InvariantCulture),
            ["description"] = line.Description,
            ["itemCode"] = line.ItemCode,
            ["classification"] = line.Classification,
            ["quantity"] = line.Quantity.ToString("0.##", CultureInfo.InvariantCulture),
            ["rate"] = line.AppliedRate,
            ["surcharges"] = line.Surcharges.Select(surcharge => (object?)new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["description"] = surcharge.Description,
                ["amount"] = surcharge.Amount.Amount,
            }).ToList(),
            ["discountRuleCode"] = line.DiscountRuleCode,
            ["discountAmount"] = line.DiscountAmount.Amount,
            ["taxableValue"] = line.TaxableValue.Amount,
            ["taxes"] = Taxes(line.Taxes.Select(tax => (tax.Kind, tax.RatePercent, tax.Amount.Amount))),
            ["taxTotal"] = line.TaxTotal.Amount,
            ["lineTotal"] = line.LineTotal.Amount,
        }).ToList();
        model["totals"] = Totals(invoice.Totals);
        ((Dictionary<string, object?>)model["totals"]!)["balanceDue"] = invoice.Totals.GrandTotal.Amount;
        model["terms"] = terms;

        return model;
    }

    /// <summary>The model of a credit or debit note.</summary>
    /// <param name="invoice">The invoice the note is against.</param>
    /// <param name="note">The note.</param>
    /// <param name="branchName">The issuing branch's display name.</param>
    public static IReadOnlyDictionary<string, object?> Note(Invoice invoice, AdjustmentNote note, string branchName)
    {
        ArgumentNullException.ThrowIfNull(invoice);
        ArgumentNullException.ThrowIfNull(note);

        var model = Common(invoice, branchName, note.Number, note.PostedOn, note.PostedAt);
        model["kind"] = (note.Kind == AdjustmentNoteKind.Credit ? DocumentKind.CreditNote : DocumentKind.DebitNote).ToString();
        model["relatedNumber"] = invoice.InvoiceNumber;
        model["reason"] = note.Reason;
        model["financialYear"] = DocumentNumbers.FinancialYearToken(note.PostedOn);
        model["lines"] = note.Lines.OrderBy(line => line.LineNumber).Select(line =>
        {
            var invoiceLine = invoice.Lines.FirstOrDefault(candidate => candidate.GarmentJobId == line.GarmentJobId);
            return (object?)new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["lineNumber"] = line.LineNumber.ToString(CultureInfo.InvariantCulture),
                ["description"] = invoiceLine?.Description ?? string.Empty,
                ["itemCode"] = invoiceLine?.ItemCode ?? string.Empty,
                ["classification"] = invoiceLine?.Classification ?? string.Empty,
                ["quantity"] = string.Empty,
                ["rate"] = null,
                ["surcharges"] = new List<object?>(),
                ["discountRuleCode"] = null,
                ["discountAmount"] = 0m,
                ["taxableValue"] = line.TaxableValue.Amount,
                ["taxes"] = Taxes(line.Taxes.Select(tax => (tax.Kind, tax.RatePercent, tax.Amount.Amount))),
                ["taxTotal"] = line.TaxTotal.Amount,
                ["lineTotal"] = line.LineTotal.Amount,
            };
        }).ToList();
        model["totals"] = Totals(note.Totals);

        return model;
    }

    private static Dictionary<string, object?> Common(Invoice invoice, string branchName, string number, DateOnly? issuedOn, DateTimeOffset renderedAt)
        => new(StringComparer.Ordinal)
        {
            ["number"] = number,
            ["issuedOn"] = issuedOn?.ToString("dd-MM-yyyy", CultureInfo.InvariantCulture) ?? string.Empty,
            ["financialYear"] = invoice.FinancialYear,
            ["renderedAt"] = renderedAt.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture),
            ["currency"] = invoice.Totals.GrandTotal.Currency,
            ["placeOfSupplyStateCode"] = invoice.Calculation.PlaceOfSupplyStateCode,
            ["scheme"] = invoice.Calculation.Scheme,
            ["supplier"] = new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                // As issued: frozen on the invoice with the GSTIN, never the registration as it is today.
                ["legalName"] = invoice.Calculation.SupplierLegalName,
                ["tradeName"] = invoice.Calculation.SupplierTradeName,
                ["gstin"] = invoice.Calculation.Gstin,
                ["stateCode"] = invoice.Calculation.SupplierStateCode,
                ["branchName"] = branchName,
            },
            ["customer"] = new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["customerNumber"] = invoice.Customer.CustomerNumber,
                ["displayName"] = invoice.Customer.DisplayName,
                ["addressLine"] = invoice.Customer.AddressLine,
                ["locality"] = invoice.Customer.Locality,
                ["postcode"] = invoice.Customer.Postcode,
            },
        };

    private static List<object?> Taxes(IEnumerable<(string Kind, decimal RatePercent, decimal Amount)> taxes)
        => taxes.Select(tax => (object?)new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["kind"] = tax.Kind,
            ["ratePercent"] = tax.RatePercent.ToString("0.##", CultureInfo.InvariantCulture),
            ["amount"] = tax.Amount,
        }).ToList();

    private static Dictionary<string, object?> Totals(InvoiceTotals totals)
        => new(StringComparer.Ordinal)
        {
            ["subtotal"] = totals.Subtotal.Amount,
            ["discountTotal"] = totals.DiscountTotal.Amount,
            ["taxableValue"] = totals.TaxableValue.Amount,
            ["centralTax"] = totals.CentralTax.Amount,
            ["stateTax"] = totals.StateTax.Amount,
            ["integratedTax"] = totals.IntegratedTax.Amount,
            ["cess"] = totals.Cess.Amount,
            ["roundOff"] = totals.RoundOff.Amount,
            ["grandTotal"] = totals.GrandTotal.Amount,
        };
}
