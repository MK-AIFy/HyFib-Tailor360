using Tailor360.Modules.Billing.Domain.Invoicing;
using Tailor360.Platform.Abstractions.Money;
using Tailor360.Platform.Abstractions.Results;

namespace Tailor360.Modules.Billing.Domain.Payments;

/// <summary>
/// The receipt that acknowledges one payment: numbered <c>RCPT-&lt;branch&gt;-&lt;FY&gt;-000001</c> from a
/// gapless per-branch sequence drawn inside the payment's own transaction, carrying an opaque <c>R-</c>
/// barcode payload minted at issue, and issued with the payment so the two commit together (INV-PAY-01,
/// the T4 boundary). Append-only: a receipt is never edited or deleted; a reversal or a refund is its own
/// record (E09-F03-3). What it acknowledges is frozen on it — the figures a customer was handed — because
/// the order's balance moves on after the moment of issue.
/// </summary>
public sealed class Receipt
{
    private Receipt()
    {
        // The persistence layer materialises instances through this constructor.
    }

    private Receipt(
        Guid id, Guid organisationId, Guid branchId, Guid paymentId, string receiptNumber, string barcodePayload, string financialYear,
        DateOnly issuedOn, DateTimeOffset issuedAt, Guid? issuedBy, Money amount, Money allocated, Money unappliedAdvance, Money orderOutstanding)
    {
        Id = id;
        OrganisationId = organisationId;
        BranchId = branchId;
        PaymentId = paymentId;
        ReceiptNumber = receiptNumber;
        BarcodePayload = barcodePayload;
        FinancialYear = financialYear;
        IssuedOn = issuedOn;
        IssuedAt = issuedAt;
        IssuedBy = issuedBy;
        Amount = amount;
        Allocated = allocated;
        UnappliedAdvance = unappliedAdvance;
        OrderOutstanding = orderOutstanding;
    }

    /// <summary>Identifier.</summary>
    public Guid Id { get; private set; }

    /// <summary>The organisation.</summary>
    public Guid OrganisationId { get; private set; }

    /// <summary>The branch that issued it: the payment's.</summary>
    public Guid BranchId { get; private set; }

    /// <summary>The payment it acknowledges; one receipt per payment.</summary>
    public Guid PaymentId { get; private set; }

    /// <summary>The display number.</summary>
    public string ReceiptNumber { get; private set; } = string.Empty;

    /// <summary>The opaque <c>R-</c> payload printed on it (conventions.md section 3.3).</summary>
    public string BarcodePayload { get; private set; } = string.Empty;

    /// <summary>The financial year the number was drawn in.</summary>
    public string FinancialYear { get; private set; } = string.Empty;

    /// <summary>The business date of issue, in the branch's calendar.</summary>
    public DateOnly IssuedOn { get; private set; }

    /// <summary>When.</summary>
    public DateTimeOffset IssuedAt { get; private set; }

    /// <summary>Who.</summary>
    public Guid? IssuedBy { get; private set; }

    /// <summary>What was received: the payment's amount.</summary>
    public Money Amount { get; private set; }

    /// <summary>What of it went to invoices at recording.</summary>
    public Money Allocated { get; private set; }

    /// <summary>What of it was held as an advance at recording; allocated plus this is the amount.</summary>
    public Money UnappliedAdvance { get; private set; }

    /// <summary>What the order's posted invoices still owed once the payment was applied, as it stood at issue.</summary>
    public Money OrderOutstanding { get; private set; }

    /// <summary>
    /// Issues the receipt for a payment just recorded and allocated: the number composed from the parts
    /// the caller drew, the payload the caller minted, and the figures frozen from the payment.
    /// </summary>
    /// <param name="id">Identifier.</param>
    /// <param name="payment">The payment, allocated.</param>
    /// <param name="receiptNumber">The number, composed by <see cref="DocumentNumbers.Compose"/>.</param>
    /// <param name="barcodePayload">The <c>R-</c> payload, minted once per command.</param>
    /// <param name="financialYear">The financial-year token the number was drawn in.</param>
    /// <param name="issuedOn">The branch-local date of issue.</param>
    /// <param name="orderOutstanding">What the order's posted invoices still owe once the payment is applied.</param>
    /// <param name="now">When.</param>
    /// <param name="by">Who.</param>
    public static Result<Receipt> Issue(
        Guid id, Payment payment, string receiptNumber, string barcodePayload, string financialYear, DateOnly issuedOn,
        Money orderOutstanding, DateTimeOffset now, Guid? by)
    {
        ArgumentNullException.ThrowIfNull(payment);

        if (string.IsNullOrWhiteSpace(receiptNumber) || receiptNumber.Length > DocumentNumbers.MaximumLength)
        {
            return Result.Failure<Receipt>(BillingErrors.Required("receiptNumber"));
        }

        if (!Tailor360.Platform.Abstractions.Barcodes.BarcodePayload.TryParse(barcodePayload, Tailor360.Platform.Abstractions.Barcodes.BarcodePayload.ReceiptNamespace, out _))
        {
            return Result.Failure<Receipt>(BillingErrors.Required("barcodePayload"));
        }

        if (orderOutstanding.IsNegative)
        {
            return Result.Failure<Receipt>(BillingErrors.AmountNotWellFormed("orderOutstanding"));
        }

        var allocated = payment.Allocated;
        var held = payment.UnappliedAdvance;
        if (allocated + held != payment.Amount)
        {
            // The payment was not allocated at recording, or was allocated twice: the receipt would say a
            // figure the rows do not hold.
            return Result.Failure<Receipt>(BillingErrors.PaymentAlreadyAllocated);
        }

        return Result.Success(new Receipt(
            id, payment.OrganisationId, payment.BranchId, payment.Id, receiptNumber.Trim(), barcodePayload, financialYear,
            issuedOn, now, by, payment.Amount, allocated, held, orderOutstanding));
    }
}
