using Tailor360.Modules.Orders.Domain.Snapshots;

namespace Tailor360.Modules.Orders.Domain.Orders;

/// <summary>
/// One priced, re-validated position of an order.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Append-only.</strong> <c>docs/prd/state-transitions.md</c> section 2.1 says a revision never
/// rewrites the previous snapshot — it appends a new one. So this is a row rather than a field, and the
/// sequence of rows is the whole history of what the order said and when. Revision
/// <see cref="Order.FirstRevisionNumber"/> is written by the confirmation itself, which is what makes
/// that statement literally true: there is no position the order has ever held that is not a row here.
/// </para>
/// <para>
/// Being append-only, it carries no concurrency token and no updated-at pair (convention: child entities
/// of an aggregate are append-only and are protected by a database trigger, not by <c>xmin</c>). It has
/// no mutator at all — a mistaken revision is corrected by a further revision, with its own reason.
/// </para>
/// <para>
/// The totals here are a snapshot for display and printing. Billing's <c>IFinancialTotalsQuery</c> holds
/// the authoritative money position (INV-ORD-07), and this module performs no money arithmetic on them.
/// </para>
/// </remarks>
public sealed class OrderRevision
{
    /// <summary>
    /// The longest reason the column holds. Matches the reason length used across this module and the
    /// precedent set by <c>CustomerExport.MaximumReasonLength</c>.
    /// </summary>
    public const int MaximumReasonLength = 500;

    private OrderRevision()
    {
        // The persistence layer materialises instances through this constructor.
        Totals = null!;
    }

    private OrderRevision(
        Guid id,
        Guid orderId,
        int revisionNumber,
        string? reason,
        PriceSnapshot totals,
        DateOnly dueDate,
        Guid? supersededEstimateId,
        DateTimeOffset recordedAt,
        Guid? recordedBy)
    {
        Id = id;
        OrderId = orderId;
        RevisionNumber = revisionNumber;
        Reason = reason;
        Totals = totals;
        DueDate = dueDate;
        SupersededEstimateId = supersededEstimateId;
        RecordedAt = recordedAt;
        RecordedBy = recordedBy;
    }

    /// <summary>Identity of this revision row. A UUIDv7.</summary>
    public Guid Id { get; private set; }

    /// <summary>The order this position belongs to.</summary>
    public Guid OrderId { get; private set; }

    /// <summary>One for the confirmation, then two, three and so on for each authorised revision.</summary>
    public int RevisionNumber { get; private set; }

    /// <summary>
    /// Why the order was revised.
    /// </summary>
    /// <remarks>
    /// Null on revision one, because a confirmation is not a revision and
    /// <c>docs/prd/state-transitions.md</c> section 8 demands a reason only for the revise command.
    /// </remarks>
    public string? Reason { get; private set; }

    /// <summary>What the order was priced at as at this revision.</summary>
    public PriceSnapshot Totals { get; private set; }

    /// <summary>The promised date as at this revision, evaluated in the branch timezone by the caller.</summary>
    public DateOnly DueDate { get; private set; }

    /// <summary>
    /// The estimate this revision superseded, where one was outstanding.
    /// </summary>
    /// <remarks>
    /// Null on revision one: at confirmation an outstanding estimate is <em>converted</em>, not
    /// superseded (state-transitions.md section 2.2), and the order records it as
    /// <see cref="Order.EstimateId"/> instead.
    /// </remarks>
    public Guid? SupersededEstimateId { get; private set; }

    /// <summary>When this position was recorded, in UTC.</summary>
    public DateTimeOffset RecordedAt { get; private set; }

    /// <summary>Who recorded it, where a person did.</summary>
    public Guid? RecordedBy { get; private set; }

    /// <summary>
    /// Appends one position to an order's history.
    /// </summary>
    /// <remarks>
    /// Internal, so only <see cref="Order"/> in this assembly can create one. A revision row written by
    /// anything but the aggregate would be a second writer of the order's price position, and the
    /// append-only guarantee above would hold only by convention.
    /// </remarks>
    /// <param name="id">Identity, from <c>IIdGenerator</c>.</param>
    /// <param name="orderId">The order.</param>
    /// <param name="revisionNumber">One at confirmation, then one more for each revision.</param>
    /// <param name="reason">Why the order was revised, or null on the confirmation's own revision.</param>
    /// <param name="totals">The priced result as at this revision.</param>
    /// <param name="dueDate">The promised date as at this revision.</param>
    /// <param name="supersededEstimateId">The estimate retired by this revision, where one was outstanding.</param>
    /// <param name="recordedAt">The instant, from <c>IClock</c>.</param>
    /// <param name="recordedBy">The actor.</param>
    /// <returns>The recorded position.</returns>
    internal static OrderRevision Record(
        Guid id,
        Guid orderId,
        int revisionNumber,
        string? reason,
        PriceSnapshot totals,
        DateOnly dueDate,
        Guid? supersededEstimateId,
        DateTimeOffset recordedAt,
        Guid? recordedBy)
        => new(
            id,
            orderId,
            revisionNumber,
            reason,
            totals,
            dueDate,
            supersededEstimateId,
            recordedAt,
            recordedBy);
}
