namespace Tailor360.Platform.Security.FieldVisibility;

/// <summary>
/// Every response view the application declares, grouped by the module that owns it.
/// </summary>
/// <remarks>
/// <para>
/// Composed here for the same reason the permission catalogue is: the views have to be complete, and
/// approvable, before the endpoints that return them exist. All three declared today are forward
/// declarations — no endpoint returns any of them yet — and each traces to a named surface in the
/// product documentation rather than to a screen somebody imagined.
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
