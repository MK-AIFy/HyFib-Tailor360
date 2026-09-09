namespace Tailor360.Modules.Catalog.Domain.Catalogue;

/// <summary>
/// One design option group a service type offers, in the order it is shown.
/// </summary>
/// <remarks>
/// The third of the five links a service type carries, and the only one that is a set rather than a
/// single reference (<c>docs/prd/category-hierarchy.md</c> section 5). The groups themselves belong to
/// issue #30 and are not modelled here; this row holds the identifier and the order, and #30's
/// validator is what refuses a reference to a group that is not published.
/// </remarks>
public sealed class ServiceTypeDesignGroup
{
    private ServiceTypeDesignGroup()
    {
        // The persistence layer materialises instances through this constructor.
    }

    private ServiceTypeDesignGroup(Guid serviceTypeId, Guid designOptionGroupId, int displayOrder)
    {
        ServiceTypeId = serviceTypeId;
        DesignOptionGroupId = designOptionGroupId;
        DisplayOrder = displayOrder;
    }

    /// <summary>The service type that offers the group.</summary>
    public Guid ServiceTypeId { get; private set; }

    /// <summary>The group, owned by the design catalogue (#30).</summary>
    public Guid DesignOptionGroupId { get; private set; }

    /// <summary>Where the group sits in the picker.</summary>
    public int DisplayOrder { get; private set; }

    /// <summary>Records that a service type offers a design option group.</summary>
    /// <param name="serviceTypeId">The service type.</param>
    /// <param name="designOptionGroupId">The group.</param>
    /// <param name="displayOrder">Where it sits in the picker.</param>
    /// <returns>The row.</returns>
    public static ServiceTypeDesignGroup For(Guid serviceTypeId, Guid designOptionGroupId, int displayOrder)
        => new(serviceTypeId, designOptionGroupId, displayOrder);
}
