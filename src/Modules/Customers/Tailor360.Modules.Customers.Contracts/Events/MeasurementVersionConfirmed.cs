using Tailor360.Platform.Abstractions.Events;

namespace Tailor360.Modules.Customers.Contracts.Events;

/// <summary>
/// A customer's measurements were confirmed against a template version.
/// </summary>
/// <remarks>
/// <para>
/// Published in the same transaction as the version, its values and the consumption of the draft they came from
/// (<c>docs/architecture/invariants.md</c> section 4.3). A version committed without this would leave every
/// downstream reader unaware the measurement exists; this committed without the version would announce one that
/// does not.
/// </para>
/// <para>
/// <strong>It carries no measurement.</strong> Not one number, not the field keys, not how many there were.
/// Measurements are sensitive personal data under <c>docs/nfr/data-classification.md</c>, and an outbox row fans
/// out to every registered handler and is kept for as long as the outbox keeps it — so a payload carrying them
/// would be a second, uncontrolled copy in a table nobody thinks of as holding measurements. A consumer that needs
/// the values reads them through the module's own contract, which re-authorises and audits the read.
/// </para>
/// <para>
/// <strong>It does not mean "re-measure anything".</strong> A garment job snapshots the version it was confirmed
/// against and does not move when a newer one appears (INV-JOB-01). A consumer treating this as "update the open
/// jobs" has misread it, and would change what a job already in production was cut to.
/// </para>
/// </remarks>
/// <param name="EventId">Identity of this occurrence. Consumers de-duplicate on it.</param>
/// <param name="OccurredAt">When the measurements were confirmed, in UTC.</param>
/// <param name="AggregateId">The measurement version. What a garment job pins.</param>
/// <param name="OrganisationId">The organisation.</param>
/// <param name="BranchId">The branch they were taken at.</param>
/// <param name="CustomerId">The customer they are about. An identifier and nothing else.</param>
/// <param name="TemplateId">The measurement template they answer.</param>
/// <param name="TemplateVersionId">The template version they were captured against, and render through.</param>
/// <param name="VersionNumber">Which measurement this is for that customer and template.</param>
/// <param name="CorrectsVersionId">
/// The version this one corrects, or null when it is not a correction. A consumer showing a customer's history
/// reads it to say "superseded" rather than "measured again".
/// </param>
public sealed record MeasurementVersionConfirmed(
    Guid EventId,
    DateTimeOffset OccurredAt,
    Guid AggregateId,
    Guid OrganisationId,
    Guid BranchId,
    Guid CustomerId,
    Guid TemplateId,
    Guid TemplateVersionId,
    int VersionNumber,
    Guid? CorrectsVersionId)
    : IntegrationEvent(EventId, OccurredAt, AggregateId)
{
    /// <summary>The wire name. Subscribe by this constant rather than by a literal.</summary>
    public const string Type = "customers.measurement-version-confirmed.v1";

    /// <inheritdoc />
    public override string EventType => Type;
}
