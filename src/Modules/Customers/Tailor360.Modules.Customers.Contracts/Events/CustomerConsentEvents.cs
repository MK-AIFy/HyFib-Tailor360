using Tailor360.Modules.Customers.Contracts.Consent;
using Tailor360.Platform.Abstractions.Events;

namespace Tailor360.Modules.Customers.Contracts.Events;

/// <summary>
/// A customer answered about one consent purpose.
/// </summary>
/// <remarks>
/// <para>
/// <strong>The payload says what changed, not what she said in full.</strong>
/// <c>docs/nfr/data-classification.md</c> section 5.3 classifies consent records as personal and says
/// of the one consumer that reads them at speed: "Notifications reads it through <c>IConsentQuery</c>
/// and never copies it." An outbox row is a copy — it is written to a table, read by every registered
/// handler, and outlives the moment it described. So the event carries the purpose, the outcome and
/// the wording version, which section 5.3 already permits to be recorded outside the record ("the
/// consent decision (purpose, outcome, wording version) is audited"), and a consumer that needs
/// anything more asks <see cref="IConsentQuery"/> for it.
/// </para>
/// <para>
/// What that deliberately excludes is <c>source</c> — "counter, verbal", or whatever the member of
/// staff typed. It is free text, and <c>docs/architecture/conventions.md</c> section 5.5 admits
/// "identifiers, codes, statuses, timestamps, amounts and branch codes only" to a payload. It is on
/// the record, and <see cref="ConsentState.Source"/> hands it to a consumer approved to read it.
/// </para>
/// <para>
/// <strong>A withdrawal is <see cref="ConsentWithdrawn"/>, not this event with a different status.</strong>
/// A withdrawal is the one a consumer must act on — Media stops relying on the record, Notifications
/// stops sending — and folding it in here would mean every such consumer subscribing to every answer
/// and filtering on a field. A consumer that forgets that filter keeps sending. It is the same split,
/// for the same reason, that <c>ConsentHandler</c> already makes between its two audit actions.
/// </para>
/// <para>
/// <strong>The aggregate is the customer</strong>, not the consent record. The dispatcher preserves
/// publication order per aggregate, and the order that matters is hers; per-record ordering would
/// guarantee nothing, every record being its own aggregate of exactly one event.
/// </para>
/// <para>
/// <strong>That ordering has a limit worth knowing before you rely on it.</strong> It holds over
/// messages that are already committed: the claim excludes any message whose aggregate has an older
/// unprocessed one, so no number of dispatchers can reorder them. It does <em>not</em> serialise the
/// writers. Two counters answering for the same customer at once each stamp <c>OccurredAt</c> before
/// they save, so the one that stamps later can commit first, be delivered, and be followed by the
/// earlier-stamped one — the dispatcher cannot rank a row it cannot yet see. Nothing in this module
/// locks a customer while an answer is recorded.
/// </para>
/// <para>
/// So a consumer de-duplicates on <see cref="IIntegrationEvent.EventId"/> and treats
/// <see cref="IConsentQuery"/> as the authority on where she stands, rather than reconstructing it
/// from the order events arrived in. That is what <c>docs/nfr/data-classification.md</c> section 5.3
/// already requires of the consumer that matters — "Notifications reads it through
/// <c>IConsentQuery</c> and never copies it" — and it is the same reason these payloads are thin.
/// Closing the gap properly means serialising the writers per customer, which is a change to the
/// module's concurrency model rather than to these types.
/// </para>
/// </remarks>
/// <param name="EventId">Identity of this occurrence.</param>
/// <param name="OccurredAt">When the answer was recorded, in UTC.</param>
/// <param name="AggregateId">The customer, whose answers are ordered against each other.</param>
/// <param name="OrganisationId">The organisation the customer belongs to.</param>
/// <param name="RecordId">
/// The consent record this event announces. Media (#31) stores it against every photograph it keeps,
/// so a later question about why an image exists has an answer that names the moment somebody agreed.
/// </param>
/// <param name="PurposeKey">The purpose, by its stable key.</param>
/// <param name="Status">
/// What she said: <see cref="ConsentStatus.Granted"/> or <see cref="ConsentStatus.Declined"/>. Never
/// <see cref="ConsentStatus.Withdrawn"/>, which has its own event, and never
/// <see cref="ConsentStatus.NeverAsked"/>, which is the absence of an answer and cannot be one.
/// </param>
/// <param name="WordingVersion">
/// The version of the wording she was read. It is what makes the answer evidence rather than a flag,
/// and a consumer comparing it against the current version is how a shop finds who to ask again.
/// </param>
/// <param name="BranchId">The branch the answer was taken at, where it was taken at one.</param>
public sealed record ConsentRecorded(
    Guid EventId,
    DateTimeOffset OccurredAt,
    Guid AggregateId,
    Guid OrganisationId,
    Guid RecordId,
    string PurposeKey,
    ConsentStatus Status,
    int WordingVersion,
    Guid? BranchId)
    : IntegrationEvent(EventId, OccurredAt, AggregateId)
{
    /// <summary>The wire name. Subscribe by this constant rather than by a literal.</summary>
    public const string Type = "customers.consent-recorded.v1";

    /// <inheritdoc />
    public override string EventType => Type;
}

/// <summary>
/// A customer withdrew consent she had given.
/// </summary>
/// <remarks>
/// <para>
/// Separate from <see cref="ConsentRecorded"/> because this is the one a consumer must act on, and
/// acting on it is not optional: section 5.3 of <c>docs/nfr/data-classification.md</c> says the record
/// exists "to honour a withdrawal immediately". A consumer subscribed to this event and to nothing
/// else has subscribed to exactly the fact it has to react to.
/// </para>
/// <para>
/// There is no status field. The event type is the status, and a payload that could say otherwise
/// would be a payload a consumer has to check.
/// </para>
/// <para>
/// It carries the same identifiers, and the same reasons for what it leaves out, as
/// <see cref="ConsentRecorded"/> — the purpose, the record and the wording version she had agreed
/// under, and not the free-text source.
/// </para>
/// </remarks>
/// <param name="EventId">Identity of this occurrence.</param>
/// <param name="OccurredAt">When the withdrawal was recorded, in UTC.</param>
/// <param name="AggregateId">The customer, whose answers are ordered against each other.</param>
/// <param name="OrganisationId">The organisation the customer belongs to.</param>
/// <param name="RecordId">
/// The withdrawal record. It is a record in its own right — withdrawing appends a row rather than
/// changing the one that granted — so this identifier is the withdrawal, not the grant it revokes.
/// </param>
/// <param name="PurposeKey">The purpose she is withdrawing from, by its stable key.</param>
/// <param name="WordingVersion">The wording version the withdrawal was recorded against.</param>
/// <param name="BranchId">The branch the withdrawal was taken at, where it was taken at one.</param>
public sealed record ConsentWithdrawn(
    Guid EventId,
    DateTimeOffset OccurredAt,
    Guid AggregateId,
    Guid OrganisationId,
    Guid RecordId,
    string PurposeKey,
    int WordingVersion,
    Guid? BranchId)
    : IntegrationEvent(EventId, OccurredAt, AggregateId)
{
    /// <summary>The wire name. Subscribe by this constant rather than by a literal.</summary>
    public const string Type = "customers.consent-withdrawn.v1";

    /// <inheritdoc />
    public override string EventType => Type;
}
