namespace Tailor360.Modules.Catalog.Domain.Design;

/// <summary>One branch that offers a design option group.</summary>
public sealed class DesignGroupBranch
{
    private DesignGroupBranch()
    {
        // The persistence layer materialises instances through this constructor.
    }

    private DesignGroupBranch(Guid designOptionGroupId, Guid branchId)
    {
        DesignOptionGroupId = designOptionGroupId;
        BranchId = branchId;
    }

    /// <summary>The group.</summary>
    public Guid DesignOptionGroupId { get; private set; }

    /// <summary>The branch that offers it.</summary>
    public Guid BranchId { get; private set; }

    /// <summary>Records that a branch offers a group.</summary>
    /// <param name="designOptionGroupId">The group.</param>
    /// <param name="branchId">The branch.</param>
    /// <returns>The row.</returns>
    public static DesignGroupBranch For(Guid designOptionGroupId, Guid branchId) => new(designOptionGroupId, branchId);
}
