using Tailor360.Platform.Abstractions.Events;

namespace Tailor360.Modules.Orders.Contracts.Events;

/// <summary>
/// A garment started being made.
/// </summary>
/// <remarks>
/// <para>
/// <strong>This is the moment the workflow version is pinned, and the payload exists mostly to say which
/// one.</strong> INV-JOB-02: the version is resolved and pinned at start-production, a later published version
/// never migrates a running job, and an order revision is refused from here on. A consumer that reports on how a
/// garment was actually made — Reporting #45's workload and turnaround projections — needs the version that was
/// in force, not whichever is current when it asks.
/// </para>
/// <para>
/// <strong><see cref="WorkflowDefinitionId"/> is repeated from <see cref="GarmentJobCreated"/> on
/// purpose.</strong> It saves every consumer a join back to the creation event to answer the commonest question
/// about this one, and a definition never changes for a garment once confirmed.
/// </para>
/// <para>
/// <strong>No prerequisites, no phase.</strong> The satisfied <c>finish_before</c> prerequisites the command was
/// checked against are an application input the domain verifies and does not store — a fact about the call, not
/// about the garment. Phase state is issue #33's, and <c>orders.job-phase-changed.v1</c> is not declared here,
/// because the repository's rule is that an event lands with the issue that raises it.
/// </para>
/// <para>
/// <strong>No actor.</strong> Who started the garment is audit's answer (ARCH-008).
/// </para>
/// </remarks>
/// <param name="EventId">Identity of this occurrence. Delivery is at least once; consumers de-duplicate on it.</param>
/// <param name="OccurredAt">When production started, in UTC.</param>
/// <param name="AggregateId">The garment job, ordered behind <see cref="GarmentJobCreated"/>.</param>
/// <param name="OrganisationId">The organisation the garment belongs to.</param>
/// <param name="BranchId">The branch whose workshop is making it.</param>
/// <param name="OrderId">The order the garment belongs to.</param>
/// <param name="GarmentJobNumber">The human reference, so a rack tag or a queue row can be named.</param>
/// <param name="WorkflowDefinitionId">The workflow definition recorded at confirmation.</param>
/// <param name="WorkflowVersionId">
/// The version pinned at this moment, which never migrates (INV-JOB-02). Never null and never empty:
/// <c>Order.StartProduction</c> refuses an empty one and pins it before returning.
/// </param>
public sealed record GarmentJobEnteredProduction(
    Guid EventId,
    DateTimeOffset OccurredAt,
    Guid AggregateId,
    Guid OrganisationId,
    Guid BranchId,
    Guid OrderId,
    string GarmentJobNumber,
    Guid WorkflowDefinitionId,
    Guid WorkflowVersionId)
    : IntegrationEvent(EventId, OccurredAt, AggregateId)
{
    /// <summary>The wire name. Subscribe by this constant rather than by a literal.</summary>
    public const string Type = "orders.job-entered-production.v1";

    /// <inheritdoc />
    public override string EventType => Type;
}
