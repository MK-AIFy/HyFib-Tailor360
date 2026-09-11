namespace Tailor360.Modules.Customers.Application.Measurements;

/// <summary>
/// The resource kinds this module's endpoints scope their authorisation to.
/// </summary>
/// <remarks>
/// In <c>Application</c> rather than beside the resolver, because both ends need it and they are in projects that
/// may not see each other: an endpoint in <c>Api</c> declares the kind, and a resolver in <c>Infrastructure</c>
/// answers for it. A literal at either end would go stale silently — the endpoint would declare a kind nothing
/// resolves, the middleware would find no resolver, and the branch check would pass for every signed-in caller,
/// which is exactly the failure the declaration exists to prevent.
/// </remarks>
public static class MeasurementResourceKinds
{
    /// <summary>A garment being measured, owned by the branch measuring it.</summary>
    public const string MeasurementDraft = "customers.measurement_draft";
}
