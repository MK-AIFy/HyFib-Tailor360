using Tailor360.Modules.Billing.Application.Abstractions;
using Tailor360.Modules.Billing.Contracts.Events;
using Tailor360.Modules.Billing.Domain;
using Tailor360.Modules.Billing.Domain.Invoicing;
using Tailor360.Modules.Billing.Domain.Payments;
using Tailor360.Platform.Abstractions.Identifiers;
using Tailor360.Platform.Abstractions.Money;
using Tailor360.Platform.Abstractions.Results;
using Tailor360.Platform.Abstractions.Time;

namespace Tailor360.Modules.Billing.Application.Payments;

/// <summary>
/// The compensating records (E09-F03-3): a reversal for a payment that never cleared, a refund for money
/// paid back. Neither edits what it compensates (INV-PAY-01); both are serialised on the order's invoice
/// rows and the payment's row, so the balance they change is recomputed from what is held (INV-PAY-03,
/// INV-PAY-06), and both go on the outbox with the paid-status change they cause.
/// </summary>
/// <param name="payments">The store.</param>
/// <param name="invoices">The invoices whose balances move.</param>
/// <param name="orders">What Billing knows about the order.</param>
/// <param name="sessions">The cashier's open session, for a refund.</param>
/// <param name="modes">The modes a refund may be paid through.</param>
/// <param name="events">Billing's outbox.</param>
/// <param name="audit">The audit trail.</param>
/// <param name="clock">The clock.</param>
/// <param name="ids">The identifier generator.</param>
public sealed class RefundHandler(
    IPaymentStore payments,
    IInvoiceStore invoices,
    IOrderFactStore orders,
    ICashierSessionStore sessions,
    IPaymentModeStore modes,
    IBillingEventPublisher events,
    IBillingAuditWriter audit,
    IClock clock,
    IIdGenerator ids)
{
    /// <summary>A payment was reversed; the matrix's action for <c>payments.reverse</c>.</summary>
    public const string ReversedAction = "payments.reverse";

    /// <summary>A refund was recorded; the matrix's action for <c>payments.refund</c>.</summary>
    public const string RefundedAction = "payments.refund";

    /// <summary>
    /// Reverses a payment recorded in error: a new row, the original untouched, its allocations and its
    /// advance released, the invoices' paid status recomputed from what remains. Once per payment, and
    /// never for a payment money has been paid back from.
    /// </summary>
    public async Task<Result<Payment>> ReverseAsync(ReversePaymentCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var payment = await payments.FindAsync(command.PaymentId, command.OrganisationId, cancellationToken);
        if (payment is null)
        {
            return Result.Failure<Payment>(BillingErrors.PaymentNotFound);
        }

        var reason = Invoice.CheckReason(command.Reason);
        if (reason.IsFailure)
        {
            return Result.Failure<Payment>(reason.Error);
        }

        if (payment.IsReversed)
        {
            return Result.Failure<Payment>(BillingErrors.PaymentAlreadyReversed);
        }

        var reversalId = ids.NewId();
        var reversed = await payments.InAllocationTransactionAsync(
            payment.OrderId,
            async token =>
            {
                // Session then payment — the same order `RefundAsync` takes below — so a reversal of this
                // payment and a refund sourced from it can never each hold the row the other wants next.
                // `CashierSessionId` is read from the row fetched before this transaction opened: it is
                // set once at recording and never moves (INV-PAY-01), so a stale read of it is not a stale
                // read of anything that changes.
                //
                // The lock itself is serialised against the session's own close (INV-CSH-03): the close's
                // `TakenByModeAsync` reads this payment as reversed or not, and the two must not race, or
                // a close that reads this payment as still active could commit a total the reversal —
                // landing either just before or just after — makes wrong the moment it commits.
                await sessions.LockForPaymentAsync(payment.CashierSessionId, token);
                await payments.LockPaymentAsync(command.PaymentId, token);
                var fresh = await payments.FindAsync(command.PaymentId, command.OrganisationId, token);
                if (fresh is null)
                {
                    return Result.Failure<Payment>(BillingErrors.PaymentNotFound);
                }

                // The invoices the payment's allocations name, as they stand with the payment counted.
                var touched = (await invoices.ListPostedForOrderAsync(fresh.OrderId, command.OrganisationId, token))
                    .Where(invoice => fresh.Allocations.Any(allocation => allocation.InvoiceId == invoice.Id))
                    .ToList();
                var before = await BalancesAsync(touched, token);

                var now = clock.UtcNow;
                var reversal = PaymentReversal.Of(reversalId, fresh, command.Reason, now, command.By);
                if (reversal.IsFailure)
                {
                    return Result.Failure<Payment>(reversal.Error);
                }

                payments.AddReversal(reversal.Value);
                events.Publish(new PaymentReversed(
                    ids.NewId(), now, fresh.Id, fresh.OrganisationId, fresh.BranchId, reversal.Value.Id, fresh.OrderId, fresh.Amount.Amount, fresh.Amount.Currency));

                // With the payment's allocations released, each invoice it paid stands where the other rows leave it.
                foreach (var (invoice, was) in before)
                {
                    var released = fresh.Allocations.Where(allocation => allocation.InvoiceId == invoice.Id).Aggregate(Money.Zero, (sum, allocation) => sum + allocation.Amount);
                    PublishStatusIfMoved(invoice, was, was.Allocated - released, was.Refunds, now);
                }

                // Staged before the save so the entry rides the same SaveChangesAsync as the reversal it
                // describes, and the two commit or roll back together (issue #179); no amount in the
                // summary. `fresh` carries no reversal until the row is read back — AddReversal does not
                // set the payment's own navigation property — so the "after" snapshot says so explicitly
                // rather than reading a change that has not reached memory yet.
                await BillingAudit.StageAsync(
                    audit, ReversedAction, BillingAudit.PaymentEntity, fresh.Id,
                    $"Payment in {payment.ModeCode} reversed; {payment.Allocations.Count} allocation(s) released.",
                    command.Reason!.Trim(), PaymentSnapshot.Of(payment), PaymentSnapshot.Of(fresh, reversed: true), token);

                var saved = await payments.SaveAsync(token);
                return saved.IsFailure ? Result.Failure<Payment>(saved.Error) : Result.Success(fresh);
            },
            async token => (await payments.FindAsync(command.PaymentId, command.OrganisationId, token))?.Reversal?.Id == reversalId,
            cancellationToken);
        if (reversed.IsFailure)
        {
            return reversed;
        }

        // The reversed payment, read again so the reversal rides on it in the response.
        var after = await payments.FindAsync(command.PaymentId, command.OrganisationId, cancellationToken);
        return Result.Success(after ?? reversed.Value);
    }

    /// <summary>
    /// Pays money back to the customer in the caller's open session, through a mode allowed for refunds,
    /// against one source: a payment's advance not applied and not yet paid back, or an invoice that holds
    /// more than it charges. Never more than the source still holds, and never through a mode that goes
    /// through a provider (OD-03).
    /// </summary>
    public async Task<Result<Refund>> RefundAsync(RecordRefundCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var modeCode = command.ModeCode?.Trim() ?? string.Empty;
        var amount = Money.Rupees(command.Amount);
        var valid = Payment.Validate(modeCode, amount, command.Reference);
        if (valid.IsFailure)
        {
            return Result.Failure<Refund>(valid.Error);
        }

        var reason = Invoice.CheckReason(command.Reason);
        if (reason.IsFailure)
        {
            return Result.Failure<Refund>(reason.Error);
        }

        if (command.PaymentId.HasValue == command.InvoiceId.HasValue)
        {
            return Result.Failure<Refund>(BillingErrors.RefundSourceNotWellFormed);
        }

        var checkedMode = await CheckModeAsync(modeCode, command.BranchId, command.OrganisationId, command.Reference, cancellationToken);
        if (checkedMode.IsFailure)
        {
            return Result.Failure<Refund>(checkedMode.Error);
        }

        // The source names the order; the order names the customer and the branch the money belongs to.
        var source = await SourceAsync(command, cancellationToken);
        if (source.IsFailure)
        {
            return Result.Failure<Refund>(source.Error);
        }

        var (orderId, customerId, sourceBranchId) = source.Value;
        if (sourceBranchId != command.BranchId)
        {
            return Result.Failure<Refund>(BillingErrors.OrderAtAnotherBranch);
        }

        var session = await sessions.FindOpenAsync(command.BranchId, command.CashierId, command.OrganisationId, cancellationToken);
        if (session is null)
        {
            return Result.Failure<Refund>(BillingErrors.CashierSessionRequired);
        }

        var refundId = ids.NewId();
        var recorded = await payments.InAllocationTransactionAsync(
            orderId,
            async token =>
            {
                await sessions.LockForPaymentAsync(session.Id, token);
                var held = await sessions.FindAsync(session.Id, command.OrganisationId, token);
                if (held is null || !held.IsOpen)
                {
                    return Result.Failure<Refund>(BillingErrors.CashierSessionRequired);
                }

                var now = clock.UtcNow;
                Result<Refund> refund;
                Invoice? invoice = null;
                InvoiceBalance? before = null;
                if (command.PaymentId is { } paymentId)
                {
                    await payments.LockPaymentAsync(paymentId, token);
                    var payment = await payments.FindAsync(paymentId, command.OrganisationId, token);
                    if (payment is null)
                    {
                        return Result.Failure<Refund>(BillingErrors.PaymentNotFound);
                    }

                    refund = Refund.Record(
                        refundId, command.OrganisationId, command.BranchId, held.Id, command.CashierId, customerId, orderId,
                        RefundSource.Advance, paymentId, null, modeCode, amount, payment.UnappliedAdvance, command.Reference, command.ClientKey, command.Reason, now, command.By);
                }
                else
                {
                    invoice = await invoices.FindAsync(command.InvoiceId!.Value, command.OrganisationId, token);
                    if (invoice is null || !invoice.IsPosted)
                    {
                        return Result.Failure<Refund>(BillingErrors.InvoiceNotFound);
                    }

                    before = (await BalancesAsync([invoice], token))[0].Before;
                    refund = Refund.Record(
                        refundId, command.OrganisationId, command.BranchId, held.Id, command.CashierId, customerId, orderId,
                        RefundSource.Invoice, null, invoice.Id, modeCode, amount, before.Refundable, command.Reference, command.ClientKey, command.Reason, now, command.By);
                }

                if (refund.IsFailure)
                {
                    return refund;
                }

                payments.AddRefund(refund.Value);
                events.Publish(new RefundRecorded(
                    ids.NewId(), now, refund.Value.Id, refund.Value.OrganisationId, refund.Value.BranchId, refund.Value.CashierSessionId,
                    refund.Value.CustomerId, refund.Value.OrderId, refund.Value.Source.ToString(), refund.Value.PaymentId, refund.Value.InvoiceId,
                    refund.Value.ModeCode, refund.Value.Amount.Amount, refund.Value.Amount.Currency));
                if (invoice is not null && before is not null)
                {
                    PublishStatusIfMoved(invoice, before, before.Allocated, before.Refunds + amount, now);
                }

                // Staged before the save so the entry rides the same SaveChangesAsync as the refund it
                // describes, and the two commit or roll back together (issue #179). No amount in the
                // summary: the trail is read by more people than the drawer is.
                await BillingAudit.StageAsync(
                    audit, RefundedAction, BillingAudit.RefundEntity, refund.Value.Id,
                    $"Refund paid in {refund.Value.ModeCode} from {(refund.Value.Source == RefundSource.Advance ? "an advance" : "an invoice's surplus")}.",
                    command.Reason!.Trim(), null, RefundSnapshot.Of(refund.Value), token);

                var saved = await payments.SaveAsync(token);
                return saved.IsFailure ? Result.Failure<Refund>(saved.Error) : refund;
            },
            async token => await payments.FindRefundAsync(refundId, command.OrganisationId, token) is not null,
            cancellationToken);

        return recorded;
    }

    private async Task<Result<(Guid OrderId, Guid CustomerId, Guid BranchId)>> SourceAsync(RecordRefundCommand command, CancellationToken cancellationToken)
    {
        if (command.PaymentId is { } paymentId)
        {
            var payment = await payments.FindAsync(paymentId, command.OrganisationId, cancellationToken);
            return payment is null
                ? Result.Failure<(Guid, Guid, Guid)>(BillingErrors.PaymentNotFound)
                : Result.Success((payment.OrderId, payment.CustomerId, payment.BranchId));
        }

        var invoice = await invoices.FindAsync(command.InvoiceId!.Value, command.OrganisationId, cancellationToken);
        if (invoice is null || !invoice.IsPosted)
        {
            return Result.Failure<(Guid, Guid, Guid)>(BillingErrors.InvoiceNotFound);
        }

        var order = await orders.FindAsync(invoice.OrderId, command.OrganisationId, cancellationToken);
        return Result.Success((invoice.OrderId, order?.CustomerId ?? invoice.CustomerId, invoice.BranchId));
    }

    private async Task<Result> CheckModeAsync(string modeCode, Guid branchId, Guid organisationId, string? reference, CancellationToken cancellationToken)
    {
        var mode = (await modes.ListAsync(organisationId, cancellationToken)).FirstOrDefault(candidate => string.Equals(candidate.Code, modeCode, StringComparison.Ordinal));
        if (mode is null)
        {
            return Result.Failure(BillingErrors.PaymentModeNotKnown("modeCode"));
        }

        if (!mode.IsActive || !mode.IsAvailableAt(branchId))
        {
            return Result.Failure(BillingErrors.PaymentModeNotAvailable("modeCode"));
        }

        if (!mode.AllowedForRefund)
        {
            return Result.Failure(BillingErrors.PaymentModeNotForRefund("modeCode"));
        }

        if (mode.RequiresProvider)
        {
            return Result.Failure(BillingErrors.PaymentProviderNotConfigured("modeCode"));
        }

        return mode.RequiresReference && string.IsNullOrWhiteSpace(reference)
            ? Result.Failure(BillingErrors.ReferenceRequired("reference"))
            : Result.Success();
    }

    private async Task<List<(Invoice Invoice, InvoiceBalance Before)>> BalancesAsync(IReadOnlyList<Invoice> posted, CancellationToken cancellationToken)
    {
        var ids = posted.Select(invoice => invoice.Id).ToList();
        var allocated = await payments.AllocatedByInvoiceAsync(ids, cancellationToken);
        var refunded = await payments.RefundedByInvoiceAsync(ids, cancellationToken);
        return posted.Select(invoice => (invoice, InvoiceBalance.Of(invoice, allocated.GetValueOrDefault(invoice.Id, Money.Zero), refunded.GetValueOrDefault(invoice.Id, Money.Zero)))).ToList();
    }

    private void PublishStatusIfMoved(Invoice invoice, InvoiceBalance before, Money allocatedNow, Money refundedNow, DateTimeOffset now)
    {
        var after = InvoiceBalance.Of(invoice, allocatedNow, refundedNow);
        if (after.Status == before.Status)
        {
            return;
        }

        events.Publish(new InvoicePaidStatusChanged(
            ids.NewId(), now, invoice.Id, invoice.OrganisationId, invoice.BranchId, invoice.OrderId,
            before.Status.ToString(), after.Status.ToString(), after.Outstanding.Amount, after.Outstanding.Currency));
    }
}

/// <summary>Reverse a payment recorded in error.</summary>
/// <param name="PaymentId">The payment.</param>
/// <param name="OrganisationId">The caller's organisation.</param>
/// <param name="Reason">Why, recorded on the trail.</param>
/// <param name="By">The caller.</param>
public sealed record ReversePaymentCommand(Guid PaymentId, Guid OrganisationId, string? Reason, Guid By);

/// <summary>Pay money back to the customer.</summary>
/// <param name="OrganisationId">The caller's organisation.</param>
/// <param name="BranchId">The caller's branch.</param>
/// <param name="CashierId">The caller, whose open session the refund is recorded in.</param>
/// <param name="PaymentId">The payment whose advance is paid back; null when the source is an invoice.</param>
/// <param name="InvoiceId">The invoice whose surplus is paid back; null when the source is an advance.</param>
/// <param name="ModeCode">The mode it is paid through.</param>
/// <param name="Amount">How much, in rupees to the paisa.</param>
/// <param name="Reference">The reference the mode requires, where it does.</param>
/// <param name="ClientKey">The request's idempotency key, kept on the row.</param>
/// <param name="Reason">Why, recorded on the trail.</param>
/// <param name="By">The caller.</param>
public sealed record RecordRefundCommand(
    Guid OrganisationId,
    Guid BranchId,
    Guid CashierId,
    Guid? PaymentId,
    Guid? InvoiceId,
    string? ModeCode,
    decimal Amount,
    string? Reference,
    string? ClientKey,
    string? Reason,
    Guid By);

/// <summary>What the audit trail records of a refund: identifiers, the source and the mode — no amount, no reference.</summary>
internal sealed record RefundSnapshot(Guid BranchId, Guid CashierSessionId, Guid OrderId, string Source, Guid? PaymentId, Guid? InvoiceId, string ModeCode, bool HasReference)
{
    public static RefundSnapshot Of(Refund refund)
        => new(refund.BranchId, refund.CashierSessionId, refund.OrderId, refund.Source.ToString(), refund.PaymentId, refund.InvoiceId, refund.ModeCode, refund.Reference is not null);
}
