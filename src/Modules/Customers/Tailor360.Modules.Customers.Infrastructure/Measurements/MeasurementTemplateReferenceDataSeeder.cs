using Tailor360.Modules.Customers.Application.Abstractions;
using Tailor360.Modules.Customers.Application.Measurements;
using Tailor360.Modules.Customers.Domain.Measurements;
using Tailor360.Platform.Abstractions.Identifiers;
using Tailor360.Platform.Abstractions.Time;

namespace Tailor360.Modules.Customers.Infrastructure.Measurements;

/// <summary>
/// Writes the six initial measurement templates, once, as drafts.
/// </summary>
/// <param name="store">The module's template store.</param>
/// <param name="clock">The platform clock.</param>
/// <param name="ids">The identifier generator.</param>
public sealed class MeasurementTemplateReferenceDataSeeder(
    IMeasurementTemplateStore store,
    IClock clock,
    IIdGenerator ids)
    : IMeasurementTemplateReferenceDataSeeder
{
    /// <inheritdoc />
    public async Task<MeasurementTemplateSeedOutcome> SeedTemplatesAsync(
        Guid organisationId,
        CancellationToken cancellationToken = default)
    {
        // Idempotent on "does this organisation have any template at all?", not per template. Once an
        // administrator has begun working here, the field sets are theirs; a later run that added a template back
        // or corrected a bound would be a release quietly editing somebody's configuration.
        if (await store.AnyAsync(organisationId, cancellationToken))
        {
            return await CountAsync(organisationId, created: false, cancellationToken);
        }

        var now = clock.UtcNow;

        foreach (var seeded in SeededMeasurementTemplates.All)
        {
            var template = MeasurementTemplate.Create(
                ids.NewId(), organisationId, seeded.Code, seeded.Name, seeded.Description, now, null);

            if (template.IsFailure)
            {
                throw new InvalidOperationException(
                    $"The seeded measurement template '{seeded.Code}' is not valid: {template.Error.Message}");
            }

            var draft = template.Value.StartDraft(
                ids, seeded.VersionName, seeded.Notes, seeded.DefaultDisplayUnit, null, now, null);

            if (draft.IsFailure)
            {
                throw new InvalidOperationException(
                    $"The first version of '{seeded.Code}' could not be started: {draft.Error.Message}");
            }

            foreach (var field in seeded.Fields)
            {
                var added = draft.Value.AddField(ids.NewId(), field, now, null);

                if (added.IsFailure)
                {
                    // A seeded field that the domain refuses is a defect in the transcription from
                    // docs/prd/measurement-templates.md, not a condition an operator can act on. Failing loudly
                    // during init-reference-data is the earliest anybody could find out.
                    throw new InvalidOperationException(
                        $"The seeded field '{field.Key}' of '{seeded.Code}' is not valid: {added.Error.Message}");
                }
            }

            store.Add(template.Value);
        }

        await store.SaveAsync(cancellationToken);

        return await CountAsync(organisationId, created: true, cancellationToken);
    }

    private async Task<MeasurementTemplateSeedOutcome> CountAsync(
        Guid organisationId,
        bool created,
        CancellationToken cancellationToken)
    {
        var templates = await store.ListAsync(organisationId, cancellationToken);

        return new MeasurementTemplateSeedOutcome(
            created,
            templates.Count,
            templates.Sum(template => template.Versions.Sum(version => version.Fields.Count)),
            templates.Count(template => template.PublishedVersion is null));
    }
}
