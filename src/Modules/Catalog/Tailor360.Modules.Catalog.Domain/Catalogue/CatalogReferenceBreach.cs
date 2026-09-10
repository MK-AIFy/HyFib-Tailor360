namespace Tailor360.Modules.Catalog.Domain.Catalogue;

/// <summary>
/// A cross-module reference of the published catalogue that has stopped being valid since publication.
/// </summary>
/// <remarks>
/// <para>
/// A service type carries five links into other modules' configuration, and none of them is a foreign key: **G-6**
/// in <c>docs/architecture/invariants.md</c> says a cross-module reference is validated through the contract at
/// write time and its <em>continued</em> validity is reconciled by events. This is where that reconciliation puts
/// its answer.
/// </para>
/// <para>
/// <strong>Why a row rather than a log line.</strong> The breach opens because of something that happened in
/// another module, at a moment nobody was looking at the catalogue. A log line is gone by the time anybody asks,
/// and a metric says how many without saying which. The row is the thing an administrator is shown, and it is in
/// the <c>catalog</c> schema because it commits with the inbox row that records the reconciliation ran — a breach
/// recorded in another module's transaction could survive a rollback of the delivery that found it.
/// </para>
/// <para>
/// <strong>Why it is not a status on the service type.</strong> A published version's rows are what orders were
/// placed against, and <see cref="ServiceType.NotOrderable"/> is derived from the links being present rather than
/// stored — there is no column to set, and inventing one would make a reconciliation rewrite a published version.
/// Whether a breach should also stop the counter ordering is a product decision (<c>OD-18</c>), not one the
/// reconciliation invents.
/// </para>
/// <para>
/// A breach is <em>closed</em> rather than deleted. A template retired and republished twice in a month is a
/// pattern worth seeing, and a row that vanished on resolution would leave the second occurrence looking like the
/// first.
/// </para>
/// </remarks>
public sealed class CatalogReferenceBreach
{
    private CatalogReferenceBreach()
    {
        // The persistence layer materialises instances through this constructor.
        Code = string.Empty;
        Target = string.Empty;
        Message = string.Empty;
        Validator = string.Empty;
        DetectedBecauseOf = string.Empty;
    }

    /// <summary>Opens a breach.</summary>
    /// <param name="id">Identity of this occurrence.</param>
    /// <param name="organisationId">The organisation whose catalogue it is.</param>
    /// <param name="catalogVersionId">The published version the breach is in.</param>
    /// <param name="code">The finding's stable dotted code.</param>
    /// <param name="target">Which part of the version it is about, or the empty string for the version itself.</param>
    /// <param name="message">What is wrong, in the shop's words.</param>
    /// <param name="validator">Which module's validator found it.</param>
    /// <param name="detectedBecauseOf">The wire name of the event that triggered the check.</param>
    /// <param name="detectedAt">When the check that found it ran, in UTC.</param>
    public CatalogReferenceBreach(
        Guid id,
        Guid organisationId,
        Guid catalogVersionId,
        string code,
        string target,
        string message,
        string validator,
        string detectedBecauseOf,
        DateTimeOffset detectedAt)
    {
        Id = id;
        OrganisationId = organisationId;
        CatalogVersionId = catalogVersionId;
        Code = code;
        Target = target;
        Message = message;
        Validator = validator;
        DetectedBecauseOf = detectedBecauseOf;
        DetectedAt = detectedAt;
    }

    /// <summary>Identity of this occurrence.</summary>
    public Guid Id { get; private set; }

    /// <summary>The organisation whose catalogue it is.</summary>
    public Guid OrganisationId { get; private set; }

    /// <summary>The published version the breach is in.</summary>
    public Guid CatalogVersionId { get; private set; }

    /// <summary>The finding's stable dotted code, such as <c>catalog.measurement-template-not-published</c>.</summary>
    public string Code { get; private set; }

    /// <summary>
    /// Which part of the version it is about, as the path an administration screen focuses — for example
    /// <c>serviceTypes[BLOUSE.STITCHING].measurementTemplateId</c>.
    /// </summary>
    /// <remarks>
    /// The empty string, never null, for a finding about the version as a whole. It is half of the key that decides
    /// whether a breach is the same one as last time, and a nullable column would make that key
    /// <c>IS NOT DISTINCT FROM</c> rather than an index lookup.
    /// </remarks>
    public string Target { get; private set; }

    /// <summary>What is wrong, in the shop's words. The validator's own message, unchanged.</summary>
    public string Message { get; private set; }

    /// <summary>Which module's validator found it.</summary>
    public string Validator { get; private set; }

    /// <summary>
    /// The wire name of the integration event whose delivery ran the check.
    /// </summary>
    /// <remarks>
    /// Kept because "why was this noticed now" is the second question anybody asks about a breach that opened at
    /// four in the morning, and the answer is always the event — a template retired, a template published, or a
    /// catalogue published.
    /// </remarks>
    public string DetectedBecauseOf { get; private set; }

    /// <summary>When the check that found it ran, in UTC.</summary>
    public DateTimeOffset DetectedAt { get; private set; }

    /// <summary>When a later check found it no longer held, or null while it still does.</summary>
    public DateTimeOffset? ResolvedAt { get; private set; }

    /// <summary>The wire name of the event whose delivery found it resolved, or null while it stands.</summary>
    public string? ResolvedBecauseOf { get; private set; }

    /// <summary>Whether this breach is still standing.</summary>
    public bool IsOpen => ResolvedAt is null;

    /// <summary>Closes the breach, because a later check no longer finds it.</summary>
    /// <param name="at">When the check ran, in UTC.</param>
    /// <param name="becauseOf">The wire name of the event that triggered that check.</param>
    /// <remarks>
    /// Idempotent: a redelivery of the same event runs the same check and must not move the moment the breach
    /// stopped holding, which is what an administrator reads as "how long the shop was exposed".
    /// </remarks>
    public void Resolve(DateTimeOffset at, string becauseOf)
    {
        if (ResolvedAt is not null)
        {
            return;
        }

        ResolvedAt = at;
        ResolvedBecauseOf = becauseOf;
    }
}
