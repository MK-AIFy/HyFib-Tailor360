namespace Tailor360.Modules.Catalog.Domain.Catalogue;

/// <summary>
/// A stitching category — a garment kind the shop takes work on — as one catalogue version holds it.
/// </summary>
/// <remarks>
/// <para>
/// <strong>A category row belongs to one version.</strong> Cloning a version copies every category
/// into it with a fresh <see cref="Id"/>, so a published row is never edited and a draft can be worked
/// on freely. What survives the copy is <see cref="Key"/>, the identity of the category as a
/// <em>concept</em>: "the Aari blouse category" is one thing across every version that has ever held
/// it, and the key is what says so. Without it, "the code changed on an already-published record"
/// could not be checked at all, because every draft's rows are new rows.
/// </para>
/// <para>
/// <strong>A category with children is a grouping node.</strong> <c>BLOUSE</c> carries no service
/// types and cannot be ordered against; orders are placed against <c>BLOUSE_PATTERN</c> or
/// <c>BLOUSE_AARI</c>. That is derived rather than stored — a category is a grouping node exactly when
/// something else names it as a parent — because storing it would let the flag and the hierarchy
/// disagree, and the hierarchy is the thing that is true.
/// </para>
/// </remarks>
public sealed class Category
{
    private readonly List<CategoryBranch> _branches = [];

    private Category()
    {
        // The persistence layer materialises instances through this constructor.
    }

    internal Category(
        Guid id,
        Guid key,
        Guid catalogVersionId,
        Guid organisationId,
        Guid? parentId,
        CategoryDetails details)
    {
        Id = id;
        Key = key;
        CatalogVersionId = catalogVersionId;
        OrganisationId = organisationId;
        ParentId = parentId;

        Apply(details);
    }

    /// <summary>Identity of this row. New in every version, and what the API addresses.</summary>
    public Guid Id { get; private set; }

    /// <summary>
    /// Identity of the category as a concept, carried unchanged from version to version.
    /// </summary>
    /// <remarks>
    /// Never exposed as a route parameter and never the thing an administrator picks. It exists so
    /// that publish validation can ask "what was this category called last time it was published?"
    /// and get an answer.
    /// </remarks>
    public Guid Key { get; private set; }

    /// <summary>The version this row belongs to.</summary>
    public Guid CatalogVersionId { get; private set; }

    /// <summary>The organisation the catalogue belongs to.</summary>
    public Guid OrganisationId { get; private set; }

    /// <summary>The parent category in the same version, or null for a top-level category.</summary>
    public Guid? ParentId { get; private set; }

    /// <summary>The machine key. Immutable once its version is published.</summary>
    public string Code { get; private set; } = string.Empty;

    /// <summary>The label staff and customers read.</summary>
    public string Name { get; private set; } = string.Empty;

    /// <summary>The Tamil label, where one is confirmed.</summary>
    public string? NameTamil { get; private set; }

    /// <summary>What the category covers.</summary>
    public string? Description { get; private set; }

    /// <summary>Where the category sits among its siblings.</summary>
    public int DisplayOrder { get; private set; }

    /// <summary>The first day the category is offered, or null for always.</summary>
    public DateOnly? ActiveFrom { get; private set; }

    /// <summary>The last day, or null for indefinitely.</summary>
    public DateOnly? ActiveTo { get; private set; }

    /// <summary>The flag that can switch the category off, or null.</summary>
    public string? FeatureFlagKey { get; private set; }

    /// <summary>The branches that offer the category.</summary>
    public IReadOnlyCollection<CategoryBranch> Branches => _branches;

    /// <summary>The branches that offer the category, as identifiers.</summary>
    public IEnumerable<Guid> BranchIds => _branches.Select(branch => branch.BranchId);

    /// <summary>Whether the category is within its active period on a given day.</summary>
    /// <param name="on">
    /// The day, in the branch's timezone. A date and not an instant: "offered from the first of
    /// October" is a statement about the shop's calendar, and evaluating it in UTC would open the
    /// category five and a half hours early (<c>docs/architecture/conventions.md</c> 2.4).
    /// </param>
    /// <returns>True when the day falls inside the active period.</returns>
    public bool IsActiveOn(DateOnly on)
        => (ActiveFrom is not { } from || on >= from)
           && (ActiveTo is not { } to || on <= to);

    /// <summary>Replaces everything an administrator says about the category.</summary>
    /// <param name="details">The validated details.</param>
    internal void Apply(CategoryDetails details)
    {
        Code = details.Code;
        Name = details.Name;
        NameTamil = details.NameTamil;
        Description = details.Description;
        DisplayOrder = details.DisplayOrder;
        ActiveFrom = details.ActiveFrom;
        ActiveTo = details.ActiveTo;
        FeatureFlagKey = details.FeatureFlagKey;

        _branches.Clear();
        foreach (var branchId in details.BranchIds.Distinct())
        {
            _branches.Add(CategoryBranch.For(Id, branchId));
        }
    }

    /// <summary>Corrects the presentation fields of a published category.</summary>
    /// <param name="presentation">The validated correction.</param>
    internal void ApplyPresentation(CatalogPresentation presentation)
    {
        Name = presentation.Name;
        NameTamil = presentation.NameTamil;
        Description = presentation.Description;
        DisplayOrder = presentation.DisplayOrder;
    }

    /// <summary>Moves the category under a different parent.</summary>
    /// <param name="parentId">The new parent, or null to make it top-level.</param>
    internal void Reparent(Guid? parentId) => ParentId = parentId;

    /// <summary>Copies the category into a new version.</summary>
    /// <param name="id">The new row's identity.</param>
    /// <param name="catalogVersionId">The version being built.</param>
    /// <param name="parentId">The copy of this category's parent in the new version.</param>
    /// <returns>The copy, carrying the same <see cref="Key"/>.</returns>
    internal Category CopyInto(Guid id, Guid catalogVersionId, Guid? parentId)
        => new(
            id,
            Key,
            catalogVersionId,
            OrganisationId,
            parentId,
            new CategoryDetails(
                Code,
                Name,
                NameTamil,
                Description,
                DisplayOrder,
                ActiveFrom,
                ActiveTo,
                FeatureFlagKey,
                [.. BranchIds]));
}
