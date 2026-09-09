namespace Tailor360.Modules.Customers.Application.Measurements;

/// <summary>
/// Writes the measurement templates a shop starts from.
/// </summary>
/// <remarks>
/// <para>
/// Idempotent on "does this organisation have any measurement template at all?", like the consent and catalogue
/// seeders beside it. Running it twice changes nothing, and it never edits a template somebody has since worked
/// on — an administrator's field set is theirs, not something a later release quietly corrects.
/// </para>
/// <para>
/// <strong>It seeds drafts and publishes nothing.</strong> <c>docs/prd/measurement-templates.md</c> section 1
/// describes the end state as one published version each, and that is where these should arrive — but by way of
/// the review that section 9's own preamble demands, not by a command-line tool. Every field set there is marked
/// "proposed and to be confirmed", the bounds and the inch steps are open decisions <strong>OD-MEA-01</strong>,
/// <strong>OD-MEA-07</strong> and <strong>OD-MEA-08</strong>, and "business review of each initial category
/// template" is an acceptance criterion of issue #27. Publishing demands
/// <c>catalog.templates.publish</c>, a second factor and a stated reason; a tool holding none of those must not
/// do it, and a shop measuring customers against numbers nobody approved is worse than one that cannot measure
/// yet and knows why.
/// </para>
/// </remarks>
public interface IMeasurementTemplateReferenceDataSeeder
{
    /// <summary>Creates the initial templates for an organisation, once.</summary>
    /// <param name="organisationId">The organisation.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>What the run did, so that an operator has evidence rather than a promise.</returns>
    Task<MeasurementTemplateSeedOutcome> SeedTemplatesAsync(
        Guid organisationId,
        CancellationToken cancellationToken = default);
}

/// <summary>What one seeding run did.</summary>
/// <param name="Created">True when this run wrote the templates; false when they were already there.</param>
/// <param name="TemplateCount">How many templates exist for the organisation.</param>
/// <param name="FieldCount">How many fields they carry between them.</param>
/// <param name="AwaitingPublication">
/// How many templates have no published version. Everything the seeder writes is a draft, so on a first run this
/// equals <paramref name="TemplateCount"/> — and it is the number an operator needs to see.
/// </param>
public sealed record MeasurementTemplateSeedOutcome(
    bool Created,
    int TemplateCount,
    int FieldCount,
    int AwaitingPublication);
