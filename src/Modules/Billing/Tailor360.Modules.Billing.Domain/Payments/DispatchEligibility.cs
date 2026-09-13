using Tailor360.Platform.Abstractions.Money;

namespace Tailor360.Modules.Billing.Domain.Payments;

/// <summary>
/// Whether an order's garment jobs may be dispatched, from Billing's own records (plan lines 1902-1907,
/// <c>docs/prd/exceptions.md</c> EX-10). A live approved exception always wins over the balance-based
/// answer, because it is what a caller consumes instead of one.
/// </summary>
public enum DispatchEligibilityReason
{
    /// <summary>No posted invoice covers the jobs, and no advance held against the order clears the
    /// configured floor while the branch's policy allows it. Fails closed.</summary>
    NotEvaluated = 0,

    /// <summary>A posted invoice covers the jobs and the branch's rule refuses what remains outstanding.</summary>
    Unpaid = 1,

    /// <summary>The paid share is below the branch's configured threshold under the partial rule.</summary>
    PartialBelowThreshold = 2,

    /// <summary>The paid share meets or exceeds the branch's configured threshold under the partial rule.</summary>
    PartialAboveThreshold = 3,

    /// <summary>Nothing is outstanding, or an advance held against the order clears the configured floor.</summary>
    Paid = 4,

    /// <summary>A live, approved dispatch exception covers exactly these jobs of this order.</summary>
    ApprovedException = 5,
}

/// <summary>The branch's dispatch policy rule (interim: <see cref="Full"/>, OD-04 leaves the numbers open).</summary>
public enum DispatchPolicyRule
{
    /// <summary>Dispatch requires nothing outstanding.</summary>
    Full = 0,

    /// <summary>Dispatch requires the paid share to meet a configured threshold.</summary>
    PartialThreshold = 1,
}

/// <summary>
/// The dispatch eligibility answer, evaluated as a pure function of Billing's own figures and the
/// branch's configured policy — no I/O, so the rule for each of the six answers is a unit test rather
/// than an integration test.
/// </summary>
public static class DispatchEligibilityRule
{
    /// <summary>Evaluates the answer for one order's jobs, right now.</summary>
    /// <param name="hasPostedInvoice">Whether at least one posted invoice covers the order.</param>
    /// <param name="hasLiveApprovedException">Whether a live, approved exception covers exactly these jobs.</param>
    /// <param name="charges">The posted invoices' grand totals, in all.</param>
    /// <param name="outstanding">What the posted invoices still owe, in all; never below zero.</param>
    /// <param name="unappliedAdvances">What is held against the order and not yet applied.</param>
    /// <param name="rule">The branch's configured rule.</param>
    /// <param name="partialThreshold">The paid share, from zero to one, the partial rule requires.</param>
    /// <param name="allowOnAdvance">Whether an advance alone may cover an order with no posted invoice.</param>
    /// <param name="advanceThreshold">
    /// The floor an unapplied advance must clear for <paramref name="allowOnAdvance"/> to answer
    /// <see cref="DispatchEligibilityReason.Paid"/>. Zero means the flag has no effect: OD-04 has not
    /// sourced a number, so a floor of zero is the only default that invents nothing.
    /// </param>
    public static DispatchEligibilityReason Evaluate(
        bool hasPostedInvoice,
        bool hasLiveApprovedException,
        Money charges,
        Money outstanding,
        Money unappliedAdvances,
        DispatchPolicyRule rule,
        decimal partialThreshold,
        bool allowOnAdvance,
        Money advanceThreshold)
    {
        if (hasLiveApprovedException)
        {
            return DispatchEligibilityReason.ApprovedException;
        }

        if (!hasPostedInvoice)
        {
            return allowOnAdvance && advanceThreshold.Amount > 0m && unappliedAdvances.Amount >= advanceThreshold.Amount
                ? DispatchEligibilityReason.Paid
                : DispatchEligibilityReason.NotEvaluated;
        }

        if (outstanding.IsZero)
        {
            return DispatchEligibilityReason.Paid;
        }

        if (rule != DispatchPolicyRule.PartialThreshold)
        {
            return DispatchEligibilityReason.Unpaid;
        }

        var paidShare = charges.Amount > 0m ? (charges.Amount - outstanding.Amount) / charges.Amount : 0m;
        return paidShare >= partialThreshold
            ? DispatchEligibilityReason.PartialAboveThreshold
            : DispatchEligibilityReason.PartialBelowThreshold;
    }
}
