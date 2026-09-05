using Tailor360.Platform.Security.Permissions;

namespace Tailor360.Modules.Customers.Application;

/// <summary>
/// The permissions the Customers module owns, covering customers, consent, communication preferences, measurement templates and measurement versions.
/// The catalogue is composed at startup and rejects a key claimed by two modules, so every permission
/// has exactly one owner. Issue #24 populates this set together with the role matrix that carries it.
/// </summary>
public sealed class CustomersPermissions : IPermissionSource
{
    /// <inheritdoc />
    public IReadOnlyCollection<Permission> Permissions => [];
}
