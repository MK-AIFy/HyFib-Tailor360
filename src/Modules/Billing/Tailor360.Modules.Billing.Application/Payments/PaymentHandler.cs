using Tailor360.Modules.Billing.Application.Abstractions;
using Tailor360.Modules.Billing.Contracts.Events;
using Tailor360.Modules.Billing.Domain;
using Tailor360.Modules.Billing.Domain.Invoicing;
using Tailor360.Modules.Billing.Domain.Payments;
using Tailor360.Modules.Identity.Contracts.Directory;
using Tailor360.Platform.Abstractions.Auditing;
using Tailor360.Platform.Abstractions.Barcodes;
using Tailor360.Platform.Abstractions.Identifiers;
using Tailor360.Platform.Abstractions.Money;
using Tailor360.Platform.Abstractions.Results;
using Tailor360.Platform.Abstractions.Time;

namespace Tailor360.Modules.Billing.Application.Payments;

/// <summary>
/// Records a payment and applies it (#162). The money goes to the order's posted invoices oldest first in
/// the transaction that records it (INV-PAY-04), the rest is held as an advance until an invoice posts
/// (INV-PAY-05), and every allocation is serialised on the order's invoice rows (INV-PAY-03). A payment is
/// refused without an open cashier session (INV-CSH-02) and never changes once written (INV-PAY-01).
/// </summary>
/// <param name="payments">The store.</param>
/// <param name="invoices">The invoices the money is applied to.</param>
/// <param name="orders">What Billing knows about the order.</param>
/// <param name="sessions">The cashier's open session.</param>
/// <param name="modes">The modes the branch takes money in.</param>
/// <param name="branches">The branch register, for the code and the calendar the receipt number is drawn in.</param>
/// <param name="events">Billing's outbox.</param>
/// <param name="audit">
/// The audit trail used only by <see cref="ApplyAdvancesOnPostingAsync"/>, which stages its business
/// writes for the dispatcher to save with the inbox row and so cannot also stage its audit entry on
/// <paramref name="billingAudit"/>'s same context without saving those business writes early.
/// </param>
/// <param name="billingAudit">
/// The audit trail used by <see cref="RecordAsync"/> and <see cref="AllocateManuallyAsync"/>, which own
/// their transaction and can ride the entry on the save they already make.
/// </param>
/// <param name="clock">The clock.</param>
/// <param name="ids">The identifier generator.</param>
public sealed class PaymentHandler(
    IPaymentStore payments,
    IInvoiceStore invoices,
    IOrderFactStore orders,
    ICashierSessionStore sessions,
    IPaymentModeStore modes,
    IBranchDirectory branches,
    IBillingEventPublisher events,
    IAuditWriter audit,
    IBillingAuditWriter billingAudit,
    IClock clock,
    IIdGenerator ids)
{
    /// <summary>A payment was taken and recorded; the matrix's action for <c>payments.record</c>.</summary>
    public const string RecordedAction = "payments.record";

    /// <summary>An advance was applied by the rule when an invoice posted; the matrix's action for <c>payments.allocate</c>.</summary>
    public const string AllocatedAction = "payments.allocate";

    /// <summary>An advance was applied by hand; the matrix's action for <c>payments.allocate_manual</c>.</summary>
    public const string AllocatedManuallyAction = "payments.allocate_manual";

    /// <summary>
    /// Records a payment against an order in the caller's open session and allocates it at once. The
    /// request is validated before anything is looked up, so a malformed amount is answered as such
    /// rather than as a missing session; the checks are then repeated inside the transaction over what
    /// is held, because a close, a cancellation or a rival payment may land between the two.
    /// </summary>
    public async Task<Result<RecordedPayment>> RecordAsync(RecordPaymentCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var modeCode = command.ModeCode?.Trim() ?? string.Empty;
        var amount = Money.Rupees(command.Amount);
        var valid = Payment.Validate(modeCode, amount, command.Reference);
        if (valid.IsFailure)
        {
            return Result.Failure<RecordedPayment>(valid.Error);
        }

        var checkedMode = await CheckModeAsync(modeCode, command.BranchId, command.OrganisationId, command.Reference, cancellationToken);
        if (checkedMode.IsFailure)
        {
            return Result.Failure<RecordedPayment>(checkedMode.Error);
        }

        var order = await orders.FindAsync(command.OrderId, command.OrganisationId, cancellationToken);
        var checkedOrder = CheckOrder(order, command.BranchId);
        if (checkedOrder.IsFailure)
        {
            return Result.Failure<RecordedPayment>(checkedOrder.Error);
        }

        var session = await sessions.FindOpenAsync(command.BranchId, command.CashierId, command.OrganisationId, cancellationToken);
        if (session is null)
        {
            return Result.Failure<RecordedPayment>(BillingErrors.CashierSessionRequired);
        }

        var branch = await BranchAsync(command.BranchId, command.OrganisationId, cancellationToken);
        if (branch.IsFailure)
        {
            return Result.Failure<RecordedPayment>(branch.Error);
        }

        var (branchCode, timeZone) = branch.Value;

        // Minted once per command rather than per attempt: it is how a commit whose outcome the connection
        // lost is told apart from money that was never recorded.
        var paymentId = ids.NewId();
        var advanceId = ids.NewId();
        var receiptId = ids.NewId();
        var receiptBarcode = BarcodePayload.Mint(BarcodePayload.ReceiptNamespace).Value;
        var recorded = await payments.InAllocationTransactionAsync(
            command.OrderId,
            async token =>
            {
                // The session, held in share mode so the close waits for this payment and counts it, and
                // read again under the hold: closed in the meantime means refused.
                await sessions.LockForPaymentAsync(session.Id, token);
                var held = await sessions.FindAsync(session.Id, command.OrganisationId, token);
                if (held is null || !held.IsOpen)
                {
                    return Result.Failure<RecordedPayment>(BillingErrors.CashierSessionRequired);
                }

                await orders.LockForReadAsync(command.OrderId, token);
                var current = await orders.FindAsync(command.OrderId, command.OrganisationId, token);
                var stillOpen = CheckOrder(current, command.BranchId);
                if (stillOpen.IsFailure)
                {
                    return Result.Failure<RecordedPayment>(stillOpen.Error);
                }

                var posted = await invoices.ListPostedForOrderAsync(command.OrderId, command.OrganisationId, token);
                var balances = await BalancesAsync(posted, token);

                var now = clock.UtcNow;
                var created = Payment.Record(
                    paymentId, command.OrganisationId, command.BranchId, held.Id, command.CashierId, current!.CustomerId, command.OrderId,
                    modeCode, amount, command.Reference, command.ClientKey, now, command.By);
                if (created.IsFailure)
                {
                    return Result.Failure<RecordedPayment>(created.Error);
                }

                var payment = created.Value;
                var applied = payment.AllocateAtRecording(
                    balances.Select(balance => (balance.Before.InvoiceId, balance.Before.Outstanding)).ToList(), ids.NewId, advanceId, now, command.By);
                if (applied.IsFailure)
                {
                    return Result.Failure<RecordedPayment>(applied.Error);
                }

                payments.Add(payment);
                events.Publish(new PaymentRecorded(
                    ids.NewId(), now, payment.Id, payment.OrganisationId, payment.BranchId, payment.CashierSessionId, payment.CustomerId, payment.OrderId,
                    payment.ModeCode, payment.Amount.Amount, payment.Amount.Currency));
                foreach (var allocation in applied.Value)
                {
                    var (invoice, before) = balances.Single(balance => balance.Invoice.Id == allocation.InvoiceId);
                    PublishAllocated(payment, allocation, now);
                    PublishStatusIfMoved(invoice, before, before.Allocated + allocation.Amount, now);
                }

                if (payment.Advance is { } advance)
                {
                    events.Publish(new AdvanceReceived(
                        ids.NewId(), now, advance.Id, payment.OrganisationId, payment.BranchId, payment.Id, payment.CustomerId, payment.OrderId,
                        advance.Amount.Amount, advance.Amount.Currency));
                }

                // The receipt, issued with the payment (the T4 boundary): its number drawn under the sequence
                // row's lock in this transaction, so a refused payment burns none, and the figures the customer
                // is handed frozen as they stand at this instant.
                var issuedOn = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(now, timeZone).DateTime);
                var financialYear = DocumentNumbers.FinancialYearToken(issuedOn);
                var sequence = await payments.AllocateAsync(DocumentNumbers.ReceiptSequence, DocumentNumbers.SequenceScope(command.OrganisationId, branchCode, financialYear), token);
                var number = DocumentNumbers.Compose(DocumentNumbers.ReceiptPrefix, branchCode, financialYear, sequence);
                if (number.IsFailure)
                {
                    return Result.Failure<RecordedPayment>(number.Error);
                }

                var outstanding = balances.Aggregate(Money.Zero, (sum, balance) => sum + balance.Before.Outstanding) - payment.Allocated;
                var receipt = Receipt.Issue(receiptId, payment, number.Value, receiptBarcode, financialYear, issuedOn, outstanding, now, command.By);
                if (receipt.IsFailure)
                {
                    return Result.Failure<RecordedPayment>(receipt.Error);
                }

                payments.AddReceipt(receipt.Value);

                // Staged before the save so the entry rides the same SaveChangesAsync as the payment it
                // describes, and the two commit or roll back together (issue #179). No amount and no
                // reference in the summary or the snapshot: the trail is read by more people than the
                // drawer is, and the reference is the terminal's, not ours to spread.
                await BillingAudit.StageAsync(
                    billingAudit, RecordedAction, BillingAudit.PaymentEntity, payment.Id,
                    payment.Advance is null
                        ? $"Payment recorded in {payment.ModeCode} against order {order!.OrderNumber}; allocated to {payment.Allocations.Count} invoice(s); receipt {receipt.Value.ReceiptNumber} issued."
                        : $"Payment recorded in {payment.ModeCode} against order {order!.OrderNumber}; allocated to {payment.Allocations.Count} invoice(s), the rest held as an advance; receipt {receipt.Value.ReceiptNumber} issued.",
                    null, null, PaymentSnapshot.Of(payment), token);

                var saved = await payments.SaveAsync(token);
                return saved.IsFailure ? Result.Failure<RecordedPayment>(saved.Error) : Result.Success(new RecordedPayment(payment, receipt.Value));
            },
            async token => await payments.FindAsync(paymentId, command.OrganisationId, token) is not null,
            cancellationToken);

        return recorded;
    }

    /// <summary>
    /// Applies part of a payment's held advance to a posted invoice of the same order by hand, under
    /// <c>payments.allocate_manual</c> with its step-up and its reason. Never more than is held, never
    /// more than the invoice owes, and serialised on the order's invoice rows like every allocation.
    /// </summary>
    public async Task<Result<Payment>> AllocateManuallyAsync(AllocateAdvanceCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var payment = await payments.FindAsync(command.PaymentId, command.OrganisationId, cancellationToken);
        if (payment is null)
        {
            return Result.Failure<Payment>(BillingErrors.PaymentNotFound);
        }

        if (payment.Advance is null || payment.UnappliedAdvance.IsZero)
        {
            return Result.Failure<Payment>(BillingErrors.NoAdvanceHeld);
        }

        // Against the rule, so with a reason on the trail: checked here, because the audit filter records
        // what the handler accepted rather than deciding for it.
        var reason = Invoice.CheckReason(command.Reason);
        if (reason.IsFailure)
        {
            return Result.Failure<Payment>(reason.Error);
        }

        var allocationId = ids.NewId();
        var amount = Money.Rupees(command.Amount);
        var applied = await payments.InAllocationTransactionAsync(
            payment.OrderId,
            async token =>
            {
                var fresh = await payments.FindAsync(command.PaymentId, command.OrganisationId, token);
                if (fresh is null)
                {
                    return Result.Failure<Payment>(BillingErrors.PaymentNotFound);
                }

                var invoice = await invoices.FindAsync(command.InvoiceId, command.OrganisationId, token);
                if (invoice is null || !invoice.IsPosted || invoice.OrderId != fresh.OrderId)
                {
                    return Result.Failure<Payment>(BillingErrors.AllocationInvoiceNotOfOrder("invoiceId"));
                }

                var before = (await BalancesAsync([invoice], token))[0].Before;
                var now = clock.UtcNow;
                var allocation = fresh.ApplyAdvance(allocationId, invoice.Id, before.Outstanding, amount, AllocationKind.Manual, now, command.By);
                if (allocation.IsFailure)
                {
                    return Result.Failure<Payment>(allocation.Error);
                }

                PublishAllocated(fresh, allocation.Value, now);
                PublishAdvanceApplied(fresh, allocation.Value, now);
                PublishStatusIfMoved(invoice, before, before.Allocated + allocation.Value.Amount, now);

                // Staged before the save so the entry rides the same SaveChangesAsync as the allocation
                // it describes, and the two commit or roll back together (issue #179).
                await BillingAudit.StageAsync(
                    billingAudit, AllocatedManuallyAction, BillingAudit.PaymentEntity, fresh.Id,
                    "An advance was applied to an invoice by hand.",
                    command.Reason!.Trim(), PaymentSnapshot.Of(payment), PaymentSnapshot.Of(fresh), token);

                var saved = await payments.SaveAsync(token);
                return saved.IsFailure ? Result.Failure<Payment>(saved.Error) : Result.Success(fresh);
            },
            async token => (await payments.FindAsync(command.PaymentId, command.OrganisationId, token))?.Allocations.Any(allocation => allocation.Id == allocationId) == true,
            cancellationToken);

        return applied;
    }

    /// <summary>
    /// The rule that applies held advances when the order posts an invoice (INV-PAY-05): over the order's
    /// posted invoices oldest first, oldest advance first, until the money or the invoices run out. The
    /// event names one invoice, but the rule is the order's: two invoices posted before either event is
    /// handled are applied to in posting order whichever event arrives first, because the second run finds
    /// what the first left. Called by the outbox consumer of <c>billing.invoice-posted.v1</c> inside the
    /// dispatcher's transaction, so it stages its writes and does not save; the dispatcher commits them
    /// with the inbox row. Audited without a user.
    /// </summary>
    public async Task ApplyAdvancesOnPostingAsync(Guid invoiceId, Guid organisationId, CancellationToken cancellationToken = default)
    {
        // The order's invoice rows, held before anything is read, so a payment recorded meanwhile has
        // committed its allocations and this one sees them.
        var orderId = await payments.LockOrderInvoicesOfAsync(invoiceId, cancellationToken);
        if (orderId is not { } order)
        {
            return;
        }

        var held = (await payments.ListForOrderAsync(order, organisationId, cancellationToken))
            .Where(payment => !payment.UnappliedAdvance.IsZero && !payment.UnappliedAdvance.IsNegative)
            .ToList();
        if (held.Count == 0)
        {
            return;
        }

        var posted = (await invoices.ListPostedForOrderAsync(order, organisationId, cancellationToken))
            .Where(invoice => !invoice.IsCancelled)
            .ToList();
        var now = clock.UtcNow;
        foreach (var (invoice, before) in await BalancesAsync(posted, cancellationToken))
        {
            var outstanding = before.Outstanding;
            if (outstanding.IsZero)
            {
                continue;
            }

            var appliedHere = 0;
            foreach (var payment in held)
            {
                var available = payment.UnappliedAdvance;
                if (available.IsZero)
                {
                    continue;
                }

                var amount = available < outstanding ? available : outstanding;
                var allocation = payment.ApplyAdvance(ids.NewId(), invoice.Id, outstanding, amount, AllocationKind.AdvanceApplied, now, null);
                if (allocation.IsFailure)
                {
                    // The domain refused what the figures above allowed: a defect, not a business outcome.
                    throw new InvalidOperationException($"The rule applying an advance was refused: {allocation.Error.Code}.");
                }

                PublishAllocated(payment, allocation.Value, now);
                PublishAdvanceApplied(payment, allocation.Value, now);
                outstanding -= amount;
                appliedHere++;
                if (outstanding.IsZero)
                {
                    break;
                }
            }

            if (appliedHere == 0)
            {
                // Every advance is spent; the invoices after this one stay as they are.
                break;
            }

            PublishStatusIfMoved(invoice, before, before.Allocated + (before.Outstanding - outstanding), now);

            // The trail commits on its own context; the allocations commit with the inbox row. A run that
            // rolls back after this line is run again on redelivery and stages the same allocations, so the
            // entry stays true in substance.
            await BillingAudit.RecordAsync(
                audit, AllocatedAction, BillingAudit.InvoiceEntity, invoice.Id,
                $"Held advances applied to invoice {invoice.InvoiceNumber} by the rule on posting: {appliedHere} allocation(s).",
                null, null, new { invoice.OrderId, Applications = appliedHere, Paid = outstanding.IsZero }, cancellationToken);
        }
    }

    private async Task<Result<(string BranchCode, TimeZoneInfo TimeZone)>> BranchAsync(Guid branchId, Guid organisationId, CancellationToken cancellationToken)
    {
        var branch = await branches.FindAsync(branchId, cancellationToken);
        if (branch is null || branch.OrganisationId != organisationId)
        {
            return Result.Failure<(string, TimeZoneInfo)>(BillingErrors.BranchNotKnown);
        }

        var code = DocumentNumbers.NormaliseBranchCode(branch.Code);
        if (code.IsFailure)
        {
            return Result.Failure<(string, TimeZoneInfo)>(code.Error);
        }

        return Result.Success((code.Value, TimeZoneInfo.FindSystemTimeZoneById(branch.TimeZoneId)));
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

        if (mode.RequiresProvider)
        {
            // OD-03: no gateway is chosen. A provider-mediated payment is recorded through an intent and a
            // verified callback (Integration), never typed in at the counter.
            return Result.Failure(BillingErrors.PaymentProviderNotConfigured("modeCode"));
        }

        return mode.RequiresReference && string.IsNullOrWhiteSpace(reference)
            ? Result.Failure(BillingErrors.ReferenceRequired("reference"))
            : Result.Success();
    }

    private static Result CheckOrder(OrderFact? order, Guid branchId)
    {
        if (order is null)
        {
            return Result.Failure(BillingErrors.OrderNotKnown);
        }

        if (order.CustomerId == Guid.Empty)
        {
            return Result.Failure(BillingErrors.OrderNotYetConfirmed);
        }

        if (!order.IsInvoiceable)
        {
            return Result.Failure(BillingErrors.OrderCancelled);
        }

        return order.BranchId == branchId ? Result.Success() : Result.Failure(BillingErrors.OrderAtAnotherBranch);
    }

    private void PublishAllocated(Payment payment, PaymentAllocation allocation, DateTimeOffset now)
        => events.Publish(new PaymentAllocated(
            ids.NewId(), now, payment.Id, payment.OrganisationId, payment.BranchId, allocation.Id, allocation.InvoiceId, allocation.AdvanceId,
            allocation.Amount.Amount, allocation.Amount.Currency, allocation.Kind.ToString()));

    private void PublishAdvanceApplied(Payment payment, PaymentAllocation allocation, DateTimeOffset now)
        => events.Publish(new AdvanceApplied(
            ids.NewId(), now, payment.Advance!.Id, payment.OrganisationId, payment.BranchId, payment.Id, allocation.InvoiceId,
            allocation.Amount.Amount, payment.UnappliedAdvance.Amount, allocation.Amount.Currency));

    /// <summary>The balance of each invoice as the rows stand: allocations from payments not reversed, refunds paid back.</summary>
    private async Task<List<(Invoice Invoice, InvoiceBalance Before)>> BalancesAsync(IReadOnlyList<Invoice> posted, CancellationToken cancellationToken)
    {
        var ids = posted.Select(invoice => invoice.Id).ToList();
        var allocated = await payments.AllocatedByInvoiceAsync(ids, cancellationToken);
        var refunded = await payments.RefundedByInvoiceAsync(ids, cancellationToken);
        return posted.Select(invoice => (invoice, InvoiceBalance.Of(invoice, allocated.GetValueOrDefault(invoice.Id, Money.Zero), refunded.GetValueOrDefault(invoice.Id, Money.Zero)))).ToList();
    }

    private void PublishStatusIfMoved(Invoice invoice, InvoiceBalance before, Money allocatedNow, DateTimeOffset now)
    {
        var after = InvoiceBalance.Of(invoice, allocatedNow, before.Refunds);
        if (after.Status == before.Status)
        {
            return;
        }

        events.Publish(new InvoicePaidStatusChanged(
            ids.NewId(), now, invoice.Id, invoice.OrganisationId, invoice.BranchId, invoice.OrderId,
            before.Status.ToString(), after.Status.ToString(), after.Outstanding.Amount, after.Outstanding.Currency));
    }
}

/// <summary>Record a payment.</summary>
/// <param name="OrganisationId">The caller's organisation.</param>
/// <param name="BranchId">The caller's branch.</param>
/// <param name="CashierId">The caller, whose open session the payment is recorded in.</param>
/// <param name="OrderId">The order the money is taken against; the customer is the order's.</param>
/// <param name="ModeCode">The payment mode.</param>
/// <param name="Amount">How much, in rupees to the paisa.</param>
/// <param name="Reference">The external reference, where the mode requires one.</param>
/// <param name="ClientKey">The request's idempotency key, kept on the row as its own guard against a twin.</param>
/// <param name="By">The caller.</param>
public sealed record RecordPaymentCommand(
    Guid OrganisationId,
    Guid BranchId,
    Guid CashierId,
    Guid OrderId,
    string? ModeCode,
    decimal Amount,
    string? Reference,
    string? ClientKey,
    Guid By);

/// <summary>A payment as recorded, with the receipt issued for it in the same transaction.</summary>
/// <param name="Payment">The payment, allocated.</param>
/// <param name="Receipt">Its receipt.</param>
public sealed record RecordedPayment(Payment Payment, Receipt Receipt);

/// <summary>Apply a held advance by hand.</summary>
/// <param name="PaymentId">The payment holding the advance.</param>
/// <param name="OrganisationId">The caller's organisation.</param>
/// <param name="InvoiceId">The posted invoice of the same order to apply it to.</param>
/// <param name="Amount">How much, in rupees to the paisa.</param>
/// <param name="Reason">Why, recorded on the trail.</param>
/// <param name="By">The caller.</param>
public sealed record AllocateAdvanceCommand(
    Guid PaymentId,
    Guid OrganisationId,
    Guid InvoiceId,
    decimal Amount,
    string? Reason,
    Guid By);

/// <summary>What the audit trail records of a payment: identifiers, the mode, the counts — no amount, no reference.</summary>
internal sealed record PaymentSnapshot(Guid BranchId, Guid CashierSessionId, Guid OrderId, string ModeCode, string Status, bool HasReference, int Allocations, bool HoldsAdvance, bool Reversed)
{
    /// <param name="payment">The payment.</param>
    /// <param name="reversed">
    /// Overrides <c>payment.IsReversed</c> where the caller knows better than the in-memory graph — a
    /// reversal just added to the store does not set the payment's own navigation property, so a
    /// snapshot taken before a fresh read would otherwise read a reversal that in fact just landed as
    /// not having happened.
    /// </param>
    public static PaymentSnapshot Of(Payment payment, bool? reversed = null)
        => new(payment.BranchId, payment.CashierSessionId, payment.OrderId, payment.ModeCode, payment.Status.ToString(), payment.Reference is not null, payment.Allocations.Count, !payment.UnappliedAdvance.IsZero, reversed ?? payment.IsReversed);
}
