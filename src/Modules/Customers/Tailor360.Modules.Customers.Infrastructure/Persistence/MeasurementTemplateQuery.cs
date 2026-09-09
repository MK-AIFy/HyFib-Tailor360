using Microsoft.EntityFrameworkCore;
using Tailor360.Modules.Customers.Contracts.Measurements;
using Tailor360.Modules.Customers.Domain.Measurements;

namespace Tailor360.Modules.Customers.Infrastructure.Persistence;

/// <summary>
/// The published measurement-template read, over the <c>customers</c> schema.
/// </summary>
/// <remarks>
/// Nothing here is cached. A template is read once when a capture starts and once when a measurement is rendered,
/// which is nowhere near the traffic of the catalogue read that earned its cache — and the answer decides what a
/// tailor is asked to measure, so a stale one is worse than a slow one.
/// </remarks>
/// <param name="context">The module's context.</param>
public sealed class MeasurementTemplateQuery(CustomersDbContext context) : IMeasurementTemplateQuery
{
    /// <inheritdoc />
    public async Task<MeasurementTemplateSnapshot?> GetPublishedAsync(
        Guid templateId,
        CancellationToken cancellationToken = default)
    {
        var template = await context.MeasurementTemplates
            .AsNoTracking()
            .SingleOrDefaultAsync(candidate => candidate.Id == templateId, cancellationToken);

        var version = template?.PublishedVersion;

        return template is null || version is null ? null : Project(template, version);
    }

    /// <inheritdoc />
    public async Task<MeasurementTemplateSnapshot?> GetVersionAsync(
        Guid versionId,
        CancellationToken cancellationToken = default)
    {
        var templateId = await context.TemplateVersions
            .AsNoTracking()
            .IgnoreAutoIncludes()
            .Where(version => version.Id == versionId)
            .Select(version => (Guid?)version.MeasurementTemplateId)
            .SingleOrDefaultAsync(cancellationToken);

        if (templateId is not { } id)
        {
            return null;
        }

        var template = await context.MeasurementTemplates
            .AsNoTracking()
            .SingleOrDefaultAsync(candidate => candidate.Id == id, cancellationToken);

        return template?.Find(versionId) is { } version ? Project(template, version) : null;
    }

    /// <inheritdoc />
    public async Task<IReadOnlySet<Guid>> WithPublishedVersionAsync(
        IReadOnlyCollection<Guid> templateIds,
        Guid organisationId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(templateIds);

        if (templateIds.Count == 0)
        {
            return new HashSet<Guid>();
        }

        var wanted = templateIds.Distinct().ToArray();

        // Projected in the database: the caller is asking "which of these are ready", and loading each template
        // with its versions and fields to answer a yes or no would read the whole configuration of the shop.
        var ready = await context.TemplateVersions
            .AsNoTracking()
            .IgnoreAutoIncludes()
            .Where(version => version.OrganisationId == organisationId
                              && version.Status == TemplateStatus.Published
                              && wanted.Contains(version.MeasurementTemplateId))
            .Select(version => version.MeasurementTemplateId)
            .Distinct()
            .ToListAsync(cancellationToken);

        return ready.ToHashSet();
    }

    private static MeasurementTemplateSnapshot Project(MeasurementTemplate template, TemplateVersion version)
        => new(
            template.Id,
            version.Id,
            template.OrganisationId,
            template.Code,
            template.Name,
            version.VersionNumber,
            version.Status == TemplateStatus.Published,
            version.DefaultDisplayUnit.ToString(),
            [
                .. version.Fields
                    .OrderBy(field => field.DisplayOrder)
                    .ThenBy(field => field.Key.Value, StringComparer.Ordinal)
                    .Select(Project),
            ]);

    private static MeasurementFieldSnapshot Project(TemplateField field)
        => new(
            field.Key.Value,
            field.Label,
            field.LabelTamil,
            field.GroupName,
            field.DisplayOrder,
            field.CanonicalUnit.ToString(),
            [.. field.DisplayUnits.Select(unit => unit.ToString())],
            field.Precision.InchFraction,
            field.Precision.CentimetreDecimals,
            field.IsRequired,
            field.Bands.MinimumMillimetres,
            field.Bands.MaximumMillimetres,
            field.Bands.WarnBelowMillimetres,
            field.Bands.WarnAboveMillimetres,
            field.HelpText,
            field.DiagramReference,
            field.DiagramAlt,
            Project(field.Rule),
            [
                .. field.Options
                    .OrderBy(option => option.DisplayOrder)
                    .ThenBy(option => option.Code, StringComparer.Ordinal)
                    .Select(option => new MeasurementOptionSnapshot(
                        option.Code, option.Label, option.LabelTamil, option.DisplayOrder)),
            ]);

    private static MeasurementRuleSnapshot? Project(ConditionalRule? rule)
        => rule is null
            ? null
            : new MeasurementRuleSnapshot(
                rule.Effect.ToString(),
                [
                    .. rule.AnyOf.Select(clause => new MeasurementRuleClauseSnapshot(
                        clause.Scope.ToString(),
                        clause.Name,
                        clause.Operator.ToString(),
                        [.. clause.Values])),
                ]);
}
