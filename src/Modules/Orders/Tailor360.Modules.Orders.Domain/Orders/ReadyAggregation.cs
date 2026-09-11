namespace Tailor360.Modules.Orders.Domain.Orders;

/// <summary>
/// Which garment jobs must be ready before the order as a whole is.
/// </summary>
/// <remarks>
/// <para>
/// <strong>This is the open half of SQ-02.</strong> <c>docs/prd/state-transitions.md</c> section 10
/// records the interim position as "<em>ready</em> when the job set required by the branch dispatch
/// policy is ready" — and leaves which job set that is unsettled. Rather than guess, the answer arrives
/// as a parameter on every command that can move the order's status, so the rule is visible at every
/// call site and changes in exactly one place when SQ-02 settles.
/// </para>
/// <para>
/// It is a parameter rather than stored state because dispatch policy is branch configuration evaluated
/// at the scan (issue #48), not a property of the order: the same order recomputed after the branch
/// changes its policy must answer with the new policy, not the one in force when it was confirmed.
/// </para>
/// <para>
/// It is an Orders-local vocabulary rather than a copy of Custody's <c>whole_order</c> /
/// <c>per_job</c> / <c>exception</c> policy because ARCH-004 forbids the reference and copying another
/// module's enumeration would tie one module's release to another's. The application maps the branch
/// policy onto these two values; <c>exception</c> is a dispatch-gate concept (state-transitions.md
/// section 9.2) and has no bearing on which jobs the ready gate must have passed.
/// </para>
/// </remarks>
public enum ReadyAggregation
{
    /// <summary>
    /// Every job the order still owes the customer must be ready.
    /// </summary>
    /// <remarks>
    /// The conservative reading, and what the branch policy <c>whole_order</c> asks for: the customer
    /// collects one parcel, so one outstanding garment keeps the whole order out of the delivery queue.
    /// </remarks>
    EveryDeliverableJob = 0,

    /// <summary>
    /// One ready job is enough.
    /// </summary>
    /// <remarks>
    /// What the branch policy <c>per_job</c> asks for, where garments are handed over as they finish. A
    /// <c>deliver_together</c> dependency still binds its siblings at the gate itself (INV-JOB-09), so
    /// this value loosens the order's status without loosening that promise.
    /// </remarks>
    AnyDeliverableJob = 1,
}
