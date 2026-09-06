namespace Tailor360.Platform.Security.Permissions;

/// <summary>
/// The whole permission catalogue, grouped by the module that owns each action.
/// </summary>
/// <remarks>
/// <para>
/// The catalogue is code in <c>Platform.Security</c> rather than data in a table, and it is composed
/// here rather than contributed by each module, for two reasons that the plan and
/// <c>docs/architecture/module-ownership.md</c> section 5.1 both state. A permission that only exists
/// once its module is composed into a host could not be granted to a role before that module shipped,
/// and the owner-approved matrix in <c>docs/security/permission-matrix.md</c> has to be complete —
/// including the modules still to be built — before the first endpoint enforces anything. Keeping the
/// list in one assembly also means the matrix test compares two artefacts, not eleven.
/// </para>
/// <para>
/// <see cref="IPermissionSource"/> stays as the seam. Nothing uses it today besides this type, and that
/// is the point: a module that later needs a permission the platform cannot sensibly name can
/// contribute one, and <see cref="PermissionCatalogue"/> will still refuse a key claimed twice.
/// </para>
/// <para>
/// Many entries here are <em>forward-declared</em>: their module exists as a project skeleton and their
/// endpoints do not exist yet. Every one of them traces to a documented action in
/// <c>docs/prd/raci.md</c> section 3, a transition in <c>docs/prd/state-transitions.md</c>, or a module
/// responsibility in <c>docs/architecture/module-ownership.md</c>. Which entries are forward-declared
/// is recorded in <c>docs/security/permission-matrix.md</c>, and the matrix test keeps the two lists
/// equal.
/// </para>
/// </remarks>
public sealed class ApplicationPermissions : IPermissionSource
{
    /// <summary>Every permission the application understands, grouped by owning module.</summary>
    public static IReadOnlyCollection<Permission> All { get; } =
    [
        .. PlatformPermissions.All,
        .. IdentityPermissions.All,
        .. CustomersPermissions.All,
        .. CatalogPermissions.All,
        .. MediaPermissions.All,
        .. OrdersPermissions.All,
        .. CustodyPermissions.All,
        .. InventoryPermissions.All,
        .. BillingPermissions.All,
        .. ReportingPermissions.All,
        .. NotificationsPermissions.All,
        .. IntegrationPermissions.All,
    ];

    /// <inheritdoc />
    public IReadOnlyCollection<Permission> Permissions => All;
}
