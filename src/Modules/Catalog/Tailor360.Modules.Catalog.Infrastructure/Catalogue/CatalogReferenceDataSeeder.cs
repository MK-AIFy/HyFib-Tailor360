using Microsoft.EntityFrameworkCore;
using Tailor360.Modules.Catalog.Application.Catalogue;
using Tailor360.Modules.Catalog.Domain.Catalogue;
using Tailor360.Modules.Catalog.Infrastructure.Persistence;
using Tailor360.Platform.Abstractions.Identifiers;
using Tailor360.Platform.Abstractions.Time;

namespace Tailor360.Modules.Catalog.Infrastructure.Catalogue;

/// <summary>
/// Writes the initial stitching hierarchy, once, for an organisation that has no catalogue at all.
/// </summary>
/// <remarks>
/// <para>
/// Idempotence is decided by one question — does this organisation have a catalogue version? — rather
/// than by reconciling row by row. That is deliberate. A row-by-row seeder would overwrite an
/// administrator's edits on the next run, and there is no correct answer to "the shop renamed the Kids
/// category; should the seed rename it back". The hierarchy is configuration a person owns from the
/// moment it exists, and the seeder's whole job is to make it exist.
/// </para>
/// <para>
/// It writes through the aggregate rather than through the context, so a mistake in the seeded data is
/// refused by the same rules an administrator's edit is. Seeding an ill-formed code or a parent that
/// is not there fails the command-line tool rather than producing a draft nobody can publish.
/// </para>
/// </remarks>
/// <param name="context">The module's context.</param>
/// <param name="store">The catalogue store.</param>
/// <param name="clock">The clock.</param>
/// <param name="ids">The identifier generator.</param>
public sealed class CatalogReferenceDataSeeder(
    CatalogDbContext context,
    Application.Abstractions.ICatalogStore store,
    IClock clock,
    IIdGenerator ids)
    : ICatalogReferenceDataSeeder
{
    /// <inheritdoc />
    public async Task<CatalogSeedOutcome> SeedInitialCatalogAsync(
        Guid organisationId,
        CancellationToken cancellationToken = default)
    {
        var existing = await context.CatalogVersions
            .AsNoTracking()
            .IgnoreAutoIncludes()
            .Where(version => version.OrganisationId == organisationId)
            .OrderBy(version => version.VersionNumber)
            .FirstOrDefaultAsync(cancellationToken);

        var published = await context.CatalogVersions
            .AsNoTracking()
            .IgnoreAutoIncludes()
            .AnyAsync(
                version => version.OrganisationId == organisationId
                           && version.Status == CatalogStatus.Published,
                cancellationToken);

        if (existing is not null)
        {
            var counts = await CountAsync(existing.Id, cancellationToken);

            return new CatalogSeedOutcome(
                false, existing.Id, existing.VersionNumber, counts.Categories, counts.Services, !published);
        }

        var now = clock.UtcNow;
        var draft = CatalogVersion.CreateDraft(
                ids.NewId(),
                organisationId,
                await store.NextVersionNumberAsync(organisationId, cancellationToken),
                SeededCatalog.DraftName,
                SeededCatalog.DraftNotes,
                now,
                null)
            .Value;

        var byCode = new Dictionary<string, Guid>(StringComparer.Ordinal);

        foreach (var category in SeededCatalog.Categories)
        {
            var parentId = category.ParentCode is { } parentCode ? byCode[parentCode] : (Guid?)null;

            var added = draft.AddCategory(
                ids.NewId(),
                ids.NewId(),
                parentId,
                new CategoryDetails(
                    category.Code,
                    category.Name,
                    category.NameTamil,
                    category.Description,
                    category.DisplayOrder,
                    null,
                    null,
                    null,
                    // Empty on purpose: which branches offer which categories is open decision
                    // OD-CAT-04, and an empty set means offered nowhere rather than everywhere.
                    []),
                now,
                null);

            if (added.IsFailure)
            {
                throw new InvalidOperationException(
                    $"The seeded category '{category.Code}' was refused: {added.Error.Code} — "
                    + $"{added.Error.Message}");
            }

            byCode[category.Code] = added.Value.Id;
        }

        foreach (var service in SeededCatalog.Services)
        {
            var kind = SeededCatalog.Kinds[service.ServiceCode];

            var added = draft.AddServiceType(
                ids.NewId(),
                ids.NewId(),
                byCode[service.CategoryCode],
                new ServiceTypeDetails(
                    service.ServiceCode,
                    kind.Name,
                    null,
                    kind.Description,
                    kind.DisplayOrder,
                    service.ExpectedDurationDays,
                    service.IntakeWarning,
                    // The five links belong to modules that have not landed yet. They are left unset
                    // and allowIncomplete is false, so publication is refused until they are supplied
                    // — which is the prompt, not an obstruction.
                    null,
                    null,
                    [],
                    null,
                    null,
                    AllowIncomplete: false,
                    null,
                    null,
                    []),
                now,
                null);

            if (added.IsFailure)
            {
                throw new InvalidOperationException(
                    $"The seeded service type '{service.CategoryCode}.{service.ServiceCode}' was "
                    + $"refused: {added.Error.Code} — {added.Error.Message}");
            }
        }

        store.Add(draft);
        await store.SaveAsync(cancellationToken);

        return new CatalogSeedOutcome(
            true,
            draft.Id,
            draft.VersionNumber,
            draft.Categories.Count,
            draft.ServiceTypes.Count,
            AwaitingPublication: true);
    }

    private async Task<(int Categories, int Services)> CountAsync(
        Guid versionId,
        CancellationToken cancellationToken)
        => (await context.Categories.AsNoTracking().IgnoreAutoIncludes()
                .CountAsync(category => category.CatalogVersionId == versionId, cancellationToken),
            await context.ServiceTypes.AsNoTracking().IgnoreAutoIncludes()
                .CountAsync(service => service.CatalogVersionId == versionId, cancellationToken));
}
