using Tailor360.Platform.Abstractions.Events;

namespace Tailor360.Modules.Customers.Contracts.Events;

/// <summary>
/// Two customer records were judged to be one person, and one of them survived.
/// </summary>
/// <remarks>
/// <para>
/// <strong>This is the event other modules have to act on.</strong> Customers re-points what it owns
/// inside the merge transaction; everything else — orders, invoices, custody, notifications — learns
/// here that a customer identifier it holds now names a record that no longer stands, and re-points
/// its own references to <see cref="AggregateId"/>.
/// <c>docs/architecture/invariants.md</c> INV-CUS-04 draws the line: references are re-pointed,
/// <em>snapshots are never rewritten</em>. An invoice that names the customer as she was written at
/// the time keeps saying so, because it is a record of what was agreed and not a view of current
/// state.
/// </para>
/// <para>
/// <strong>The aggregate is the survivor, not the record that went away.</strong> Per-aggregate
/// ordering is what a consumer gets from the outbox, and the useful guarantee is that this event is
/// ordered against the survivor's other events — the record that carries on. The merged record has no
/// further events to be ordered against.
/// </para>
/// <para>
/// <strong>The payload carries identifiers and nothing else.</strong> No name, no number, no telephone
/// number, and not the reason: <c>docs/architecture/conventions.md</c> section 5.5 admits identifiers,
/// codes, statuses, timestamps, amounts and branch codes, and a reason is free text a member of staff
/// typed about a named person. A subscriber that needs to show what happened asks
/// <c>ICustomerSnapshotQuery</c>, which re-authorises the read; the reason stays on the merge record,
/// where the erasure workflow can reach it.
/// </para>
/// <para>
/// <strong>It is not a merge instruction.</strong> A consumer that has never heard of either record
/// does nothing. Delivery is at least once, so re-pointing must be written to be safe on a second
/// delivery — pointing a reference at a record it already names is the ordinary case, not an error.
/// </para>
/// </remarks>
/// <param name="EventId">Identity of this occurrence. Consumers de-duplicate on it.</param>
/// <param name="OccurredAt">When the merge was recorded, in UTC.</param>
/// <param name="AggregateId">The customer that survived, and answers for the person from now on.</param>
/// <param name="OrganisationId">The organisation both records belong to.</param>
/// <param name="MergedCustomerId">
/// The customer that was folded in. A reference a consumer holds to this identifier should now name
/// <see cref="AggregateId"/> instead, except where it is part of a frozen snapshot.
/// </param>
/// <param name="MergeId">
/// The merge decision, so a consumer asking "on whose authority" has something to quote back. The
/// decision itself — who, when, why — stays in Customers.
/// </param>
/// <param name="BranchId">The branch the decision was taken at, or null where the session had none.</param>
public sealed record CustomerMerged(
    Guid EventId,
    DateTimeOffset OccurredAt,
    Guid AggregateId,
    Guid OrganisationId,
    Guid MergedCustomerId,
    Guid MergeId,
    Guid? BranchId)
    : IntegrationEvent(EventId, OccurredAt, AggregateId)
{
    /// <summary>The wire name. Subscribe by this constant rather than by a literal.</summary>
    public const string Type = "customers.customer-merged.v1";

    /// <inheritdoc />
    public override string EventType => Type;
}
