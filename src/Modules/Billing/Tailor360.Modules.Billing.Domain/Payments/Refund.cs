using Tailor360.Modules.Billing.Domain.Invoicing;
using Tailor360.Platform.Abstractions.Money;
using Tailor360.Platform.Abstractions.Results;

namespace Tailor360.Modules.Billing.Domain.Payments;

/// <summary>What a refund pays back.</summary>
public enum RefundSource
{
    /// <summary>The part of a payment held as an advance and not applied to any invoice.</summary>
    Advance = 0,

    /// <summary>What an invoice holds beyond what it charges: a credit note's value, or an over-payment.</summary>
    Invoice = 1,
}

/// <summary>
/// Money paid back to the customer (E09-F03-3): a compensating record, never an edit of what it pays back
/// (INV-PAY-01). Paid through a mode allowed for refunds, in the caller's open cashier session so the day
/// still reconciles (INV-CSH-03), against exactly one source — a payment's unapplied advance, or an invoice
/// that holds more than it charges — and never more than that source still holds. Whether an advance is
/// refundable on a cancellation is the Owner's policy (OD-04, OD-05); this is the mechanism.
/// </summary>
public sealed class Refund
{
    /// <summary>The longest reason kept with the row.</summary>
    public const int MaximumReasonLength = Invoice.MaximumReasonLength;

    private Refund()
    {
        // The persistence layer materialises instances through this constructor.
    }

    private Refund(
        Guid id, Guid organisationId, Guid branchId, Guid cashierSessionId, Guid cashierId, Guid customerId, Guid orderId,
        RefundSource source, Guid? paymentId, Guid? invoiceId, string modeCode, Money amount, string? reference, string? clientKey,
        string reason, DateTimeOffset now, Guid? by)
    {
        Id = id;
        OrganisationId = organisationId;
        BranchId = branchId;
        CashierSessionId = cashierSessionId;
        CashierId = cashierId;
        CustomerId = customerId;
        OrderId = orderId;
        Source = source;
        PaymentId = paymentId;
        InvoiceId = invoiceId;
        ModeCode = modeCode;
        Amount = amount;
        Reference = reference;
        ClientKey = clientKey;
        Reason = reason;
        RecordedAt = now;
        RecordedBy = by;
    }

    /// <summary>Identifier.</summary>
    public Guid Id { get; private set; }

    /// <summary>The organisation.</summary>
    public Guid OrganisationId { get; private set; }

    /// <summary>The branch it was paid at.</summary>
    public Guid BranchId { get; private set; }

    /// <summary>The open cashier session it was recorded in.</summary>
    public Guid CashierSessionId { get; private set; }

    /// <summary>The cashier who paid it.</summary>
    public Guid CashierId { get; private set; }

    /// <summary>The customer, the order's.</summary>
    public Guid CustomerId { get; private set; }

    /// <summary>The order the money came from.</summary>
    public Guid OrderId { get; private set; }

    /// <summary>What it pays back.</summary>
    public RefundSource Source { get; private set; }

    /// <summary>The payment whose advance is paid back, where the source is an advance.</summary>
    public Guid? PaymentId { get; private set; }

    /// <summary>The invoice whose surplus is paid back, where the source is an invoice.</summary>
    public Guid? InvoiceId { get; private set; }

    /// <summary>The mode it was paid through.</summary>
    public string ModeCode { get; private set; } = string.Empty;

    /// <summary>How much.</summary>
    public Money Amount { get; private set; }

    /// <summary>The reference the mode required, where it did.</summary>
    public string? Reference { get; private set; }

    /// <summary>The idempotency key the request arrived with, the row's own guard against a twin.</summary>
    public string? ClientKey { get; private set; }

    /// <summary>Why, as the trail records it.</summary>
    public string Reason { get; private set; } = string.Empty;

    /// <summary>When.</summary>
    public DateTimeOffset RecordedAt { get; private set; }

    /// <summary>Who.</summary>
    public Guid? RecordedBy { get; private set; }

    /// <summary>
    /// Records a refund against one source, never more than it still holds. The mode's rules — allowed for
    /// refund, available at the branch, a reference where required — are the caller's to apply first.
    /// </summary>
    /// <param name="refundable">What the source still holds: the advance unapplied and unrefunded, or the invoice's surplus unrefunded.</param>
    public static Result<Refund> Record(
        Guid id, Guid organisationId, Guid branchId, Guid cashierSessionId, Guid cashierId, Guid customerId, Guid orderId,
        RefundSource source, Guid? paymentId, Guid? invoiceId, string modeCode, Money amount, Money refundable,
        string? reference, string? clientKey, string? reason, DateTimeOffset now, Guid? by)
    {
        var valid = Payment.Validate(modeCode, amount, reference);
        if (valid.IsFailure)
        {
            return Result.Failure<Refund>(valid.Error);
        }

        if ((source == RefundSource.Advance) != paymentId.HasValue || (source == RefundSource.Invoice) != invoiceId.HasValue)
        {
            return Result.Failure<Refund>(BillingErrors.RefundSourceNotWellFormed);
        }

        var checkedReason = Invoice.CheckReason(reason);
        if (checkedReason.IsFailure)
        {
            return Result.Failure<Refund>(checkedReason.Error);
        }

        if (refundable.IsNegative || amount > refundable)
        {
            return Result.Failure<Refund>(BillingErrors.RefundExceedsRefundable("amount"));
        }

        var key = clientKey?.Trim();
        if (key is { Length: > Payment.MaximumClientKeyLength })
        {
            key = key[..Payment.MaximumClientKeyLength];
        }

        return Result.Success(new Refund(
            id, organisationId, branchId, cashierSessionId, cashierId, customerId, orderId, source, paymentId, invoiceId,
            modeCode, amount, PaymentReferences.Check(reference, "reference").Value, string.IsNullOrEmpty(key) ? null : key,
            reason!.Trim(), now, by));
    }
}
