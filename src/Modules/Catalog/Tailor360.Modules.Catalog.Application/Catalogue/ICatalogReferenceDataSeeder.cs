namespace Tailor360.Modules.Catalog.Application.Catalogue;

/// <summary>
/// Writes the reference data the Catalog module owns: the initial stitching hierarchy and its services.
/// </summary>
/// <remarks>
/// <para>
/// Idempotent and safe to run against a live database, like the Identity and Customers seeders beside
/// it. It writes only when the organisation has <em>no</em> catalogue version at all, so running it
/// twice changes nothing and running it after an administrator has started work never touches what
/// they did (<c>docs/prd/category-hierarchy.md</c> section 9).
/// </para>
/// <para>
/// <strong>It seeds a draft and publishes nothing, and that is the point.</strong> Publishing demands
/// <c>catalog.publish</c>, a second factor, a fresh re-authentication and a stated reason, because a
/// published version is what orders are quoted, worked to and invoiced against. A command-line tool
/// holding none of those and answering to nobody must not perform it. The hierarchy itself is also not
/// yet agreed — section 11 records it as open decision <b>OD-CAT-01</b>, "drafted for the owner
/// workshop" — so publishing it automatically would present an unsettled product decision as settled,
/// in production, with orders taken against it.
/// </para>
/// <para>
/// <strong>It seeds no branch availability either</strong>, for the same reason. An empty set of
/// branches means offered nowhere rather than everywhere, and which branches offer which categories is
/// open decision <b>OD-CAT-04</b>. The seeder puts the hierarchy in place and leaves both decisions to
/// the person accountable for them; the outcome below is what tells an operator that is what happened.
/// </para>
/// </remarks>
public interface ICatalogReferenceDataSeeder
{
    /// <summary>Creates the initial catalogue draft for an organisation, if it has none.</summary>
    /// <param name="organisationId">The organisation whose catalogue is being seeded.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>What the run did, so that an operator has evidence rather than a promise.</returns>
    Task<CatalogSeedOutcome> SeedInitialCatalogAsync(
        Guid organisationId,
        CancellationToken cancellationToken = default);
}

/// <summary>What one seeding run did.</summary>
/// <param name="Created">
/// True when this run wrote the initial draft. False when the organisation already had a catalogue
/// version, in which case nothing was touched.
/// </param>
/// <param name="CatalogVersionId">The draft it wrote, or the version it found and left alone.</param>
/// <param name="VersionNumber">That version's number.</param>
/// <param name="CategoryCount">How many categories the draft holds.</param>
/// <param name="ServiceTypeCount">How many service types it holds.</param>
/// <param name="AwaitingPublication">
/// True while the organisation has no published version. Nothing is orderable until an Owner publishes
/// one, which is the number an operator acts on after a first install.
/// </param>
public sealed record CatalogSeedOutcome(
    bool Created,
    Guid CatalogVersionId,
    int VersionNumber,
    int CategoryCount,
    int ServiceTypeCount,
    bool AwaitingPublication);
