using Tailor360.Modules.Billing.Domain.Invoicing;

namespace Tailor360.Modules.Billing.Api.Payloads;

/// <summary>What an <c>I-</c> barcode payload resolved to: the invoice, by identifier and number, at the caller's branch.</summary>
/// <param name="InvoiceId">The invoice.</param>
/// <param name="InvoiceNumber">Its display number.</param>
/// <param name="BranchId">The branch that issued it — the caller's.</param>
/// <param name="CustomerId">The customer, by identifier only.</param>
/// <param name="OrderId">The order it charges for.</param>
/// <param name="Status">Draft, Posted or Discarded.</param>
/// <param name="Cancelled">Whether a cancellation has been appended.</param>
/// <param name="GrandTotal">What the invoice charges.</param>
/// <param name="Currency">The currency.</param>
public sealed record BarcodeResolutionPayload(
    Guid InvoiceId,
    string InvoiceNumber,
    Guid BranchId,
    Guid CustomerId,
    Guid OrderId,
    string Status,
    bool Cancelled,
    decimal GrandTotal,
    string Currency)
{
    /// <summary>Projects the invoice a payload resolved to.</summary>
    public static BarcodeResolutionPayload From(Invoice invoice)
    {
        ArgumentNullException.ThrowIfNull(invoice);

        return new BarcodeResolutionPayload(
            invoice.Id, invoice.InvoiceNumber ?? string.Empty, invoice.BranchId, invoice.CustomerId, invoice.OrderId, invoice.Status.ToString(),
            invoice.IsCancelled, invoice.Totals.GrandTotal.Amount, invoice.Totals.GrandTotal.Currency);
    }
}

/// <summary>Send a rendered invoice to the branch's print queue.</summary>
/// <param name="Copies">How many copies, one to five; one when omitted.</param>
public sealed record PrintInvoiceRequest(int? Copies);

/// <summary>The print job the queue acknowledged.</summary>
/// <param name="PrintJobId">The job's identifier.</param>
public sealed record PrintJobPayload(Guid PrintJobId);
