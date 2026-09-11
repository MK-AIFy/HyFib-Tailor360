namespace Tailor360.Modules.Orders.Domain.Orders;

/// <summary>
/// Where an order stands in its lifecycle.
/// </summary>
/// <remarks>
/// <para>
/// The seven values are the stored order statuses of plan Section 8 and
/// <c>docs/prd/state-transitions.md</c> section 2. They are a lifecycle position, not a summary of
/// everything true about the order: a held garment, an open rework and an unpaid balance are all
/// visible on an order screen without any of them being a status here.
/// </para>
/// <para>
/// <strong>Two words in common shop-floor use are deliberately absent.</strong> <em>Dispatched</em> is a
/// custody state, not an order status — the order stays <see cref="Ready"/> from the moment the delivery
/// team takes the parcel until the doorstep confirmation moves it to <see cref="Delivered"/>
/// (state-transitions.md section 4). <em>On hold</em> is a garment job status and a holds record with a
/// reason and an approval; an order shown as "on hold" on a screen is a derived display over its jobs'
/// open holds (state-transitions.md section 3). Adding either as a status here would have put two
/// writers on one field.
/// </para>
/// <para>
/// The order's status is derived from its garment jobs rather than commanded directly — see
/// <see cref="Order.RecomputeStatus"/> and open decision <strong>SQ-02</strong>. The one exception is
/// <see cref="Cancelled"/>, which only the explicit, reasoned <see cref="Order.Cancel"/> writes.
/// </para>
/// </remarks>
public enum OrderStatus
{
    /// <summary>
    /// Work in progress at the counter, before any commitment has been made.
    /// </summary>
    /// <remarks>
    /// No <see cref="Order"/> instance is ever created in this state: the lifecycle position is held by
    /// <c>OrderDraft</c>, and an order row exists only from confirmation onwards. The value is declared
    /// because it is a stored status of the lifecycle (plan Section 8) and a screen switching on the
    /// lifecycle needs the whole set.
    /// </remarks>
    Draft = 0,

    /// <summary>
    /// The commitment is made: snapshots are frozen and display numbers allocated.
    /// </summary>
    /// <remarks>
    /// Irreversible (state-transitions.md section 7). The only routes onwards are a revision before
    /// production, production itself, or a reasoned cancellation.
    /// </remarks>
    Confirmed = 1,

    /// <summary>At least one garment job has entered production.</summary>
    /// <remarks>
    /// The interim position of <strong>SQ-02</strong>, which is not settled. Order revision is refused
    /// from this moment (INV-ORD-05, INV-JOB-02).
    /// </remarks>
    InProduction = 2,

    /// <summary>
    /// The job set the branch dispatch policy requires has passed the ready-for-delivery gate.
    /// </summary>
    /// <remarks>
    /// Written by the gate's outcome alone and never by a member of staff (INV-JOB-07,
    /// <c>docs/prd/raci.md</c> row 16). Which job set the policy requires is the part of
    /// <strong>SQ-02</strong> that is still open, so it arrives as a <see cref="ReadyAggregation"/>
    /// parameter rather than being guessed here.
    /// </remarks>
    Ready = 3,

    /// <summary>Every non-cancelled garment job has been handed over at the door.</summary>
    /// <remarks>
    /// The interim position of <strong>SQ-02</strong>. Doorstep confirmation is irreversible
    /// (state-transitions.md section 7); an accepted post-delivery alteration nevertheless returns the
    /// order to <see cref="InProduction"/>, which is a new commitment rather than an undo.
    /// </remarks>
    Delivered = 4,

    /// <summary>The order is finished and out of the working set.</summary>
    /// <remarks>
    /// Declared because plan Section 8 stores it, and deliberately unreachable. <strong>SQ-01</strong>
    /// has not settled what closes a delivered order, and state-transitions.md section 10 says that
    /// until it is decided, <em>delivered is the last automatic state</em>. No command in this module
    /// writes this value and <see cref="Order.RecomputeStatus"/> never produces it. It is nonetheless
    /// handled wherever a status could be read back from the database, so adding the transition later is
    /// additive.
    /// </remarks>
    Closed = 5,

    /// <summary>Cancelled by an explicit, reasoned, separately authorised command.</summary>
    /// <remarks>
    /// There is no un-cancel (state-transitions.md section 2.1). A customer who changes their mind again
    /// is served by a new order that may reuse the same measurement version. Cancelling every garment job
    /// does not reach this value — see <strong>SQ-04</strong> and <see cref="Order.CancelJob"/>.
    /// </remarks>
    Cancelled = 6,
}
