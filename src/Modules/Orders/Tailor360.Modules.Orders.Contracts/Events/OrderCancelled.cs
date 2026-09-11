using Tailor360.Platform.Abstractions.Events;

namespace Tailor360.Modules.Orders.Contracts.Events;

/// <summary>
/// A confirmed order was cancelled.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Cancelling the order does not cancel its garments, and this payload says exactly that by
/// omission.</strong> <c>Order.Cancel</c> does not touch a single garment job, so naming them here would state
/// something the transition did not do. A consumer that needs to know where each garment stands asks
/// <c>IOrderSnapshotQuery</c>; a garment cancelled in its own right has its own event,
/// <see cref="GarmentJobCancelled"/>.
/// </para>
/// <para>
/// <strong>Only the code travels, never the reason.</strong> The free-text cancellation reason is what a member
/// of staff typed about a named person's order, which <c>docs/architecture/conventions.md</c> section 5.5 does
/// not admit to a payload and which the erasure workflow has to be able to redact. It stays on the order. This is
/// the same line <c>docs/integration/events/README.md</c> section 3 takes about a merge reason.
/// </para>
/// <para>
/// <strong>No actor and no approval.</strong> Who cancelled and under whose authority is audit's answer
/// (ARCH-008), and staff identifiers on an outbox row would be personal data fanned out to every registered
/// handler and outliving the moment it described.
/// </para>
/// <para>
/// <strong>The prohibited states the command was checked against are not published either.</strong> They are the
/// application's input to the decision, not a fact about the order.
/// </para>
/// </remarks>
/// <param name="EventId">Identity of this occurrence. Delivery is at least once; consumers de-duplicate on it.</param>
/// <param name="OccurredAt">When the cancellation was recorded, in UTC.</param>
/// <param name="AggregateId">The order, so the cancellation is ordered after everything else about it.</param>
/// <param name="OrganisationId">The organisation the order belongs to.</param>
/// <param name="BranchId">The branch the order was taken at.</param>
/// <param name="CustomerId">The customer the order was for. An identifier and nothing more.</param>
/// <param name="OrderNumber">The human reference, so a message about the cancellation can name the order.</param>
/// <param name="ReasonCode">
/// The configured cancellation reason, by its code. Never null and never blank — <c>Order.Cancel</c> refuses a
/// cancellation without one — and never constrained to a fixed set here, because the vocabulary is configuration
/// that issue #34 settles rather than an enumeration this contract may freeze.
/// </param>
public sealed record OrderCancelled(
    Guid EventId,
    DateTimeOffset OccurredAt,
    Guid AggregateId,
    Guid OrganisationId,
    Guid BranchId,
    Guid CustomerId,
    string OrderNumber,
    string ReasonCode)
    : IntegrationEvent(EventId, OccurredAt, AggregateId)
{
    /// <summary>The wire name. Subscribe by this constant rather than by a literal.</summary>
    public const string Type = "orders.order-cancelled.v1";

    /// <inheritdoc />
    public override string EventType => Type;
}
