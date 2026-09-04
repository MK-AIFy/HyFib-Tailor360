using Tailor360.Platform.Security.Permissions;

namespace Tailor360.Modules.Inventory.Application;

/// <summary>
/// The permissions the Inventory module owns, covering items, units, suppliers, locations, the stock ledger, reservations, stocktakes and alerts.
/// The catalogue is composed at startup and rejects a key claimed by two modules, so every permission
/// has exactly one owner. Issue #24 populates this set together with the role matrix that carries it.
/// </summary>
public sealed class InventoryPermissions : IPermissionSource
{
    /// <inheritdoc />
    public IReadOnlyCollection<Permission> Permissions => [];
}
