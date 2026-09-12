using Microsoft.Extensions.Options;
using Tailor360.Modules.Billing.Application.Abstractions;
using Tailor360.Modules.Billing.Contracts.Events;
using Tailor360.Modules.Billing.Contracts.Payments;
using Tailor360.Modules.Billing.Domain;
using Tailor360.Modules.Billing.Domain.Payments;
using Tailor360.Platform.Abstractions.Auditing;
using Tailor360.Platform.Abstractions.Identifiers;
using Tailor360.Platform.Abstractions.Money;
using Tailor360.Platform.Abstractions.Results;
using Tailor360.Platform.Abstractions.Time;

namespace Tailor360.Modules.Billing.Application.Payments;

/// <summary>
/// Answers <see cref="IDispatchEligibilityQuery"/> from Billing's own records only, and consumes the
/// live exception behind an <c>ApprovedException</c> answer exactly once (#164, plan lines 1902-1916).
/// </summary>
/// <param name="invoices">The posted invoices.</param>
/// <param name="payments">Allocations, refunds, unapplied advances and the order-invoice lock.</param>
/// <param name="exceptions">The store.</param>
/// <param name="policy">The dispatch policy in force.</param>
/// <param name="events">Billing's outbox.</param>
/// <param name="audit">The audit trail.</param>
/// <param name="clock">The clock.</param>
/// <param name="ids">The identifier generator.</param>
public sealed class DispatchEligibilityQuery(
    IInvoiceStore invoices,
    IPaymentStore payments,
    IDispatchExceptionStore exceptions,
    IOptions<DispatchPolicyOptions> policy,
    IBillingEventPublisher events,
    IAuditWriter audit,
    IClock clock,
    IIdGenerator ids) : IDispatchEligibilityQuery
{
    /// <summary>The audit action for every consumption attempt, refused or not.</summary>
    public const string ConsumedAction = "billing.consume_dispatch_exception";

    /// <inheritdoc />
    public async Task<DispatchEligibilityResult> GetDispatchEligibilityAsync(
        Guid orderId, IReadOnlyCollection<Guid> jobIds, Guid organisationId, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(jobIds);

        var options = policy.Value;
        var live = await exceptions.FindLiveMatchingAsync(orderId, organisationId, jobIds, clock.UtcNow, cancellationToken);
        if (live is not null)
        {
            return new DispatchEligibilityResult(DispatchEligibilityReason.ApprovedException.ToString(), live.PolicyVersion, live.Id);
        }

        var (allJobsCovered, charges, outstanding) = await JobAttributedBalanceAsync(orderId, organisationId, jobIds, cancellationToken);
        var unappliedAdvances = (await payments.ListForOrderAsync(orderId, organisationId, cancellationToken))
            .Aggregate(Money.Zero, (sum, payment) => sum + payment.UnappliedAdvance);

        var reason = DispatchEligibilityRule.Evaluate(
            hasPostedInvoice: allJobsCovered,
            hasLiveApprovedException: false,
            charges: charges,
            outstanding: outstanding,
            unappliedAdvances: unappliedAdvances,
            rule: options.Rule,
            partialThreshold: options.PartialThreshold,
            allowOnAdvance: options.AllowOnAdvance,
            advanceThreshold: Money.Rupees(options.AdvanceThreshold));

        return new DispatchEligibilityResult(reason.ToString(), options.Version, null);
    }

    /// <summary>
    /// The charges and the outstanding balance attributable to a specific set of garment jobs: only the
    /// posted, uncancelled invoices that cover at least one of them, and only once every one of them is
    /// covered by one of those invoices. A job an order's other invoices happen to have settled proves
    /// nothing about a job nobody has ever charged for — the whole point of asking per job rather than
    /// per order — so a requested job with no covering invoice fails the whole answer closed rather
    /// than being averaged away by a sibling job's invoice.
    /// </summary>
    private async Task<(bool AllJobsCovered, Money Charges, Money Outstanding)> JobAttributedBalanceAsync(
        Guid orderId, Guid organisationId, IReadOnlyCollection<Guid> jobIds, CancellationToken cancellationToken)
    {
        if (jobIds.Count == 0)
        {
            return (false, Money.Zero, Money.Zero);
        }

        var posted = await invoices.ListPostedForOrderAsync(orderId, organisationId, cancellationToken);
        var covering = posted.Where(invoice => !invoice.IsCancelled && invoice.GarmentJobIds.Intersect(jobIds).Any()).ToList();
        var coveredJobIds = covering.SelectMany(invoice => invoice.GarmentJobIds).ToHashSet();
        if (!jobIds.All(coveredJobIds.Contains))
        {
            return (false, Money.Zero, Money.Zero);
        }

        var invoiceIds = covering.Select(invoice => invoice.Id).ToList();
        var allocated = await payments.AllocatedByInvoiceAsync(invoiceIds, cancellationToken);
        var refunded = await payments.RefundedByInvoiceAsync(invoiceIds, cancellationToken);
        var balances = covering.Select(invoice => InvoiceBalance.Of(
            invoice, allocated.GetValueOrDefault(invoice.Id, Money.Zero), refunded.GetValueOrDefault(invoice.Id, Money.Zero)));

        var charges = Money.Zero;
        var outstanding = Money.Zero;
        foreach (var balance in balances)
        {
            charges += balance.Charges;
            outstanding += balance.Outstanding;
        }

        return (true, charges, outstanding);
    }

    /// <inheritdoc />
    public async Task<Result> ConsumeExceptionAsync(
        Guid dispatchExceptionId, Guid orderId, IReadOnlyCollection<Guid> jobIds, Guid dispatcherId, Guid organisationId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(jobIds);

        var found = await exceptions.FindAsync(dispatchExceptionId, organisationId, cancellationToken);
        if (found is null)
        {
            return Result.Failure(BillingErrors.DispatchExceptionNotFound);
        }

        var before = DispatchExceptionSnapshot.Of(found);
        var currentPolicyVersion = policy.Value.Version;

        var outcome = await exceptions.RunExclusiveAsync(dispatchExceptionId, async token =>
        {
            var fresh = await exceptions.FindAsync(dispatchExceptionId, organisationId, token);
            if (fresh is null)
            {
                return Result.Failure<DispatchException>(BillingErrors.DispatchExceptionNotFound);
            }

            if (fresh.OrderId != orderId)
            {
                return Result.Failure<DispatchException>(BillingErrors.DispatchExceptionNotFound);
            }

            // Locked before the balance is read, so a refund, a reversal, an allocation or an invoice
            // posting racing this consumption cannot commit between the read and this transaction's own
            // commit: the outstanding figure below is the one this attempt's outcome is judged against,
            // not one a concurrent writer has since moved past.
            await payments.LockOrderInvoicesAsync(orderId, token);

            var currentOutstanding = await OutstandingAsync(orderId, organisationId, token);
            var consumed = fresh.Consume(dispatcherId, currentOutstanding, jobIds, currentPolicyVersion, clock.UtcNow);
            if (consumed.IsFailure)
            {
                return Result.Failure<DispatchException>(consumed.Error);
            }

            events.Publish(new DispatchExceptionConsumed(ids.NewId(), clock.UtcNow, fresh.Id, fresh.OrganisationId, fresh.BranchId, fresh.OrderId, dispatcherId));

            var saved = await exceptions.SaveAsync(token);
            return saved.IsFailure ? Result.Failure<DispatchException>(saved.Error) : Result.Success(fresh);
        },
        // Checked by this attempt's own dispatcher, not merely by status: after an ambiguous commit a
        // concurrent attempt may be the one that actually landed as Consumed, and this attempt's own
        // write never took effect. Reporting that as success here would tell two different dispatchers
        // they had each consumed the same exception.
        async token => (await exceptions.FindAsync(dispatchExceptionId, organisationId, token))?.ConsumedBy == dispatcherId,
        cancellationToken);

        // Every attempt is audited, refused or not: a refusal here is a dispatch that did not happen,
        // which is exactly the kind of thing the trail exists to show. No amount in the summary.
        await BillingAudit.RecordAsync(
            audit, ConsumedAction, BillingAudit.DispatchExceptionEntity, dispatchExceptionId,
            outcome.IsSuccess ? "Dispatch exception consumed." : $"Dispatch exception consumption refused ({outcome.Error.Code}).",
            null, before, outcome.IsSuccess ? DispatchExceptionSnapshot.Of(outcome.Value) : before, cancellationToken);

        return outcome.IsFailure ? Result.Failure(outcome.Error) : Result.Success();
    }

    /// <summary>
    /// What the order's posted invoices still owe in all, read the same way <c>RefundHandler</c> does
    /// inside its own transaction: through the stores directly, never through
    /// <see cref="IFinancialTotalsQuery"/>, which opens a transaction of its own.
    /// </summary>
    private async Task<Money> OutstandingAsync(Guid orderId, Guid organisationId, CancellationToken cancellationToken)
    {
        var posted = await invoices.ListPostedForOrderAsync(orderId, organisationId, cancellationToken);
        var invoiceIds = posted.Select(invoice => invoice.Id).ToList();
        var allocated = await payments.AllocatedByInvoiceAsync(invoiceIds, cancellationToken);
        var refunded = await payments.RefundedByInvoiceAsync(invoiceIds, cancellationToken);

        return posted
            .Select(invoice => InvoiceBalance.Of(
                invoice, allocated.GetValueOrDefault(invoice.Id, Money.Zero), refunded.GetValueOrDefault(invoice.Id, Money.Zero)))
            .Aggregate(Money.Zero, (sum, balance) => sum + balance.Outstanding);
    }
}
