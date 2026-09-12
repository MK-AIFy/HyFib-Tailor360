namespace Tailor360.Modules.Catalog.Application.Catalogue;

/// <summary>
/// Loads the design catalogue — groups, options and rules of <c>docs/prd/design-options.md</c> section
/// 9 — into the organisation's seeded draft (issue #139, following #137 and #138).
/// </summary>
/// <remarks>
/// <para>
/// Idempotent in the same way <see cref="ICatalogReferenceDataSeeder"/> is: it writes only when the
/// organisation's earliest catalogue version has <em>no</em> design groups at all, so running it twice
/// adds nothing and running it after an administrator has started editing the design catalogue never
/// touches what they did.
/// </para>
/// <para>
/// It requires the category and service-type hierarchy to already exist — <see cref="ICatalogReferenceDataSeeder"/>
/// runs first, in the same <c>init-reference-data</c> command — because a design group is seeded into a
/// category by its code (<c>BLOUSE_PATTERN</c>, <c>BLOUSE_AARI</c>, <c>SALWAR</c>, <c>LEHENGA</c>,
/// <c>GOWN</c>, <c>KIDS</c>) and linked to that category's <c>STITCHING</c> and <c>RESTITCHING</c>
/// service types (link 3, OD-DES-01); <c>ALTERATION</c> carries none.
/// </para>
/// </remarks>
public interface IDesignCatalogueReferenceDataSeeder
{
    /// <summary>Adds the seeded design groups, options and rules to the organisation's draft, if it has none yet.</summary>
    /// <param name="organisationId">The organisation whose catalogue is being seeded.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>What the run did, so that an operator has evidence rather than a promise.</returns>
    Task<DesignCatalogueSeedOutcome> SeedDesignGroupsAsync(
        Guid organisationId,
        CancellationToken cancellationToken = default);
}

/// <summary>What one seeding run did.</summary>
/// <param name="Created">
/// True when this run wrote the design groups. False when the organisation's draft already held design
/// groups, in which case nothing was touched.
/// </param>
/// <param name="CatalogVersionId">The draft the groups were written to, or found already seeded.</param>
/// <param name="GroupCount">How many design option groups the draft holds.</param>
/// <param name="RuleCount">How many design rules the draft holds.</param>
public sealed record DesignCatalogueSeedOutcome(
    bool Created,
    Guid CatalogVersionId,
    int GroupCount,
    int RuleCount);
