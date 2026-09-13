namespace Tailor360.Modules.Catalog.Contracts.Catalogue;

/// <summary>
/// What the catalogue offers, and to whom. The only way another module reads it.
/// </summary>
/// <remarks>
/// <para>
/// Two questions, and they are deliberately different. <see cref="IsOrderableAsync"/> is asked before
/// something new is taken on and answers about the <em>currently published</em> version;
/// <see cref="GetServiceAsync"/> is asked about a service a job was already pinned to and answers from
/// whatever version that was, published or retired. Collapsing them would mean either refusing to
/// render a job whose category has since been retired, or offering a retired category at intake — and
/// both of those are the failure the versioning exists to prevent
/// (<c>docs/prd/category-hierarchy.md</c> section 7).
/// </para>
/// <para>
/// Reads are cached against the published version's identifier and the cache is made unreachable by
/// <c>CatalogVersionPublished</c> rather than swept (decision D21). No cache is ever authoritative:
/// a caller that needs to be certain asks again after the event.
/// </para>
/// </remarks>
public interface ICatalogAvailabilityQuery
{
    /// <summary>
    /// Whether a service may be ordered at a branch on a given day.
    /// </summary>
    /// <remarks>
    /// True only when every one of these holds: the service belongs to the published version, its
    /// category is not a grouping node, the category and the service are both inside their active
    /// periods, the branch offers both, the category's feature flag is on, and the service was not
    /// flagged not-orderable at publication. Intake neither lists nor accepts anything else.
    /// </remarks>
    /// <param name="serviceTypeId">The service type.</param>
    /// <param name="branchId">The branch the work would be taken at.</param>
    /// <param name="at">
    /// The instant to judge. Active dates are the shop's calendar, so this is read as a date in the
    /// branch's timezone rather than in UTC (<c>docs/architecture/conventions.md</c> 2.4).
    /// </param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True when the service may be ordered.</returns>
    Task<bool> IsOrderableAsync(
        Guid serviceTypeId,
        Guid branchId,
        DateTimeOffset at,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// One service type as its own version holds it, whether or not that version is still published.
    /// </summary>
    /// <remarks>
    /// This is what makes a two-year-old job card render. The job pinned a service type at
    /// confirmation; the version it belonged to may since have been retired, and reading it back must
    /// still give the labels, the duration and the five links exactly as they were.
    /// </remarks>
    /// <param name="serviceTypeId">The service type, from a pin.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The snapshot, or null when no version holds that service type.</returns>
    Task<CatalogServiceSnapshot?> GetServiceAsync(
        Guid serviceTypeId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Everything a branch may order today, and the version that answer came from.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The same predicate <see cref="IsOrderableAsync"/> applies, asked once for the whole branch
    /// rather than once per service. It is one method rather than a filter the caller writes, because
    /// a second copy of "what orderable means" is how a screen ends up offering something the
    /// confirmation then refuses.
    /// </para>
    /// <para>
    /// <strong>The version and the services are one answer, not two.</strong> Asking which version is
    /// published and then what it offers reads the published version twice, and a publication landing
    /// between the two reads would label one version's services with another version's identifier — a
    /// caller pinning that pair would pin a catalogue selection that never existed. One lookup cannot
    /// disagree with itself.
    /// </para>
    /// </remarks>
    /// <param name="organisationId">The organisation.</param>
    /// <param name="branchId">The branch the work would be taken at.</param>
    /// <param name="at">The instant to judge, read as a date in the branch's timezone.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The published version and what it offers, or <see cref="OrderableCatalog.None"/>.</returns>
    Task<OrderableCatalog> GetOrderableCatalogAsync(
        Guid organisationId,
        Guid branchId,
        DateTimeOffset at,
        CancellationToken cancellationToken = default);

    /// <summary>Whether the published catalogue still points at a measurement template.</summary>
    /// <remarks>
    /// <para>
    /// Asked by Customers before it retires a template's last published version (#27). Retiring one the catalogue
    /// still references would leave a service type a counter can order with nothing to measure it by — the failure
    /// the dependency validators exist to prevent, arriving from the other direction.
    /// </para>
    /// <para>
    /// It answers about the <em>published</em> version only. A draft catalogue referencing a template is not a
    /// reason to refuse: nothing can be ordered against a draft, and the catalogue's own publish validation is
    /// where that reference is checked.
    /// </para>
    /// </remarks>
    /// <param name="measurementTemplateId">The template.</param>
    /// <param name="organisationId">The organisation.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True when a published service type carries it.</returns>
    Task<bool> ReferencesMeasurementTemplateAsync(
        Guid measurementTemplateId,
        Guid organisationId,
        CancellationToken cancellationToken = default);

    /// <summary>Every price-list item code the published catalogue names, and where it is offered.</summary>
    /// <remarks>
    /// <para>
    /// Asked by Billing before it publishes a price-list version (#146), because publishing one retires the
    /// version it supersedes: a draft that dropped an item the catalogue names, or a branch the outgoing
    /// version priced, would leave a service the counter can order with nothing to price it by — link 4's
    /// mirror image of <see cref="ReferencesMeasurementTemplateAsync"/>.
    /// </para>
    /// <para>
    /// It answers about the <em>published</em> version only, and about every service type and every active
    /// design option carrying a code, whatever its active period: a code that is offered next month must be
    /// priced when next month comes, and the version publishing today is the one that will be in force then.
    /// </para>
    /// </remarks>
    /// <param name="organisationId">The organisation.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The references, in qualified-reference order; empty when nothing is published.</returns>
    Task<IReadOnlyList<CatalogPriceListItemReference>> PublishedPriceListItemReferencesAsync(
        Guid organisationId,
        CancellationToken cancellationToken = default);
}

/// <summary>One place the published catalogue names a price-list item code.</summary>
/// <param name="Reference">
/// The qualified reference of what carries the code: <c>CATEGORY.SERVICE</c> for a service type,
/// <c>CATEGORY.GROUP.OPTION</c> for a design option.
/// </param>
/// <param name="ItemCode">The price-list item code it names.</param>
/// <param name="BranchIds">The branches it is offered at: its own branches within its category's.</param>
public sealed record CatalogPriceListItemReference(
    string Reference,
    string ItemCode,
    IReadOnlyCollection<Guid> BranchIds);

/// <summary>What a branch may order, and the catalogue version that says so.</summary>
/// <remarks>
/// A null <paramref name="VersionId"/> and an empty <paramref name="Services"/> travel together: they
/// are the honest answer when nothing is published, and neither can be true without the other.
/// </remarks>
/// <param name="VersionId">The published version an order placed now would be pinned to, or null.</param>
/// <param name="Services">What the branch may order, in category then service code order.</param>
public sealed record OrderableCatalog(
    Guid? VersionId,
    IReadOnlyList<CatalogServiceSnapshot> Services)
{
    /// <summary>Nothing is published, so nothing is orderable.</summary>
    public static OrderableCatalog None { get; } = new(null, []);
}

/// <summary>One service type, its category and its five links, as one version fixed them.</summary>
/// <param name="ServiceTypeId">The service type's row identity in its version.</param>
/// <param name="ServiceTypeKey">Its identity as a concept, carried across versions.</param>
/// <param name="CatalogVersionId">The version this answer came from.</param>
/// <param name="CategoryId">The category's row identity in the same version.</param>
/// <param name="CategoryCode">The category's machine key.</param>
/// <param name="CategoryName">The category's label at the time.</param>
/// <param name="ServiceCode">The service's machine key, unique within its category.</param>
/// <param name="ServiceName">The service's label at the time.</param>
/// <param name="ExpectedDurationDays">The default due-date offset in working days.</param>
/// <param name="IntakeWarning">What the counter is warned about, or null.</param>
/// <param name="MeasurementTemplateId">Link 1 (#27).</param>
/// <param name="WorkflowDefinitionId">Link 2 (#33).</param>
/// <param name="DesignOptionGroupIds">Link 3, in display order (#30).</param>
/// <param name="PriceListItemCode">Link 4 (#41).</param>
/// <param name="QcChecklistTemplateId">Link 5 (#34).</param>
/// <param name="NotOrderable">Whether it was published with a link missing.</param>
public sealed record CatalogServiceSnapshot(
    Guid ServiceTypeId,
    Guid ServiceTypeKey,
    Guid CatalogVersionId,
    Guid CategoryId,
    string CategoryCode,
    string CategoryName,
    string ServiceCode,
    string ServiceName,
    int ExpectedDurationDays,
    string? IntakeWarning,
    Guid? MeasurementTemplateId,
    Guid? WorkflowDefinitionId,
    IReadOnlyList<Guid> DesignOptionGroupIds,
    string? PriceListItemCode,
    Guid? QcChecklistTemplateId,
    bool NotOrderable)
{
    /// <summary>
    /// The <c>CATEGORY_CODE.SERVICE_CODE</c> reference price lists, seed files, event payloads and
    /// exports use (<c>docs/prd/category-hierarchy.md</c> section 4).
    /// </summary>
    public string QualifiedReference => $"{CategoryCode}.{ServiceCode}";
}
