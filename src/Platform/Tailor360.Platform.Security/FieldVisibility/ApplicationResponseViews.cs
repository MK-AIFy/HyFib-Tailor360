namespace Tailor360.Platform.Security.FieldVisibility;

/// <summary>
/// Every response view the application declares, grouped by the module that owns it.
/// </summary>
/// <remarks>
/// <para>
/// Composed here for the same reason the permission catalogue is: the views have to be complete, and
/// approvable, before the endpoints that return them exist. Two of the five are served today — the
/// customer record and the search card, by the Customers endpoints of #26 — and the remaining three
/// are forward declarations waiting for the modules that will serve them. Each traces to a named
/// surface in the product documentation rather than to a screen somebody imagined.
/// </para>
/// <para>
/// <see cref="IResponseViewSource"/> is the seam a module uses when it needs a view the platform cannot
/// sensibly name. Nothing uses it besides this type today, and <see cref="ResponseViewCatalogue"/> still
/// refuses a key claimed twice.
/// </para>
/// </remarks>
public sealed class ApplicationResponseViews : IResponseViewSource
{
    /// <summary>Every declared view.</summary>
    public static IReadOnlyCollection<ResponseView> All { get; } =
    [
        .. CustomersResponseViews.All,
        .. OrdersResponseViews.All,
    ];

    /// <inheritdoc />
    public IReadOnlyCollection<ResponseView> Views => All;
}
