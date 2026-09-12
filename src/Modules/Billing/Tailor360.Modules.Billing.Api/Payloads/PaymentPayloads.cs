using Tailor360.Modules.Billing.Contracts.Payments;
using Tailor360.Modules.Billing.Domain.Payments;

namespace Tailor360.Modules.Billing.Api.Payloads;

/// <summary>A payment mode as an administrator sees it.</summary>
/// <param name="Id">Identifier.</param>
/// <param name="Code">The code, fixed for good.</param>
/// <param name="Name">The name on the button.</param>
/// <param name="RequiresReference">Whether a payment must carry an external reference.</param>
/// <param name="RequiresProvider">Whether a payment goes through a provider intent.</param>
/// <param name="AllowedForRefund">Whether a refund may be paid through it.</param>
/// <param name="IsActive">Whether new payments may use it.</param>
/// <param name="BranchIds">The branches it is restricted to; empty means every branch.</param>
/// <param name="UpdatedAt">When it last changed.</param>
public sealed record PaymentModePayload(
    Guid Id,
    string Code,
    string Name,
    bool RequiresReference,
    bool RequiresProvider,
    bool AllowedForRefund,
    bool IsActive,
    IReadOnlyList<Guid> BranchIds,
    DateTimeOffset UpdatedAt)
{
    /// <summary>Projects a mode.</summary>
    public static PaymentModePayload From(PaymentMode mode)
    {
        ArgumentNullException.ThrowIfNull(mode);

        return new PaymentModePayload(
            mode.Id, mode.Code, mode.Name, mode.RequiresReference, mode.RequiresProvider, mode.AllowedForRefund, mode.IsActive,
            mode.Branches.Select(branch => branch.BranchId).OrderBy(id => id).ToList(), mode.UpdatedAt);
    }
}

/// <summary>A cashier session: its float, and once closed, its counts and what they came to.</summary>
/// <param name="Id">Identifier.</param>
/// <param name="BranchId">The branch whose drawer it is.</param>
/// <param name="CashierId">The cashier accountable for it.</param>
/// <param name="Status">Open or Closed.</param>
/// <param name="OpeningFloat">The cash put in the drawer at opening.</param>
/// <param name="OpenedAt">When it opened.</param>
/// <param name="ClosedAt">When it closed; null while open.</param>
/// <param name="ClosedBy">Who closed it; null while open.</param>
/// <param name="ExpectedTotal">Over every mode, what the session should have held; zero while open.</param>
/// <param name="CountedTotal">Over every mode, what was counted; zero while open.</param>
/// <param name="Variance">Counted minus expected; zero while open.</param>
/// <param name="VarianceReason">Why the count differs, where it does.</param>
/// <param name="Currency">The currency of every figure on the session.</param>
/// <param name="Denominations">The count sheet, written at close.</param>
/// <param name="ModeTotals">Expected against counted per mode, written at close.</param>
/// <param name="ReconciliationBatch">The batch opened at close; null until then.</param>
public sealed record CashierSessionPayload(
    Guid Id,
    Guid BranchId,
    Guid CashierId,
    string Status,
    decimal OpeningFloat,
    DateTimeOffset OpenedAt,
    DateTimeOffset? ClosedAt,
    Guid? ClosedBy,
    decimal ExpectedTotal,
    decimal CountedTotal,
    decimal Variance,
    string? VarianceReason,
    string Currency,
    IReadOnlyList<DenominationCountPayload> Denominations,
    IReadOnlyList<ModeTotalPayload> ModeTotals,
    ReconciliationBatchPayload? ReconciliationBatch)
{
    /// <summary>Projects a session.</summary>
    public static CashierSessionPayload From(CashierSession session)
    {
        ArgumentNullException.ThrowIfNull(session);

        return new CashierSessionPayload(
            session.Id, session.BranchId, session.CashierId, session.Status.ToString(),
            session.OpeningFloat.Amount, session.OpenedAt, session.ClosedAt, session.ClosedBy,
            session.ExpectedTotal.Amount, session.CountedTotal.Amount, session.Variance.Amount, session.VarianceReason,
            session.OpeningFloat.Currency,
            session.Counts.OrderByDescending(count => count.Denomination).Select(count => new DenominationCountPayload(count.Denomination, count.Quantity, count.Value)).ToList(),
            session.ModeTotals.OrderBy(total => total.ModeCode, StringComparer.Ordinal).Select(total => new ModeTotalPayload(total.ModeCode, total.Expected, total.Counted, total.Variance)).ToList(),
            session.ReconciliationBatch is { } batch ? ReconciliationBatchPayload.From(batch) : null);
    }
}

/// <summary>What a closed session's reconciliation batch found (INV-CSH-06).</summary>
/// <param name="Id">Identifier.</param>
/// <param name="Status">NotRequired, Pending or Approved.</param>
/// <param name="ExpectedTotal">Over every mode, what the session should have held.</param>
/// <param name="RecordedTotal">Over every mode, what was counted.</param>
/// <param name="Variance">Recorded minus expected.</param>
/// <param name="Currency">The currency of the three figures.</param>
/// <param name="ApprovedBy">Who approved it; null until approved.</param>
/// <param name="ApprovedAt">When it was approved; null until then.</param>
/// <param name="ModeLines">Expected against recorded per mode.</param>
public sealed record ReconciliationBatchPayload(
    Guid Id,
    string Status,
    decimal ExpectedTotal,
    decimal RecordedTotal,
    decimal Variance,
    string Currency,
    Guid? ApprovedBy,
    DateTimeOffset? ApprovedAt,
    IReadOnlyList<ReconciliationBatchModeLinePayload> ModeLines)
{
    /// <summary>Projects a batch.</summary>
    public static ReconciliationBatchPayload From(ReconciliationBatch batch)
    {
        ArgumentNullException.ThrowIfNull(batch);

        return new ReconciliationBatchPayload(
            batch.Id, batch.Status.ToString(), batch.ExpectedTotal.Amount, batch.RecordedTotal.Amount, batch.Variance.Amount,
            batch.ExpectedTotal.Currency, batch.ApprovedBy, batch.ApprovedAt,
            batch.ModeLines.OrderBy(line => line.ModeCode, StringComparer.Ordinal)
                .Select(line => new ReconciliationBatchModeLinePayload(line.ModeCode, line.Expected, line.Recorded, line.Variance)).ToList());
    }
}

/// <summary>One mode's expected against recorded, on a reconciliation batch.</summary>
/// <param name="ModeCode">The payment mode.</param>
/// <param name="Expected">What the close expected in this mode.</param>
/// <param name="Recorded">What the close recorded as counted in this mode.</param>
/// <param name="Variance">Recorded minus expected.</param>
public sealed record ReconciliationBatchModeLinePayload(string ModeCode, decimal Expected, decimal Recorded, decimal Variance);

/// <summary>One line of the count sheet.</summary>
/// <param name="Denomination">The note or coin, in rupees.</param>
/// <param name="Quantity">How many.</param>
/// <param name="Value">What the line is worth.</param>
public sealed record DenominationCountPayload(decimal Denomination, int Quantity, decimal Value);

/// <summary>One mode's expected against counted.</summary>
/// <param name="ModeCode">The payment mode.</param>
/// <param name="Expected">What the session should hold in it.</param>
/// <param name="Counted">What was counted.</param>
/// <param name="Variance">Counted minus expected.</param>
public sealed record ModeTotalPayload(string ModeCode, decimal Expected, decimal Counted, decimal Variance);

/// <summary>A payment mode as the counter sees it: only what is needed to take money in it.</summary>
/// <param name="Id">Identifier.</param>
/// <param name="Code">The code a payment names.</param>
/// <param name="Name">The name on the button.</param>
/// <param name="RequiresReference">Whether the payment must carry the terminal's or the bank's reference.</param>
public sealed record AvailablePaymentModePayload(Guid Id, string Code, string Name, bool RequiresReference)
{
    /// <summary>Projects a mode.</summary>
    public static AvailablePaymentModePayload From(PaymentMode mode)
    {
        ArgumentNullException.ThrowIfNull(mode);

        return new AvailablePaymentModePayload(mode.Id, mode.Code, mode.Name, mode.RequiresReference);
    }
}

/// <summary>A recorded payment, where it went and what of it is still held.</summary>
/// <param name="Id">Identifier.</param>
/// <param name="BranchId">The branch it was taken at.</param>
/// <param name="CashierSessionId">The session it was recorded in.</param>
/// <param name="CashierId">The cashier accountable for it.</param>
/// <param name="CustomerId">The customer, the order's.</param>
/// <param name="OrderId">The order it was taken against.</param>
/// <param name="ModeCode">The payment mode.</param>
/// <param name="Amount">How much.</param>
/// <param name="Currency">The currency of every figure on the payment.</param>
/// <param name="Reference">The external reference, where the mode required one.</param>
/// <param name="Status">Recorded.</param>
/// <param name="RecordedAt">When.</param>
/// <param name="RecordedBy">Who.</param>
/// <param name="Allocated">What has been allocated in all.</param>
/// <param name="UnappliedAdvance">What is still held against the order.</param>
/// <param name="Allocations">Where the money went, in the order it went there.</param>
/// <param name="Advance">The remainder held at recording, or null when every rupee found an invoice.</param>
/// <param name="Receipt">The receipt issued with it.</param>
/// <param name="Reversal">The compensating record that says it never cleared, or null.</param>
/// <param name="RefundedFromAdvance">What has been paid back from its advance.</param>
public sealed record PaymentPayload(
    Guid Id,
    Guid BranchId,
    Guid CashierSessionId,
    Guid CashierId,
    Guid CustomerId,
    Guid OrderId,
    string ModeCode,
    decimal Amount,
    string Currency,
    string? Reference,
    string Status,
    DateTimeOffset RecordedAt,
    Guid? RecordedBy,
    decimal Allocated,
    decimal UnappliedAdvance,
    IReadOnlyList<PaymentAllocationPayload> Allocations,
    AdvancePayload? Advance,
    ReceiptPayload? Receipt,
    PaymentReversalPayload? Reversal,
    decimal RefundedFromAdvance)
{
    /// <summary>Projects a payment and the receipt issued with it.</summary>
    public static PaymentPayload From(Payment payment, Receipt? receipt)
    {
        ArgumentNullException.ThrowIfNull(payment);

        return new PaymentPayload(
            payment.Id, payment.BranchId, payment.CashierSessionId, payment.CashierId, payment.CustomerId, payment.OrderId,
            payment.ModeCode, payment.Amount.Amount, payment.Amount.Currency, payment.Reference, payment.Status.ToString(),
            payment.RecordedAt, payment.RecordedBy, payment.Allocated.Amount, payment.UnappliedAdvance.Amount,
            payment.Allocations.Select(PaymentAllocationPayload.From).ToList(),
            payment.Advance is { } advance ? new AdvancePayload(advance.Id, advance.Amount.Amount, payment.UnappliedAdvance.Amount, advance.ReceivedAt) : null,
            receipt is null ? null : ReceiptPayload.From(receipt),
            payment.Reversal is { } reversal ? new PaymentReversalPayload(reversal.Id, reversal.Reason, reversal.ReversedAt, reversal.ReversedBy) : null,
            payment.RefundedFromAdvance.Amount);
    }
}

/// <summary>The compensating record of a payment that never cleared.</summary>
/// <param name="Id">Identifier.</param>
/// <param name="Reason">Why.</param>
/// <param name="ReversedAt">When.</param>
/// <param name="ReversedBy">Who.</param>
public sealed record PaymentReversalPayload(Guid Id, string Reason, DateTimeOffset ReversedAt, Guid? ReversedBy);

/// <summary>Money paid back to the customer.</summary>
/// <param name="Id">Identifier.</param>
/// <param name="BranchId">The branch it was paid at.</param>
/// <param name="CashierSessionId">The session it was recorded in.</param>
/// <param name="CashierId">The cashier who paid it.</param>
/// <param name="CustomerId">The customer, the order's.</param>
/// <param name="OrderId">The order the money came from.</param>
/// <param name="Source">Advance or Invoice.</param>
/// <param name="PaymentId">The payment whose advance was paid back, where the source is an advance.</param>
/// <param name="InvoiceId">The invoice whose surplus was paid back, where the source is an invoice.</param>
/// <param name="ModeCode">The mode it was paid through.</param>
/// <param name="Amount">How much.</param>
/// <param name="Currency">The currency.</param>
/// <param name="Reference">The reference the mode required, where it did.</param>
/// <param name="Reason">Why.</param>
/// <param name="RecordedAt">When.</param>
/// <param name="RecordedBy">Who.</param>
public sealed record RefundPayload(
    Guid Id,
    Guid BranchId,
    Guid CashierSessionId,
    Guid CashierId,
    Guid CustomerId,
    Guid OrderId,
    string Source,
    Guid? PaymentId,
    Guid? InvoiceId,
    string ModeCode,
    decimal Amount,
    string Currency,
    string? Reference,
    string Reason,
    DateTimeOffset RecordedAt,
    Guid? RecordedBy)
{
    /// <summary>Projects a refund.</summary>
    public static RefundPayload From(Refund refund)
    {
        ArgumentNullException.ThrowIfNull(refund);

        return new RefundPayload(
            refund.Id, refund.BranchId, refund.CashierSessionId, refund.CashierId, refund.CustomerId, refund.OrderId, refund.Source.ToString(),
            refund.PaymentId, refund.InvoiceId, refund.ModeCode, refund.Amount.Amount, refund.Amount.Currency, refund.Reference, refund.Reason,
            refund.RecordedAt, refund.RecordedBy);
    }
}

/// <summary>The receipt that acknowledges a payment: its number, its barcode and the figures frozen on it at issue.</summary>
/// <param name="Id">Identifier.</param>
/// <param name="PaymentId">The payment it acknowledges.</param>
/// <param name="BranchId">The branch that issued it.</param>
/// <param name="ReceiptNumber">The display number.</param>
/// <param name="BarcodePayload">The opaque R- payload printed on it.</param>
/// <param name="FinancialYear">The financial year the number was drawn in.</param>
/// <param name="IssuedOn">The business date of issue, in the branch's calendar.</param>
/// <param name="IssuedAt">When.</param>
/// <param name="Amount">What was received.</param>
/// <param name="Allocated">What of it went to invoices at recording.</param>
/// <param name="UnappliedAdvance">What of it was held at recording.</param>
/// <param name="OrderOutstanding">What the order's posted invoices still owed once the payment was applied, as at issue.</param>
/// <param name="Currency">The currency of every figure.</param>
public sealed record ReceiptPayload(
    Guid Id,
    Guid PaymentId,
    Guid BranchId,
    string ReceiptNumber,
    string BarcodePayload,
    string FinancialYear,
    DateOnly IssuedOn,
    DateTimeOffset IssuedAt,
    decimal Amount,
    decimal Allocated,
    decimal UnappliedAdvance,
    decimal OrderOutstanding,
    string Currency)
{
    /// <summary>Projects a receipt.</summary>
    public static ReceiptPayload From(Receipt receipt)
    {
        ArgumentNullException.ThrowIfNull(receipt);

        return new ReceiptPayload(
            receipt.Id, receipt.PaymentId, receipt.BranchId, receipt.ReceiptNumber, receipt.BarcodePayload, receipt.FinancialYear,
            receipt.IssuedOn, receipt.IssuedAt, receipt.Amount.Amount, receipt.Allocated.Amount, receipt.UnappliedAdvance.Amount,
            receipt.OrderOutstanding.Amount, receipt.Amount.Currency);
    }
}

/// <summary>Money from a payment applied to an invoice.</summary>
/// <param name="Id">Identifier.</param>
/// <param name="InvoiceId">The invoice.</param>
/// <param name="AdvanceId">The advance it was applied from, where the money was held first.</param>
/// <param name="Amount">How much.</param>
/// <param name="Kind">Automatic, AdvanceApplied or Manual.</param>
/// <param name="AllocatedAt">When.</param>
/// <param name="AllocatedBy">Who; null for the rule applying an advance when an invoice posted.</param>
public sealed record PaymentAllocationPayload(Guid Id, Guid InvoiceId, Guid? AdvanceId, decimal Amount, string Kind, DateTimeOffset AllocatedAt, Guid? AllocatedBy)
{
    /// <summary>Projects an allocation.</summary>
    public static PaymentAllocationPayload From(PaymentAllocation allocation)
    {
        ArgumentNullException.ThrowIfNull(allocation);

        return new PaymentAllocationPayload(allocation.Id, allocation.InvoiceId, allocation.AdvanceId, allocation.Amount.Amount, allocation.Kind.ToString(), allocation.AllocatedAt, allocation.AllocatedBy);
    }
}

/// <summary>The part of a payment held against the order until an invoice posts.</summary>
/// <param name="Id">Identifier.</param>
/// <param name="Amount">How much was held.</param>
/// <param name="Unapplied">How much is still held.</param>
/// <param name="ReceivedAt">When.</param>
public sealed record AdvancePayload(Guid Id, decimal Amount, decimal Unapplied, DateTimeOffset ReceivedAt);

/// <summary>Where an order stands across its posted invoices, as the counter reads it.</summary>
/// <param name="OrderId">The order.</param>
/// <param name="Charges">The posted invoices' grand totals.</param>
/// <param name="Credits">Their credit notes.</param>
/// <param name="Debits">Their debit notes.</param>
/// <param name="Allocated">What has been allocated to them.</param>
/// <param name="Refunds">What has been refunded against them.</param>
/// <param name="UnappliedAdvances">What is held against the order and not yet applied.</param>
/// <param name="Outstanding">What the invoices still owe in all; never below zero.</param>
/// <param name="Currency">The currency of every figure.</param>
/// <param name="Invoices">Each posted invoice's own balance, oldest first.</param>
public sealed record OrderBalancePayload(
    Guid OrderId,
    decimal Charges,
    decimal Credits,
    decimal Debits,
    decimal Allocated,
    decimal Refunds,
    decimal UnappliedAdvances,
    decimal Outstanding,
    string Currency,
    IReadOnlyList<InvoiceBalancePayload> Invoices)
{
    /// <summary>Projects the contract's answer.</summary>
    public static OrderBalancePayload From(OrderBalanceSummary summary)
    {
        ArgumentNullException.ThrowIfNull(summary);

        return new OrderBalancePayload(
            summary.OrderId, summary.Charges, summary.Credits, summary.Debits, summary.Allocated, summary.Refunds, summary.UnappliedAdvances, summary.Outstanding, summary.Currency,
            summary.Invoices.Select(InvoiceBalancePayload.From).ToList());
    }
}

/// <summary>Where one posted invoice stands.</summary>
/// <param name="InvoiceId">The invoice.</param>
/// <param name="InvoiceNumber">Its display number.</param>
/// <param name="Charges">The grand total as posted.</param>
/// <param name="Credits">The credit notes against it, the cancellation's included.</param>
/// <param name="Debits">The debit notes against it.</param>
/// <param name="Allocated">The payments and advances applied to it.</param>
/// <param name="Refunds">The refunds against it.</param>
/// <param name="Outstanding">What it still owes; never below zero.</param>
/// <param name="Currency">The currency of every figure.</param>
/// <param name="Status">Unpaid, PartlyPaid, Paid or Cancelled.</param>
public sealed record InvoiceBalancePayload(
    Guid InvoiceId,
    string InvoiceNumber,
    decimal Charges,
    decimal Credits,
    decimal Debits,
    decimal Allocated,
    decimal Refunds,
    decimal Outstanding,
    string Currency,
    string Status)
{
    /// <summary>Projects the contract's answer.</summary>
    public static InvoiceBalancePayload From(InvoiceBalanceSummary summary)
    {
        ArgumentNullException.ThrowIfNull(summary);

        return new InvoiceBalancePayload(
            summary.InvoiceId, summary.InvoiceNumber, summary.Charges, summary.Credits, summary.Debits, summary.Allocated, summary.Refunds, summary.Outstanding, summary.Currency, summary.Status);
    }
}
