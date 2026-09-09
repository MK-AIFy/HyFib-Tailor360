using Tailor360.Platform.Abstractions.Events;

namespace Tailor360.Modules.Catalog.Contracts.Events;

/// <summary>
/// A catalogue version stopped being offered for new work.
/// </summary>
/// <remarks>
/// Retirement stops new orders and nothing else. Jobs already in production run to dispatch on the
/// configuration they were pinned to, and the retired version stays fully readable so their job cards,
/// invoices and reports render exactly as they did (<c>docs/prd/category-hierarchy.md</c> section 7).
/// A consumer that cancels or re-routes work on this event has misread it.
/// </remarks>
/// <param name="EventId">Identity of this occurrence.</param>
/// <param name="OccurredAt">When the version was retired, in UTC.</param>
/// <param name="AggregateId">The catalogue version that was retired.</param>
/// <param name="OrganisationId">The organisation whose catalogue it is.</param>
/// <param name="VersionNumber">The version's number, for a message a person reads.</param>
public sealed record CatalogVersionRetired(
    Guid EventId,
    DateTimeOffset OccurredAt,
    Guid AggregateId,
    Guid OrganisationId,
    int VersionNumber)
    : IntegrationEvent(EventId, OccurredAt, AggregateId)
{
    /// <summary>The wire name. Subscribe by this constant rather than by a literal.</summary>
    public const string Type = "catalog.catalog-version-retired.v1";

    /// <inheritdoc />
    public override string EventType => Type;
}
