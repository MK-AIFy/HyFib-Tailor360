namespace Tailor360.Modules.Catalog.Domain.Catalogue;

/// <summary>A branch that offers a category.</summary>
/// <remarks>
/// A set rather than a flag, because availability is per branch and a shop with three branches may
/// offer Aari work at one of them. An <em>empty</em> set means the category is offered nowhere, which
/// is a deliberate reading: a category an administrator has not yet made available anywhere is not
/// silently available everywhere.
/// </remarks>
public sealed class CategoryBranch
{
    private CategoryBranch()
    {
        // The persistence layer materialises instances through this constructor.
    }

    private CategoryBranch(Guid categoryId, Guid branchId)
    {
        CategoryId = categoryId;
        BranchId = branchId;
    }

    /// <summary>The category offered.</summary>
    public Guid CategoryId { get; private set; }

    /// <summary>The branch that offers it.</summary>
    public Guid BranchId { get; private set; }

    /// <summary>Records that a branch offers a category.</summary>
    /// <param name="categoryId">The category.</param>
    /// <param name="branchId">The branch.</param>
    /// <returns>The row.</returns>
    public static CategoryBranch For(Guid categoryId, Guid branchId) => new(categoryId, branchId);
}
