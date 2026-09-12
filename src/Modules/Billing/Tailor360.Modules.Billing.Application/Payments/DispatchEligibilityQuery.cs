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
/// <param name="balances">The order balance, for the figures the rule is evaluated against.</param>
/// <param name="invoices">The posted invoices, read again inside the consumption's own transaction.</param>
/// <param name="payments">Allocations, refunds and the read-consistency the balance is composed under.</param>
/// <param name="exceptions">The store.</param>
/// <param name="policy">The dispatch policy in force.</param>
/// <param name="events">Billing's outbox.</param>
/// <param name="audit">The audit trail.</param>
/// <param name="clock">The clock.</param>
/// <param name="ids">The identifier generator.</param>
public sealed class DispatchEligibilityQuery(
    IFinancialTotalsQuery balances,
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

        var balance = await balances.GetOrderBalanceAsync(orderId, organisationId, cancellationToken);
        var reason = DispatchEligibilityRule.Evaluate(
            hasPostedInvoice: balance is { Invoices.Count: > 0 },
            hasLiveApprovedException: false,
            charges: Money.Rupees(balance?.Charges ?? 0m),
            outstanding: Money.Rupees(balance?.Outstanding ?? 0m),
            unappliedAdvances: Money.Rupees(balance?.UnappliedAdvances ?? 0m),
            rule: options.Rule,
            partialThreshold: options.PartialThreshold,
            allowOnAdvance: options.AllowOnAdvance,
            advanceThreshold: Money.Rupees(options.AdvanceThreshold));

        return new DispatchEligibilityResult(reason.ToString(), options.Version, null);
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
        async token => (await exceptions.FindAsync(dispatchExceptionId, organisationId, token))?.Status == DispatchExceptionStatus.Consumed,
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
