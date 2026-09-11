namespace Tailor360.Modules.Orders.Domain.Estimates;

/// <summary>
/// Where an estimate stands in its sub-lifecycle.
/// </summary>
/// <remarks>
/// <para>
/// Three values and no fourth. <strong>Expired is deliberately not one of them</strong>: expiry is a derived
/// display over the validity date, not a stored status, because an estimate past its date is reissued at
/// current prices and never silently honoured (<c>docs/prd/state-transitions.md</c> section 2.2). A stored
/// expiry would need a writer, and the only honest writer would be a clock — which would leave the shop
/// treating an estimate as live or dead depending on when a background job last ran.
/// </para>
/// <para>
/// An estimate is a priced snapshot of a draft and not a stage of the order, so this is a sub-lifecycle beside
/// the order lifecycle rather than inside it (<c>docs/IMPLEMENTATION_PLAN.md</c> #32a).
/// </para>
/// </remarks>
public enum EstimateStatus
{
    /// <summary>Issued and outstanding. The only state from which the other two are reachable.</summary>
    Issued = 0,

    /// <summary>
    /// A newer estimate has been issued for the same draft. The superseded link stops resolving; the rendered
    /// document remains stored with its original checksum.
    /// </summary>
    Superseded = 1,

    /// <summary>The draft was confirmed into an order.</summary>
    Converted = 2,
}
