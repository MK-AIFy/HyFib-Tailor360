using Tailor360.Platform.Security.Permissions;

namespace Tailor360.Modules.Orders.Application;

/// <summary>
/// The permissions the Orders module owns, covering estimates, orders, garment jobs, snapshots, the production workflow, QC and alterations.
/// The catalogue is composed at startup and rejects a key claimed by two modules, so every permission
/// has exactly one owner. Issue #24 populates this set together with the role matrix that carries it.
/// </summary>
public sealed class OrdersPermissions : IPermissionSource
{
    /// <inheritdoc />
    public IReadOnlyCollection<Permission> Permissions => [];
}
