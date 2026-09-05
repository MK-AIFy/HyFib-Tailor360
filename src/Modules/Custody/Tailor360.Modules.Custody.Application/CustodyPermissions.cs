using Tailor360.Platform.Security.Permissions;

namespace Tailor360.Modules.Custody.Application;

/// <summary>
/// The permissions the Custody module owns, covering barcode identities, labels, scans, custody transfers, reconciliation and the delivery queue.
/// The catalogue is composed at startup and rejects a key claimed by two modules, so every permission
/// has exactly one owner. Issue #24 populates this set together with the role matrix that carries it.
/// </summary>
public sealed class CustodyPermissions : IPermissionSource
{
    /// <inheritdoc />
    public IReadOnlyCollection<Permission> Permissions => [];
}
