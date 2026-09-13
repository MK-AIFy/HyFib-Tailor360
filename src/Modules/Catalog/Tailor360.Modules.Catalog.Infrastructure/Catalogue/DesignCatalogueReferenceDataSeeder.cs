using Microsoft.EntityFrameworkCore;
using Tailor360.Modules.Catalog.Application.Abstractions;
using Tailor360.Modules.Catalog.Application.Catalogue;
using Tailor360.Modules.Catalog.Domain.Catalogue;
using Tailor360.Modules.Catalog.Domain.Design;
using Tailor360.Modules.Catalog.Infrastructure.Persistence;
using Tailor360.Platform.Abstractions.Identifiers;
using Tailor360.Platform.Abstractions.Time;

namespace Tailor360.Modules.Catalog.Infrastructure.Catalogue;

/// <summary>
/// Writes the design catalogue of <c>docs/prd/design-options.md</c> section 9 into the organisation's
/// earliest catalogue draft, once, the way <see cref="CatalogReferenceDataSeeder"/> writes the hierarchy.
/// </summary>
/// <remarks>
/// Idempotence is decided the same way: does the organisation's earliest catalogue version already hold
/// a design group? A row-by-row seeder would overwrite an administrator's edits on the next run, and the
/// design catalogue is exactly the kind of configuration an administrator owns from the moment it
/// exists. It writes through the aggregate, so a mistake in the seeded data is refused by the same rules
/// (and the same publish-time validator) an administrator's own edit would be.
/// </remarks>
/// <param name="context">The module's context, for the lightweight existence checks.</param>
/// <param name="store">The catalogue store, for the whole-aggregate load and save.</param>
/// <param name="clock">The clock.</param>
/// <param name="ids">The identifier generator.</param>
public sealed class DesignCatalogueReferenceDataSeeder(
    CatalogDbContext context,
    ICatalogStore store,
    IClock clock,
    IIdGenerator ids)
    : IDesignCatalogueReferenceDataSeeder
{
    /// <inheritdoc />
    public async Task<DesignCatalogueSeedOutcome> SeedDesignGroupsAsync(
        Guid organisationId,
        CancellationToken cancellationToken = default)
    {
        var versions = await context.CatalogVersions
            .AsNoTracking()
            .IgnoreAutoIncludes()
            .Where(version => version.OrganisationId == organisationId)
            .OrderBy(version => version.VersionNumber)
            .ToListAsync(cancellationToken);

        if (versions.Count == 0)
        {
            // The category and service-type seed (ICatalogReferenceDataSeeder) has not run for this
            // organisation. init-reference-data always calls it first; a caller that has not is asking
            // for design groups linked to categories that do not exist yet.
            throw new InvalidOperationException(
                $"No catalogue version exists for organisation {organisationId}. Run "
                + $"{nameof(ICatalogReferenceDataSeeder)}.{nameof(ICatalogReferenceDataSeeder.SeedInitialCatalogAsync)} "
                + "first.");
        }

        var versionIds = versions.Select(version => version.Id).ToArray();

        var alreadySeededVersionId = await context.DesignGroups
            .AsNoTracking()
            .IgnoreAutoIncludes()
            .Where(group => versionIds.Contains(group.CatalogVersionId))
            .Select(group => (Guid?)group.CatalogVersionId)
            .FirstOrDefaultAsync(cancellationToken);

        if (alreadySeededVersionId is { } seededVersionId)
        {
            var counts = await CountAsync(seededVersionId, cancellationToken);

            return new DesignCatalogueSeedOutcome(false, seededVersionId, counts.Groups, counts.Rules);
        }

        // Not just the earliest version: by the time this seeder runs, the organisation's earliest
        // catalogue version may already have been published (an administrator can publish the plain
        // stitching hierarchy — #137's own scope — long before the design catalogue exists to add to
        // it). Writing to a published version is refused by the aggregate itself, so seed into whichever
        // version is still a draft rather than assuming the lowest version number always is one.
        var existingVersion = versions.FirstOrDefault(version => version.Status == CatalogStatus.Draft)
            ?? throw new InvalidOperationException(
                $"Organisation {organisationId} has {versions.Count} catalogue version(s) and none is a "
                + "draft — every version is already published. The design catalogue can only be seeded "
                + "into a draft; create one through the catalogue administration API, then run this "
                + "seeder again.");

        var draft = await store.FindAsync(existingVersion.Id, organisationId, cancellationToken)
            ?? throw new InvalidOperationException(
                $"Catalogue version {existingVersion.Id} was found and then vanished before it could be loaded.");

        var now = clock.UtcNow;
        var categoryIdByCode = draft.Categories.ToDictionary(
            category => category.Code, category => category.Id, StringComparer.Ordinal);

        var groupIdByCategoryAndCode = new Dictionary<(string CategoryCode, string GroupCode), Guid>();
        var displayOrderByCategory = new Dictionary<string, int>(StringComparer.Ordinal);

        foreach (var group in SeededDesignCatalogue.Groups)
        {
            if (!categoryIdByCode.TryGetValue(group.CategoryCode, out var categoryId))
            {
                throw new InvalidOperationException(
                    $"The seeded design group '{group.CategoryCode}.{group.Code}' names a category the "
                    + "category seed did not create. Run the category seed first.");
            }

            var displayOrder = displayOrderByCategory.GetValueOrDefault(group.CategoryCode);
            displayOrderByCategory[group.CategoryCode] = displayOrder + 1;

            var addedGroup = draft.AddDesignGroup(
                ids.NewId(),
                ids.NewId(),
                categoryId,
                new DesignGroupDetails(
                    group.Code,
                    group.Name,
                    null,
                    group.SelectionMode,
                    group.Required,
                    displayOrder,
                    null,
                    null,
                    // Branch availability is left empty, as the category and service-type seed leaves
                    // the categories themselves: an empty set means offered nowhere until an Owner sets
                    // it, never everywhere by omission (OD-CAT-04's design-catalogue counterpart).
                    []),
                now,
                null);

            if (addedGroup.IsFailure)
            {
                throw new InvalidOperationException(
                    $"The seeded design group '{group.CategoryCode}.{group.Code}' was refused: "
                    + $"{addedGroup.Error.Code} — {addedGroup.Error.Message}");
            }

            groupIdByCategoryAndCode[(group.CategoryCode, group.Code)] = addedGroup.Value.Id;

            for (var index = 0; index < group.Options.Count; index++)
            {
                var option = group.Options[index];

                var addedOption = draft.AddDesignOption(
                    ids.NewId(),
                    ids.NewId(),
                    addedGroup.Value.Id,
                    new DesignOptionDetails(
                        option.Code,
                        option.Name,
                        null,
                        SeededDesignCatalogue.HelpTextFor(group.Name, option.Name),
                        $"{group.IllustrationSheet}#{group.Code}.{option.Code}",
                        SeededDesignCatalogue.IllustrationAltFor(group.Name, option.Name),
                        option.PriceListItemCode,
                        option.DayImpact,
                        index,
                        Active: true),
                    now,
                    null);

                if (addedOption.IsFailure)
                {
                    throw new InvalidOperationException(
                        $"The seeded design option '{group.CategoryCode}.{group.Code}.{option.Code}' was "
                        + $"refused: {addedOption.Error.Code} — {addedOption.Error.Message}");
                }
            }
        }

        foreach (var rule in SeededDesignCatalogue.Rules)
        {
            var categoryId = categoryIdByCode[rule.CategoryCode];

            var addedRule = draft.AddDesignRule(
                ids.NewId(),
                ids.NewId(),
                categoryId,
                rule.Number,
                new DesignRuleDetails(rule.Type, rule.Antecedent, rule.Consequent, rule.Note, rule.Why),
                now,
                null);

            if (addedRule.IsFailure)
            {
                throw new InvalidOperationException(
                    $"The seeded design rule DR-{rule.Number:00} ({rule.CategoryCode}) was refused: "
                    + $"{addedRule.Error.Code} — {addedRule.Error.Message}");
            }
        }

        // Link 3: STITCHING and RESTITCHING carry the category's full set of groups, in the seeded
        // order; ALTERATION carries none — the document's own proposal, tracked as OD-DES-01.
        foreach (var categoryCode in categoryIdByCode.Keys)
        {
            if (!groupIdByCategoryAndCode.Keys.Any(key => key.CategoryCode == categoryCode))
            {
                // A category with no seeded design groups at all (there are none today, but a future
                // category added to SeededCatalog without a Section 9 counterpart must not crash here).
                continue;
            }

            var categoryId = categoryIdByCode[categoryCode];
            var groupIdsInOrder = SeededDesignCatalogue.Groups
                .Where(group => string.Equals(group.CategoryCode, categoryCode, StringComparison.Ordinal))
                .Select(group => groupIdByCategoryAndCode[(categoryCode, group.Code)])
                .ToArray();

            foreach (var serviceCode in SeededDesignCatalogue.ServiceCodesOffered)
            {
                var service = draft.ServicesOf(categoryId)
                    .SingleOrDefault(candidate => string.Equals(candidate.Code, serviceCode, StringComparison.Ordinal));

                if (service is null)
                {
                    throw new InvalidOperationException(
                        $"The category seed did not create a '{categoryCode}.{serviceCode}' service type "
                        + "to link the seeded design groups to.");
                }

                var edited = draft.EditServiceType(
                    service.Id,
                    new ServiceTypeDetails(
                        service.Code,
                        service.Name,
                        service.NameTamil,
                        service.Description,
                        service.DisplayOrder,
                        service.ExpectedDurationDays,
                        service.IntakeWarning,
                        service.MeasurementTemplateId,
                        service.WorkflowDefinitionId,
                        groupIdsInOrder,
                        service.PriceListItemCode,
                        service.QcChecklistTemplateId,
                        service.AllowIncomplete,
                        service.ActiveFrom,
                        service.ActiveTo,
                        [.. service.BranchIds]),
                    now,
                    null);

                if (edited.IsFailure)
                {
                    throw new InvalidOperationException(
                        $"Linking the seeded design groups to '{categoryCode}.{serviceCode}' was refused: "
                        + $"{edited.Error.Code} — {edited.Error.Message}");
                }
            }
        }

        await store.SaveAsync(cancellationToken);

        return new DesignCatalogueSeedOutcome(true, draft.Id, draft.DesignGroups.Count, draft.DesignRules.Count);
    }

    private async Task<(int Groups, int Rules)> CountAsync(Guid versionId, CancellationToken cancellationToken)
        => (await context.DesignGroups.AsNoTracking().IgnoreAutoIncludes()
                .CountAsync(group => group.CatalogVersionId == versionId, cancellationToken),
            await context.DesignRules.AsNoTracking().IgnoreAutoIncludes()
                .CountAsync(rule => rule.CatalogVersionId == versionId, cancellationToken));
}
