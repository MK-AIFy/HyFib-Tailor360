namespace Tailor360.Modules.Orders.Application.Drafts;

/// <summary>
/// The resource kinds Orders' draft routes scope their authorisation to (#199).
/// </summary>
/// <remarks>
/// In <c>Application</c> rather than beside the resolver, matching <c>DesignSelectionResourceKinds</c>
/// and <c>MeasurementResourceKinds</c>: an endpoint in <c>Api</c> declares the kind and a resolver in
/// <c>Infrastructure</c> answers for it, and the two projects may not see each other. This is the
/// module's first <see cref="Tailor360.Platform.Security.Authorisation.IResourceScopeResolver"/>. It
/// claims only the draft kind — <c>orders.order</c> and <c>orders.garment_job</c> belong to #201, which
/// confirms a draft into an immutable order and its garment jobs.
/// </remarks>
public static class OrdersResourceKinds
{
    /// <summary>An order being built at the counter, owned by the branch building it.</summary>
    public const string OrderDraft = "orders.order_draft";
}
