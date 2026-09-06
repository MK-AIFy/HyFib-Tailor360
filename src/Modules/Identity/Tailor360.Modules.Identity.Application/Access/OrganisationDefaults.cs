namespace Tailor360.Modules.Identity.Application.Access;

/// <summary>
/// The organisation a single-shop installation runs as.
/// </summary>
/// <remarks>
/// <para>
/// Every operational aggregate carries an <c>organisation_id</c> and the product is one legal entity
/// owning every branch (<c>docs/prd/glossary.md</c>), so nothing in this release chooses between
/// organisations. Reference-data seeding still has to name one, and inventing a fresh identifier on
/// each run would make the command anything but idempotent — so a single well-known value is fixed
/// here, and the seeding command takes <c>--organisation</c> for the day a deployment needs another.
/// </para>
/// <para>
/// It is a literal rather than a generated identifier for the same reason: a seed that is not
/// reproducible is not a seed. The value is a UUIDv7 with a fixed timestamp, which keeps it in the same
/// key space as every other identifier in the system.
/// </para>
/// </remarks>
public static class OrganisationDefaults
{
    /// <summary>The organisation a single-shop installation seeds its reference data for.</summary>
    public static Guid OrganisationId { get; } = Guid.Parse("0199a000-0000-7000-8000-000000000000");
}
