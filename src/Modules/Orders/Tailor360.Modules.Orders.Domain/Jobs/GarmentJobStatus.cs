namespace Tailor360.Modules.Orders.Domain.Jobs;

/// <summary>
/// Where one garment job stands in its own lifecycle.
/// </summary>
/// <remarks>
/// <para>
/// The seven stored statuses of <c>docs/prd/state-transitions.md</c> section 3 and plan Section 8.
/// <strong>Phases are not statuses.</strong> A phase lives <em>inside</em> <see cref="InProduction"/> and is
/// instantiated from the workflow version pinned at start of production, so cutting, stitching and finishing
/// move a phase row and never this enumeration (INV-JOB-02, INV-JOB-03). A screen that shows "at the finishing
/// table" is rendering a phase over a job that is, to this type, simply in production.
/// </para>
/// <para>
/// <strong>There is no delete.</strong> A garment that will not be made is <see cref="Cancelled"/> and stays
/// readable, because an invoice line, a stock reservation and a custody chain all name it (G-5).
/// </para>
/// </remarks>
public enum GarmentJobStatus
{
    /// <summary>
    /// Created inside the order confirmation transaction, with its measurement, design and price snapshots
    /// frozen. Irreversible: there is no command that un-confirms a job (state-transitions.md section 7).
    /// </summary>
    Confirmed = 0,

    /// <summary>
    /// The workflow version is pinned and phases exist. Irreversible: order revision is refused from here
    /// (INV-ORD-05, INV-JOB-02), and the only route afterwards is an alteration request.
    /// </summary>
    InProduction = 1,

    /// <summary>
    /// Waiting on a recorded hold with a reason code and an approval. The ready gate is closed while it stands,
    /// because <c>NoOpenHold</c> is one of its six predicates (state-transitions.md section 9.1).
    /// </summary>
    OnHold = 2,

    /// <summary>
    /// Every ready-gate predicate passed.
    /// </summary>
    /// <remarks>
    /// Written by the gate alone. <c>docs/prd/raci.md</c> row 16 is explicit that no role, however senior, can
    /// declare a garment ready, which is INV-JOB-07; the type system carries that here, because the only way to
    /// reach this value is a <see cref="ReadyGateOutcome"/>, and only <see cref="ReadyGate"/> can make one.
    /// </remarks>
    Ready = 3,

    /// <summary>
    /// Handed over at the door.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The handover itself is irreversible: a gate recomputation can never pull back a garment that has left
    /// (state-transitions.md section 7), and the remedy for a delivery that did not happen is a failed or
    /// returned delivery recorded by Custody.
    /// </para>
    /// <para>
    /// <strong>It is not the end of the lifecycle.</strong> Section 3.2 draws one more edge out of it —
    /// "Delivered | Alteration requested and accepted — REASON | In production" — and section 3's diagram draws
    /// it too. That is a new commitment rather than an undo: the original job's history is never rewritten, and
    /// price and due-date decisions are taken and communicated before it. No command in this module writes it
    /// yet, because the alteration request, its decision and its billing intent are <strong>issue #34</strong>'s
    /// (<c>docs/IMPLEMENTATION_PLAN.md</c>, <c>feat/e06-f03-qc-rework-alteration-hold-cancel</c>);
    /// <see cref="Tailor360.Modules.Orders.Domain.Orders.OrderStatus.Delivered"/> names the same route at order
    /// level, and the two must not be left saying different things about one lifecycle.
    /// </para>
    /// </remarks>
    Delivered = 4,

    /// <summary>
    /// Declared because plan Section 8 stores it, and deliberately unreachable.
    /// </summary>
    /// <remarks>
    /// <strong>SQ-01 has not settled what closes a delivered job</strong> (state-transitions.md section 10,
    /// proposed and to be confirmed). Until it is decided, <em>delivered is the last automatic state</em>, so no
    /// command in this module writes this value. It is nonetheless handled everywhere a status could be read back
    /// from the database, which makes adding the transition later additive rather than a rewrite.
    /// </remarks>
    Closed = 5,

    /// <summary>
    /// Cancelled with a configured reason code and a reason.
    /// </summary>
    /// <remarks>
    /// There is no un-cancel and this value has no outbound edge. <strong>SQ-03</strong>'s interim position
    /// (state-transitions.md section 10, proposed and to be confirmed) is that reversing a cancellation requires a
    /// new order, and that <c>job-reopened</c>, when it arrives with issue #34, applies to a delivered or closed
    /// job rather than to a cancelled one.
    /// </remarks>
    Cancelled = 6,
}
