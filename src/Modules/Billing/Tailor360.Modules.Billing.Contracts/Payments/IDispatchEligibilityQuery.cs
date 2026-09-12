using Tailor360.Platform.Abstractions.Results;

namespace Tailor360.Modules.Billing.Contracts.Payments;

/// <summary>
/// Whether an order's garment jobs may be dispatched, answered from Billing's own records only
/// (<c>docs/prd/state-transitions.md</c> section 9.2): Custody's receive scan and Delivery's dispatch
/// never compute a balance themselves. Fails closed — an order Billing has not heard of, or one with no
/// posted invoice, answers <c>NotEvaluated</c> rather than throwing or answering null.
/// </summary>
public interface IDispatchEligibilityQuery
{
    /// <summary>The eligibility answer for dispatching these jobs of this order, right now.</summary>
    Task<DispatchEligibilityResult> GetDispatchEligibilityAsync(
        Guid orderId, IReadOnlyCollection<Guid> jobIds, Guid organisationId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Consumes a live approved exception exactly once: the caller — the dispatch authorisation — has
    /// already been told <c>ApprovedException</c> and is now committing to it. Re-validates everything
    /// the exception was bound to against the current state, because time has passed since it was
    /// approved: the outstanding balance, the job set, the policy version and the expiry, and that the
    /// person dispatching is not the person who approved it.
    /// </summary>
    Task<Result> ConsumeExceptionAsync(
        Guid dispatchExceptionId, Guid orderId, IReadOnlyCollection<Guid> jobIds, Guid dispatcherId, Guid organisationId,
        CancellationToken cancellationToken = default);
}

/// <summary>The dispatch eligibility answer, and the policy version it was decided against.</summary>
/// <param name="Reason">
/// <c>Paid</c>, <c>PartialAboveThreshold</c>, <c>PartialBelowThreshold</c>, <c>Unpaid</c>,
/// <c>ApprovedException</c> or <c>NotEvaluated</c>.
/// </param>
/// <param name="PolicyVersion">The dispatch policy version the answer was decided under.</param>
/// <param name="DispatchExceptionId">The live exception behind an <c>ApprovedException</c> answer; null otherwise.</param>
public sealed record DispatchEligibilityResult(string Reason, string PolicyVersion, Guid? DispatchExceptionId);
