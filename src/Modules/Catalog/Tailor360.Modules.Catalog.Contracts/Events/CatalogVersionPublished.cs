using Tailor360.Platform.Abstractions.Events;

namespace Tailor360.Modules.Catalog.Contracts.Events;

/// <summary>
/// A catalogue version became the active configuration.
/// </summary>
/// <remarks>
/// <para>
/// Two kinds of consumer act on this. The first is any cache of the catalogue: reads are keyed by the
/// published version's identifier, so this event is what makes a stale entry unreachable rather than
/// what deletes it (decision D21). The second is anything that shows what is orderable — the intake
/// screens, the estimate builder — which re-reads after it arrives.
/// </para>
/// <para>
/// <strong>It changes nothing that is already under way.</strong> A garment job pins its catalogue
/// version at confirmation, so publishing never re-prices, re-routes or re-measures work in progress
/// (<c>docs/prd/category-hierarchy.md</c> section 7). A consumer that treats this as "re-evaluate the
/// open orders" has misread it.
/// </para>
/// <para>
/// The payload carries identifiers and a version number, and nothing about the categories themselves.
/// A consumer that needs the tree reads it through <c>ICatalogAvailabilityQuery</c>, which
/// re-authorises; copying the hierarchy into an event would make every subscriber a second, stale
/// catalogue.
/// </para>
/// </remarks>
/// <param name="EventId">Identity of this occurrence. Consumers de-duplicate on it.</param>
/// <param name="OccurredAt">When the version was published, in UTC.</param>
/// <param name="AggregateId">The catalogue version that was published.</param>
/// <param name="OrganisationId">The organisation whose catalogue it is.</param>
/// <param name="VersionNumber">The version's number, for a message a person reads.</param>
/// <param name="SupersededVersionId">
/// The version that was current until now, or null when this is the first. A consumer holding a cache
/// keyed by that identifier can drop it, though it does not have to: the key is unreachable either way.
/// </param>
public sealed record CatalogVersionPublished(
    Guid EventId,
    DateTimeOffset OccurredAt,
    Guid AggregateId,
    Guid OrganisationId,
    int VersionNumber,
    Guid? SupersededVersionId)
    : IntegrationEvent(EventId, OccurredAt, AggregateId)
{
    /// <summary>The wire name. Subscribe by this constant rather than by a literal.</summary>
    public const string Type = "catalog.version-published.v1";

    /// <inheritdoc />
    public override string EventType => Type;
}
