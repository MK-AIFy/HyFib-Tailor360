using Tailor360.Platform.Abstractions.Events;

namespace Tailor360.Modules.Orders.Contracts.Events;

/// <summary>
/// A garment's promised date moved.
/// </summary>
/// <remarks>
/// <para>
/// <strong><see cref="PreviousDueDate"/> is the field this event exists for.</strong> The due date is the one
/// fact the domain overwrites in place, so a consumer that missed an earlier message cannot reconstruct the
/// promise that moved — and Reporting #45 measures promised-against-actual turnaround, which is meaningless
/// without knowing which promise. Publishing it means the application layer reads the garment's due date
/// <em>before</em> calling <c>Order.Reschedule</c>, because the call has overwritten it by the time it returns.
/// </para>
/// <para>
/// <strong>Both dates are branch-local.</strong> They have already been evaluated in the branch timezone against
/// the branch working calendar by the caller (<c>docs/architecture/conventions.md</c> section 2.2); the domain
/// never asks a clock what today is (ARCH-014).
/// </para>
/// <para>
/// <strong>Only the garment's date moves.</strong> <c>Order.Reschedule</c> does not touch the order's own
/// promised date, so a consumer must not infer one from this event — a consumer that needs it asks
/// <c>IOrderSnapshotQuery</c>.
/// </para>
/// <para>
/// <strong>The reason is mandatory to the command and withheld from the payload.</strong> Renegotiating a promise
/// honestly means saying why, and the free text saying why is what a member of staff typed about a named person's
/// garment: it stays in Orders, as every other reason in this namespace does. No actor either — audit (ARCH-008)
/// is the authority on who.
/// </para>
/// </remarks>
/// <param name="EventId">Identity of this occurrence. Delivery is at least once; consumers de-duplicate on it.</param>
/// <param name="OccurredAt">When the rescheduling was recorded, in UTC.</param>
/// <param name="AggregateId">The garment job, ordered behind <see cref="GarmentJobCreated"/>.</param>
/// <param name="OrganisationId">The organisation the garment belongs to.</param>
/// <param name="BranchId">The branch whose workshop is making it.</param>
/// <param name="OrderId">The order the garment belongs to.</param>
/// <param name="GarmentJobNumber">The human reference, so a rack tag or a queue row can be named.</param>
/// <param name="DueDate">The new promised date, branch-local.</param>
/// <param name="PreviousDueDate">
/// The promise that was replaced, branch-local. Read from the garment before the transition, because the
/// transition overwrites it.
/// </param>
public sealed record GarmentJobRescheduled(
    Guid EventId,
    DateTimeOffset OccurredAt,
    Guid AggregateId,
    Guid OrganisationId,
    Guid BranchId,
    Guid OrderId,
    string GarmentJobNumber,
    DateOnly DueDate,
    DateOnly PreviousDueDate)
    : IntegrationEvent(EventId, OccurredAt, AggregateId)
{
    /// <summary>The wire name. Subscribe by this constant rather than by a literal.</summary>
    public const string Type = "orders.job-rescheduled.v1";

    /// <inheritdoc />
    public override string EventType => Type;
}
