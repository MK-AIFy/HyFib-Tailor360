using Tailor360.Platform.Abstractions.Events;

namespace Tailor360.Modules.Billing.Contracts.Events;

/// <summary>A single-use dispatch exception was approved (plan lines 1908-1916, <c>docs/prd/exceptions.md</c> EX-10).</summary>
public sealed record DispatchExceptionApproved(
    Guid EventId, DateTimeOffset OccurredAt, Guid AggregateId,
    Guid OrganisationId, Guid BranchId, Guid OrderId, Guid ApprovedBy, string PolicyVersion,
    decimal MaxOutstandingAmount, string Currency, DateTimeOffset ExpiresAt)
    : IntegrationEvent(EventId, OccurredAt, AggregateId)
{
    /// <summary>The wire name.</summary>
    public const string Type = "billing.dispatch-exception-approved.v1";

    /// <inheritdoc />
    public override string EventType => Type;
}

/// <summary>A dispatch exception was consumed by the dispatch authorisation it covered, exactly once.</summary>
public sealed record DispatchExceptionConsumed(
    Guid EventId, DateTimeOffset OccurredAt, Guid AggregateId,
    Guid OrganisationId, Guid BranchId, Guid OrderId, Guid ConsumedBy)
    : IntegrationEvent(EventId, OccurredAt, AggregateId)
{
    /// <summary>The wire name.</summary>
    public const string Type = "billing.dispatch-exception-consumed.v1";

    /// <inheritdoc />
    public override string EventType => Type;
}

/// <summary>A dispatch exception reached its expiry without ever being consumed (EX-15).</summary>
public sealed record DispatchExceptionExpired(
    Guid EventId, DateTimeOffset OccurredAt, Guid AggregateId,
    Guid OrganisationId, Guid BranchId, Guid OrderId)
    : IntegrationEvent(EventId, OccurredAt, AggregateId)
{
    /// <summary>The wire name.</summary>
    public const string Type = "billing.dispatch-exception-expired.v1";

    /// <inheritdoc />
    public override string EventType => Type;
}
