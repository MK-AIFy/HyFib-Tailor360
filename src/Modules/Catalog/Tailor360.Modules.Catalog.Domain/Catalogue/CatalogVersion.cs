using Tailor360.Platform.Abstractions.Identifiers;
using Tailor360.Platform.Abstractions.Results;

namespace Tailor360.Modules.Catalog.Domain.Catalogue;

/// <summary>
/// One coherent snapshot of the whole catalogue: the hierarchy, its service types and their links.
/// </summary>
/// <remarks>
/// <para>
/// <strong>The version is the aggregate, not the category.</strong> That is the decision the rest of
/// this file follows from. An order is placed against exactly one published version
/// (<c>docs/prd/category-hierarchy.md</c> section 7), so the thing that has to be internally
/// consistent — no duplicate codes, no orphaned parents, no cycles, availability narrowing as it
/// descends — is the whole tree at once. Making the category the aggregate would put every one of
/// those rules across an aggregate boundary, where nothing could enforce them.
/// </para>
/// <para>
/// <strong>The lifecycle is one-way and the domain enforces only the states.</strong> Draft becomes
/// published; published becomes retired; nothing returns. What the domain does <em>not</em> decide is
/// whether a draft is fit to publish: that is the registered validators' answer, and it needs the
/// other modules, so it is asked in the application layer and this type is told the result.
/// </para>
/// <para>
/// <strong>A published version is immutable but for its presentation.</strong> The methods here refuse
/// an edit once <see cref="Status"/> leaves <see cref="CatalogStatus.Draft"/>, and a database trigger
/// refuses the same thing to anybody who reaches the tables with psql. Both exist because "immutable"
/// that only the application believes is not immutable.
/// </para>
/// </remarks>
public sealed partial class CatalogVersion
{
    /// <summary>The longest version name the column holds.</summary>
    public const int MaximumNameLength = 120;

    /// <summary>The longest note the column holds.</summary>
    public const int MaximumNotesLength = 2000;

    /// <summary>The longest reason the column holds.</summary>
    public const int MaximumReasonLength = 1000;

    private readonly List<Category> _categories = [];
    private readonly List<ServiceType> _serviceTypes = [];

    private CatalogVersion()
    {
        // The persistence layer materialises instances through this constructor.
    }

    private CatalogVersion(
        Guid id,
        Guid organisationId,
        int versionNumber,
        string name,
        string? notes,
        Guid? clonedFromVersionId,
        DateTimeOffset now,
        Guid? by)
    {
        Id = id;
        OrganisationId = organisationId;
        VersionNumber = versionNumber;
        Name = name;
        Notes = notes;
        ClonedFromVersionId = clonedFromVersionId;
        Status = CatalogStatus.Draft;
        CreatedAt = now;
        CreatedBy = by;
        UpdatedAt = now;
        UpdatedBy = by;
    }

    /// <summary>Identity of the version. A UUIDv7, and what every route and pin refers to.</summary>
    public Guid Id { get; private set; }

    /// <summary>The organisation the catalogue belongs to.</summary>
    public Guid OrganisationId { get; private set; }

    /// <summary>
    /// The version's number, counting from one.
    /// </summary>
    /// <remarks>
    /// A label, never a key. <c>docs/architecture/conventions.md</c> section 3.1 forbids exposing a
    /// sequential identifier, and this is not one: it is what an administrator says out loud
    /// ("we published version four this morning"), while every reference in a route, an event or a pin
    /// is <see cref="Id"/>.
    /// </remarks>
    public int VersionNumber { get; private set; }

    /// <summary>What this version is for, in an administrator's words.</summary>
    public string Name { get; private set; } = string.Empty;

    /// <summary>Longer notes for the reviewer, where there are any.</summary>
    public string? Notes { get; private set; }

    /// <summary>Where the version stands in its life.</summary>
    public CatalogStatus Status { get; private set; }

    /// <summary>The version this one was cloned from, or null when it was started empty.</summary>
    public Guid? ClonedFromVersionId { get; private set; }

    /// <summary>When the draft was created.</summary>
    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>Who created it.</summary>
    public Guid? CreatedBy { get; private set; }

    /// <summary>When it was last changed.</summary>
    public DateTimeOffset UpdatedAt { get; private set; }

    /// <summary>Who last changed it.</summary>
    public Guid? UpdatedBy { get; private set; }

    /// <summary>When it was published, or null while it is a draft.</summary>
    public DateTimeOffset? PublishedAt { get; private set; }

    /// <summary>Who published it.</summary>
    public Guid? PublishedBy { get; private set; }

    /// <summary>Why it was published. Publication demands a reason.</summary>
    public string? PublishReason { get; private set; }

    /// <summary>When it was retired, or null while it is not.</summary>
    public DateTimeOffset? RetiredAt { get; private set; }

    /// <summary>Who retired it.</summary>
    public Guid? RetiredBy { get; private set; }

    /// <summary>Why it was retired. Retirement demands a reason.</summary>
    public string? RetiredReason { get; private set; }

    /// <summary>The categories in this version.</summary>
    public IReadOnlyCollection<Category> Categories => _categories;

    /// <summary>The service types in this version.</summary>
    public IReadOnlyCollection<ServiceType> ServiceTypes => _serviceTypes;

    /// <summary>Whether the version may still be edited.</summary>
    public bool IsEditable => Status == CatalogStatus.Draft;

    /// <summary>Starts an empty draft.</summary>
    /// <param name="id">The new version's identity.</param>
    /// <param name="organisationId">The organisation.</param>
    /// <param name="versionNumber">The next number in the organisation's sequence.</param>
    /// <param name="name">What this version is for.</param>
    /// <param name="notes">Longer notes for the reviewer.</param>
    /// <param name="now">The current instant, from the clock abstraction.</param>
    /// <param name="by">The administrator, or null for a seeding run.</param>
    /// <returns>The draft, or the reason it could not be started.</returns>
    public static Result<CatalogVersion> CreateDraft(
        Guid id,
        Guid organisationId,
        int versionNumber,
        string name,
        string? notes,
        DateTimeOffset now,
        Guid? by)
    {
        var described = Describe(name, notes);

        return described.IsFailure
            ? Result.Failure<CatalogVersion>(described.Error)
            : Result.Success(
                new CatalogVersion(id, organisationId, versionNumber, name, notes, null, now, by));
    }

    /// <summary>Copies this version into a new draft.</summary>
    /// <remarks>
    /// The whole tree is copied with fresh row identities and the same
    /// <see cref="Category.Key"/> and <see cref="ServiceType.Key"/>, so the new draft is a different
    /// set of rows describing the same categories. Parents are re-pointed at the copies, which is why
    /// the categories are copied parents-first rather than in whatever order they arrived in.
    /// </remarks>
    /// <param name="ids">The identifier generator, so no row is minted with <c>Guid.NewGuid</c>.</param>
    /// <param name="versionNumber">The next number in the organisation's sequence.</param>
    /// <param name="name">What the new version is for.</param>
    /// <param name="notes">Longer notes for the reviewer.</param>
    /// <param name="now">The current instant.</param>
    /// <param name="by">The administrator.</param>
    /// <returns>The new draft, or the reason it could not be started.</returns>
    public Result<CatalogVersion> CloneAsDraft(
        IIdGenerator ids,
        int versionNumber,
        string name,
        string? notes,
        DateTimeOffset now,
        Guid? by)
    {
        ArgumentNullException.ThrowIfNull(ids);

        var described = Describe(name, notes);

        if (described.IsFailure)
        {
            return Result.Failure<CatalogVersion>(described.Error);
        }

        var clone = new CatalogVersion(
            ids.NewId(), OrganisationId, versionNumber, name, notes, Id, now, by);

        var copiedCategories = new Dictionary<Guid, Guid>();

        foreach (var category in InParentFirstOrder())
        {
            var parentId = category.ParentId is { } original ? copiedCategories[original] : (Guid?)null;
            var copy = category.CopyInto(ids.NewId(), clone.Id, parentId);

            copiedCategories[category.Id] = copy.Id;
            clone._categories.Add(copy);
        }

        // The groups and rules go first, because a service type's design link names group rows and
        // the copies must point at the copies, not at the rows of the version being cloned.
        var copiedGroups = clone.CopyDesignFrom(this, ids, copiedCategories);

        foreach (var service in _serviceTypes)
        {
            clone._serviceTypes.Add(
                service.CopyInto(
                    ids.NewId(), clone.Id, copiedCategories[service.CategoryId], copiedGroups));
        }

        return Result.Success(clone);
    }

    /// <summary>Adds a category to the draft.</summary>
    /// <param name="id">The new row's identity.</param>
    /// <param name="key">The category's identity as a concept, new for a category nobody has seen.</param>
    /// <param name="parentId">The parent category in this version, or null for top level.</param>
    /// <param name="details">What the administrator says about it.</param>
    /// <param name="now">The current instant.</param>
    /// <param name="by">The administrator.</param>
    /// <returns>The category, or the reason it could not be added.</returns>
    public Result<Category> AddCategory(
        Guid id,
        Guid key,
        Guid? parentId,
        CategoryDetails details,
        DateTimeOffset now,
        Guid? by)
    {
        ArgumentNullException.ThrowIfNull(details);

        if (!IsEditable)
        {
            return Result.Failure<Category>(CatalogErrors.VersionNotEditable);
        }

        var validated = details.Validate();

        if (validated.IsFailure)
        {
            return Result.Failure<Category>(validated.Error);
        }

        if (_categories.Any(existing => string.Equals(existing.Code, details.Code, StringComparison.Ordinal)))
        {
            return Result.Failure<Category>(CatalogErrors.CodeNotUnique("code"));
        }

        if (parentId is { } parent && _categories.All(existing => existing.Id != parent))
        {
            return Result.Failure<Category>(CatalogErrors.ParentNotFound);
        }

        var category = new Category(id, key, Id, OrganisationId, parentId, details);

        _categories.Add(category);
        Touch(now, by);

        return Result.Success(category);
    }

    /// <summary>Replaces what the draft says about a category.</summary>
    /// <param name="categoryId">The category in this version.</param>
    /// <param name="parentId">The parent it should have, or null for top level.</param>
    /// <param name="details">What the administrator now says about it.</param>
    /// <param name="now">The current instant.</param>
    /// <param name="by">The administrator.</param>
    /// <returns>The category, or the reason it could not be changed.</returns>
    public Result<Category> EditCategory(
        Guid categoryId,
        Guid? parentId,
        CategoryDetails details,
        DateTimeOffset now,
        Guid? by)
    {
        ArgumentNullException.ThrowIfNull(details);

        if (!IsEditable)
        {
            return Result.Failure<Category>(CatalogErrors.VersionNotEditable);
        }

        if (Find(categoryId) is not { } category)
        {
            return Result.Failure<Category>(CatalogErrors.CategoryNotFound);
        }

        var validated = details.Validate();

        if (validated.IsFailure)
        {
            return Result.Failure<Category>(validated.Error);
        }

        if (_categories.Any(other =>
                other.Id != categoryId
                && string.Equals(other.Code, details.Code, StringComparison.Ordinal)))
        {
            return Result.Failure<Category>(CatalogErrors.CodeNotUnique("code"));
        }

        if (parentId is { } parent)
        {
            if (_categories.All(existing => existing.Id != parent))
            {
                return Result.Failure<Category>(CatalogErrors.ParentNotFound);
            }

            if (WouldCycle(categoryId, parent))
            {
                return Result.Failure<Category>(CatalogErrors.HierarchyWouldCycle);
            }
        }

        category.Reparent(parentId);
        category.Apply(details);
        Touch(now, by);

        return Result.Success(category);
    }

    /// <summary>Removes a category, and every service type and descendant beneath it, from the draft.</summary>
    /// <remarks>
    /// Descendants go with it deliberately. Leaving them would produce exactly the orphaned parent the
    /// publish validation refuses, discovered minutes later by somebody who did not make the change.
    /// </remarks>
    /// <param name="categoryId">The category in this version.</param>
    /// <param name="now">The current instant.</param>
    /// <param name="by">The administrator.</param>
    /// <returns>Success, or the reason it could not be removed.</returns>
    public Result RemoveCategory(Guid categoryId, DateTimeOffset now, Guid? by)
    {
        if (!IsEditable)
        {
            return Result.Failure(CatalogErrors.VersionNotEditable);
        }

        if (Find(categoryId) is null)
        {
            return Result.Failure(CatalogErrors.CategoryNotFound);
        }

        var removing = DescendantsOf(categoryId).Append(categoryId).ToHashSet();

        _serviceTypes.RemoveAll(service => removing.Contains(service.CategoryId));
        RemoveDesignOf(removing);
        _categories.RemoveAll(category => removing.Contains(category.Id));
        Touch(now, by);

        return Result.Success();
    }

    /// <summary>Adds a service type to a category in the draft.</summary>
    /// <param name="id">The new row's identity.</param>
    /// <param name="key">The service type's identity as a concept.</param>
    /// <param name="categoryId">The category that offers it.</param>
    /// <param name="details">What the administrator says about it.</param>
    /// <param name="now">The current instant.</param>
    /// <param name="by">The administrator.</param>
    /// <returns>The service type, or the reason it could not be added.</returns>
    public Result<ServiceType> AddServiceType(
        Guid id,
        Guid key,
        Guid categoryId,
        ServiceTypeDetails details,
        DateTimeOffset now,
        Guid? by)
    {
        ArgumentNullException.ThrowIfNull(details);

        if (!IsEditable)
        {
            return Result.Failure<ServiceType>(CatalogErrors.VersionNotEditable);
        }

        if (Find(categoryId) is null)
        {
            return Result.Failure<ServiceType>(CatalogErrors.CategoryNotFound);
        }

        var validated = details.Validate();

        if (validated.IsFailure)
        {
            return Result.Failure<ServiceType>(validated.Error);
        }

        if (ServicesOf(categoryId).Any(existing =>
                string.Equals(existing.Code, details.Code, StringComparison.Ordinal)))
        {
            return Result.Failure<ServiceType>(CatalogErrors.CodeNotUnique("code"));
        }

        var service = new ServiceType(id, key, Id, OrganisationId, categoryId, details);

        _serviceTypes.Add(service);
        Touch(now, by);

        return Result.Success(service);
    }

    /// <summary>Replaces what the draft says about a service type.</summary>
    /// <param name="serviceTypeId">The service type in this version.</param>
    /// <param name="details">What the administrator now says about it.</param>
    /// <param name="now">The current instant.</param>
    /// <param name="by">The administrator.</param>
    /// <returns>The service type, or the reason it could not be changed.</returns>
    public Result<ServiceType> EditServiceType(
        Guid serviceTypeId,
        ServiceTypeDetails details,
        DateTimeOffset now,
        Guid? by)
    {
        ArgumentNullException.ThrowIfNull(details);

        if (!IsEditable)
        {
            return Result.Failure<ServiceType>(CatalogErrors.VersionNotEditable);
        }

        if (FindService(serviceTypeId) is not { } service)
        {
            return Result.Failure<ServiceType>(CatalogErrors.ServiceTypeNotFound);
        }

        var validated = details.Validate();

        if (validated.IsFailure)
        {
            return Result.Failure<ServiceType>(validated.Error);
        }

        if (ServicesOf(service.CategoryId).Any(other =>
                other.Id != serviceTypeId
                && string.Equals(other.Code, details.Code, StringComparison.Ordinal)))
        {
            return Result.Failure<ServiceType>(CatalogErrors.CodeNotUnique("code"));
        }

        service.Apply(details);
        Touch(now, by);

        return Result.Success(service);
    }

    /// <summary>Removes a service type from the draft.</summary>
    /// <param name="serviceTypeId">The service type in this version.</param>
    /// <param name="now">The current instant.</param>
    /// <param name="by">The administrator.</param>
    /// <returns>Success, or the reason it could not be removed.</returns>
    public Result RemoveServiceType(Guid serviceTypeId, DateTimeOffset now, Guid? by)
    {
        if (!IsEditable)
        {
            return Result.Failure(CatalogErrors.VersionNotEditable);
        }

        if (FindService(serviceTypeId) is not { } service)
        {
            return Result.Failure(CatalogErrors.ServiceTypeNotFound);
        }

        _serviceTypes.Remove(service);
        Touch(now, by);

        return Result.Success();
    }

    /// <summary>Publishes the draft.</summary>
    /// <remarks>
    /// The state machine and nothing else. Whether the draft is <em>fit</em> to publish is the
    /// registered validators' answer and is asked before this is called, and whether a service may be
    /// ordered is derived from its links rather than settled here — a published version is immutable,
    /// so a flag written onto one of its rows at the moment of publication is a write the database
    /// refuses.
    /// </remarks>
    /// <param name="now">The current instant.</param>
    /// <param name="by">The administrator.</param>
    /// <param name="reason">Why it is being published. Required.</param>
    /// <returns>Success, or the reason it could not be published.</returns>
    public Result Publish(DateTimeOffset now, Guid? by, string reason)
    {
        if (Status != CatalogStatus.Draft)
        {
            return Result.Failure(CatalogErrors.VersionNotPublishable);
        }

        var reasoned = CheckReason(reason);

        if (reasoned.IsFailure)
        {
            return reasoned;
        }

        Status = CatalogStatus.Published;
        PublishedAt = now;
        PublishedBy = by;
        PublishReason = reason;
        Touch(now, by);

        return Result.Success();
    }

    /// <summary>Retires the published version.</summary>
    /// <remarks>
    /// Whether retiring would strand work in progress is not decided here — it needs the Orders
    /// module — so the caller asks first and this method is reached only once the answer is no.
    /// </remarks>
    /// <param name="now">The current instant.</param>
    /// <param name="by">The administrator.</param>
    /// <param name="reason">Why it is being retired. Required.</param>
    /// <returns>Success, or the reason it could not be retired.</returns>
    public Result Retire(DateTimeOffset now, Guid? by, string reason)
    {
        if (Status != CatalogStatus.Published)
        {
            return Result.Failure(CatalogErrors.VersionNotRetirable);
        }

        var reasoned = CheckReason(reason);

        if (reasoned.IsFailure)
        {
            return reasoned;
        }

        Status = CatalogStatus.Retired;
        RetiredAt = now;
        RetiredBy = by;
        RetiredReason = reason;
        Touch(now, by);

        return Result.Success();
    }

    /// <summary>Corrects the label, Tamil label, description or display order of a published category.</summary>
    /// <remarks>
    /// The reason is checked here rather than at the endpoint, for the same reason publication and
    /// retirement check theirs here: this is the one mutation a published version admits, and an
    /// invariant that lives in the transport is not an invariant. It is not stored on the row — the row
    /// is immutable but for the four presentation columns — so it reaches the audit trail and nothing
    /// else, which is where a reader looks to find out why a published label reads differently today.
    /// </remarks>
    /// <param name="categoryId">The category in this version.</param>
    /// <param name="presentation">The correction.</param>
    /// <param name="reason">Why. A correction to a published version demands one.</param>
    /// <param name="now">The current instant.</param>
    /// <param name="by">The administrator.</param>
    /// <returns>The category, or the reason it could not be corrected.</returns>
    public Result<Category> CorrectCategoryPresentation(
        Guid categoryId,
        CatalogPresentation presentation,
        string reason,
        DateTimeOffset now,
        Guid? by)
    {
        ArgumentNullException.ThrowIfNull(presentation);

        if (Status == CatalogStatus.Draft)
        {
            return Result.Failure<Category>(CatalogErrors.CorrectionNeedsPublishedVersion);
        }

        if (Find(categoryId) is not { } category)
        {
            return Result.Failure<Category>(CatalogErrors.CategoryNotFound);
        }

        var validated = presentation.Validate();

        if (validated.IsFailure)
        {
            return Result.Failure<Category>(validated.Error);
        }

        var reasoned = CheckReason(reason);

        if (reasoned.IsFailure)
        {
            return Result.Failure<Category>(reasoned.Error);
        }

        category.ApplyPresentation(presentation);
        Touch(now, by);

        return Result.Success(category);
    }

    /// <summary>Corrects the presentation fields of a published service type.</summary>
    /// <param name="serviceTypeId">The service type in this version.</param>
    /// <param name="presentation">The correction.</param>
    /// <param name="reason">Why. A correction to a published version demands one.</param>
    /// <param name="now">The current instant.</param>
    /// <param name="by">The administrator.</param>
    /// <returns>The service type, or the reason it could not be corrected.</returns>
    public Result<ServiceType> CorrectServiceTypePresentation(
        Guid serviceTypeId,
        CatalogPresentation presentation,
        string reason,
        DateTimeOffset now,
        Guid? by)
    {
        ArgumentNullException.ThrowIfNull(presentation);

        if (Status == CatalogStatus.Draft)
        {
            return Result.Failure<ServiceType>(CatalogErrors.CorrectionNeedsPublishedVersion);
        }

        if (FindService(serviceTypeId) is not { } service)
        {
            return Result.Failure<ServiceType>(CatalogErrors.ServiceTypeNotFound);
        }

        var validated = presentation.Validate();

        if (validated.IsFailure)
        {
            return Result.Failure<ServiceType>(validated.Error);
        }

        var reasoned = CheckReason(reason);

        if (reasoned.IsFailure)
        {
            return Result.Failure<ServiceType>(reasoned.Error);
        }

        service.ApplyPresentation(presentation);
        Touch(now, by);

        return Result.Success(service);
    }

    /// <summary>One category of this version, or null.</summary>
    /// <param name="categoryId">The category's row identity.</param>
    /// <returns>The category, or null when this version does not hold it.</returns>
    public Category? Find(Guid categoryId)
        => _categories.SingleOrDefault(category => category.Id == categoryId);

    /// <summary>One service type of this version, or null.</summary>
    /// <param name="serviceTypeId">The service type's row identity.</param>
    /// <returns>The service type, or null when this version does not hold it.</returns>
    public ServiceType? FindService(Guid serviceTypeId)
        => _serviceTypes.SingleOrDefault(service => service.Id == serviceTypeId);

    /// <summary>The service types a category offers.</summary>
    /// <param name="categoryId">The category.</param>
    /// <returns>Its service types.</returns>
    public IEnumerable<ServiceType> ServicesOf(Guid categoryId)
        => _serviceTypes.Where(service => service.CategoryId == categoryId);

    /// <summary>The categories directly beneath a category.</summary>
    /// <param name="categoryId">The parent.</param>
    /// <returns>Its children.</returns>
    public IEnumerable<Category> ChildrenOf(Guid categoryId)
        => _categories.Where(category => category.ParentId == categoryId);

    /// <summary>
    /// Whether a category is a grouping node — something orders are never placed against.
    /// </summary>
    /// <remarks>
    /// Derived from the hierarchy rather than stored, so the flag and the tree cannot disagree.
    /// <c>BLOUSE</c> is the seeded example: it exists to group its two sub-categories and carries no
    /// service types of its own.
    /// </remarks>
    /// <param name="categoryId">The category.</param>
    /// <returns>True when something names it as a parent.</returns>
    public bool IsGroupingNode(Guid categoryId)
        => _categories.Any(category => category.ParentId == categoryId);

    /// <summary>The categories, parents before their children.</summary>
    /// <remarks>
    /// A breadth-first walk from the top-level categories. It visits only what is reachable, so a row
    /// whose parent is missing — the orphan the publish validation refuses — is deliberately absent
    /// from the result rather than crashing the walk.
    /// </remarks>
    /// <returns>The reachable categories, parents first.</returns>
    public IEnumerable<Category> InParentFirstOrder()
    {
        var byParent = _categories
            .Where(category => category.ParentId is not null)
            .ToLookup(category => category.ParentId!.Value);

        var queue = new Queue<Category>(
            _categories.Where(category => category.ParentId is null).OrderBy(category => category.Code, StringComparer.Ordinal));

        while (queue.Count > 0)
        {
            var category = queue.Dequeue();

            yield return category;

            foreach (var child in byParent[category.Id].OrderBy(child => child.Code, StringComparer.Ordinal))
            {
                queue.Enqueue(child);
            }
        }
    }

    /// <summary>Every category beneath one, however deep.</summary>
    /// <param name="categoryId">The ancestor.</param>
    /// <returns>Its descendants.</returns>
    public IReadOnlySet<Guid> DescendantsOf(Guid categoryId)
    {
        var found = new HashSet<Guid>();
        var queue = new Queue<Guid>([categoryId]);

        while (queue.Count > 0)
        {
            var current = queue.Dequeue();

            foreach (var child in _categories.Where(category => category.ParentId == current))
            {
                if (found.Add(child.Id))
                {
                    queue.Enqueue(child.Id);
                }
            }
        }

        return found;
    }

    private bool WouldCycle(Guid categoryId, Guid parentId)
        => parentId == categoryId || DescendantsOf(categoryId).Contains(parentId);

    private void Touch(DateTimeOffset now, Guid? by)
    {
        UpdatedAt = now;
        UpdatedBy = by;
    }

    private static Result Describe(string name, string? notes)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return Result.Failure(CatalogErrors.Required("name"));
        }

        if (name.Length > MaximumNameLength)
        {
            return Result.Failure(CatalogErrors.TooLong("name", MaximumNameLength));
        }

        return notes is { Length: > MaximumNotesLength }
            ? Result.Failure(CatalogErrors.TooLong("notes", MaximumNotesLength))
            : Result.Success();
    }

    private static Result CheckReason(string reason)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            return Result.Failure(CatalogErrors.Required("reason"));
        }

        return reason.Length > MaximumReasonLength
            ? Result.Failure(CatalogErrors.TooLong("reason", MaximumReasonLength))
            : Result.Success();
    }
}
