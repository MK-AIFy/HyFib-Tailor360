using Tailor360.Modules.Customers.Contracts.Preferences;
using Tailor360.Platform.Abstractions.Events;

namespace Tailor360.Modules.Customers.Contracts.Events;

/// <summary>
/// A customer's communication preference was recorded or replaced.
/// </summary>
/// <remarks>
/// <para>
/// <strong>The payload carries no preference.</strong> Not the channels, not the language, not the
/// quiet window. <c>docs/nfr/data-classification.md</c> section 5.3 covers "allowed channels; language
/// preference; quiet hours" in the same **Personal** row as the consent record, and says of the module
/// that acts on them: "Notifications reads it through <c>IConsentQuery</c> and never copies it." An
/// outbox row is a copy that fans out to every registered handler and outlives the moment. So this
/// event says only that the answer changed, and a consumer re-reads
/// <see cref="ICommunicationPreferenceQuery"/> — which is what #47 does before every send anyway,
/// because a preference evaluated at send time is the only one that is current.
/// </para>
/// <para>
/// That makes the event a cache invalidation rather than a data feed, and it is worth being plain that
/// this is a choice: a subscriber that genuinely needs the content is a change to who may access it
/// under section 5.3, and therefore a new major version and an approval, not a field added within
/// this one.
/// </para>
/// <para>
/// <strong>There is one event for both cases.</strong> A first preference and a replacement are the
/// same fact to a consumer — what she accepts is now different from what the consumer last read — and
/// splitting them would only invite a consumer to handle one.
/// </para>
/// </remarks>
/// <param name="EventId">Identity of this occurrence.</param>
/// <param name="OccurredAt">When the preference was recorded or replaced, in UTC.</param>
/// <param name="AggregateId">The customer, whose changes are ordered against each other.</param>
/// <param name="OrganisationId">The organisation the customer belongs to.</param>
/// <param name="WasFirstRecorded">
/// True when nobody had recorded a preference for this customer before. It is a fact about the
/// system's knowledge rather than about her, and it is the difference between a consumer that had
/// nothing cached and one whose cache is now stale.
/// </param>
public sealed record PreferencesChanged(
    Guid EventId,
    DateTimeOffset OccurredAt,
    Guid AggregateId,
    Guid OrganisationId,
    bool WasFirstRecorded)
    : IntegrationEvent(EventId, OccurredAt, AggregateId)
{
    /// <summary>The wire name. Subscribe by this constant rather than by a literal.</summary>
    public const string Type = "customers.preferences-changed.v1";

    /// <inheritdoc />
    public override string EventType => Type;
}
