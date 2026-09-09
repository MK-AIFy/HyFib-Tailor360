namespace Tailor360.Modules.Catalog.Domain.Catalogue;

/// <summary>A branch that offers a service type.</summary>
/// <remarks>
/// Narrower than its category's set and never wider: a branch that does not offer the category cannot
/// offer one of its services. The subset rule is checked at publish rather than on every edit, because
/// a draft passes through inconsistent states while an administrator moves availability about.
/// </remarks>
public sealed class ServiceTypeBranch
{
    private ServiceTypeBranch()
    {
        // The persistence layer materialises instances through this constructor.
    }

    private ServiceTypeBranch(Guid serviceTypeId, Guid branchId)
    {
        ServiceTypeId = serviceTypeId;
        BranchId = branchId;
    }

    /// <summary>The service type offered.</summary>
    public Guid ServiceTypeId { get; private set; }

    /// <summary>The branch that offers it.</summary>
    public Guid BranchId { get; private set; }

    /// <summary>Records that a branch offers a service type.</summary>
    /// <param name="serviceTypeId">The service type.</param>
    /// <param name="branchId">The branch.</param>
    /// <returns>The row.</returns>
    public static ServiceTypeBranch For(Guid serviceTypeId, Guid branchId) => new(serviceTypeId, branchId);
}
