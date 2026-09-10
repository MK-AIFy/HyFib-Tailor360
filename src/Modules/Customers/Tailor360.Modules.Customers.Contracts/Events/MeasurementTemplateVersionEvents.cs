using Tailor360.Platform.Abstractions.Events;

namespace Tailor360.Modules.Customers.Contracts.Events;

/// <summary>
/// A measurement-template version became the one measurements are captured against.
/// </summary>
/// <remarks>
/// <para>
/// A catalogue service type references the <em>template</em>, never one of its versions, so publishing a version
/// changes what the counter asks for without touching a catalogue row
/// (<c>docs/prd/measurement-templates.md</c> section 2). Nothing downstream has to re-point anything.
/// </para>
/// <para>
/// <strong>What this is for is the other direction.</strong> A template with no published version strands every
/// catalogue service type that points at it — invariant INV-MTV-06 — and the two guards that protect that
/// invariant sit in different modules, so they can each observe the other's pre-write state and both commit.
/// Publishing a version is the event that can <em>heal</em> such a breach, which is why it is published at all:
/// Catalog reconciles on it and closes a breach that no longer holds.
/// </para>
/// <para>
/// It carries identifiers, a code and a version number, and nothing about the fields. A consumer that needs them
/// reads through <c>IMeasurementTemplateQuery</c>, which answers from the live template; copying a field set into
/// an event would make every subscriber a second, stale template.
/// </para>
/// </remarks>
/// <param name="EventId">Identity of this occurrence. Consumers de-duplicate on it.</param>
/// <param name="OccurredAt">When the version was published, in UTC.</param>
/// <param name="AggregateId">The template, not the version — it is what a catalogue reference names.</param>
/// <param name="OrganisationId">The organisation whose template it is.</param>
/// <param name="TemplateVersionId">The version that was published.</param>
/// <param name="TemplateCode">The template's stable machine key, such as <c>MT_BLOUSE_PATTERN</c>.</param>
/// <param name="VersionNumber">The version's number, for a message a person reads.</param>
/// <param name="SupersededVersionId">
/// The version this one replaced, retired by the same publication, or null when it is the first.
/// </param>
public sealed record MeasurementTemplateVersionPublished(
    Guid EventId,
    DateTimeOffset OccurredAt,
    Guid AggregateId,
    Guid OrganisationId,
    Guid TemplateVersionId,
    string TemplateCode,
    int VersionNumber,
    Guid? SupersededVersionId)
    : IntegrationEvent(EventId, OccurredAt, AggregateId)
{
    /// <summary>The wire name. Subscribe by this constant rather than by a literal.</summary>
    public const string Type = "customers.measurement-template-version-published.v1";

    /// <inheritdoc />
    public override string EventType => Type;
}

/// <summary>
/// A measurement-template version stopped taking new captures.
/// </summary>
/// <remarks>
/// <para>
/// Retirement invalidates nothing. Measurements already taken render through the version they were captured
/// under, forever (<c>docs/prd/measurement-templates.md</c> section 11); what stops is new capture against it.
/// </para>
/// <para>
/// <strong>This is the event that can strand a catalogue.</strong> Retiring a template's last published version
/// leaves every published service type pointing at it with nothing to measure by. The retirement command refuses
/// exactly that — but it asks Catalog through a contract and then writes to its own schema, so a catalogue
/// publication committing in the same window can be validated against a template that is retired by the time it
/// lands. INV-MTV-06 is therefore reconciled by this event rather than guaranteed by the guard alone, which is
/// what <strong>G-6</strong> promises for every cross-module reference.
/// </para>
/// <para>
/// <see cref="TemplateStillPublishable"/> is the one piece of derived state on the payload, and it is here because
/// it is the fact the event is <em>about</em>: retiring a superseded version is routine and strands nothing,
/// while retiring the last published one is the case worth reconciling. A consumer still re-reads before acting —
/// the payload says what was true when the write committed, and a reconciliation asks again.
/// </para>
/// </remarks>
/// <param name="EventId">Identity of this occurrence. Consumers de-duplicate on it.</param>
/// <param name="OccurredAt">When the version was retired, in UTC.</param>
/// <param name="AggregateId">The template, not the version — it is what a catalogue reference names.</param>
/// <param name="OrganisationId">The organisation whose template it is.</param>
/// <param name="TemplateVersionId">The version that was retired.</param>
/// <param name="TemplateCode">The template's stable machine key.</param>
/// <param name="VersionNumber">The version's number, for a message a person reads.</param>
/// <param name="TemplateStillPublishable">
/// Whether the template still has a published version after this retirement. False is the case that can strand a
/// catalogue.
/// </param>
public sealed record MeasurementTemplateVersionRetired(
    Guid EventId,
    DateTimeOffset OccurredAt,
    Guid AggregateId,
    Guid OrganisationId,
    Guid TemplateVersionId,
    string TemplateCode,
    int VersionNumber,
    bool TemplateStillPublishable)
    : IntegrationEvent(EventId, OccurredAt, AggregateId)
{
    /// <summary>The wire name. Subscribe by this constant rather than by a literal.</summary>
    public const string Type = "customers.measurement-template-version-retired.v1";

    /// <inheritdoc />
    public override string EventType => Type;
}
