using Tailor360.Platform.Security.Permissions;

namespace Tailor360.Modules.Integration.Application;

/// <summary>
/// The permissions the Integration module owns, covering the integration event relay, webhooks, provider adapters and the accounting export.
/// The catalogue is composed at startup and rejects a key claimed by two modules, so every permission
/// has exactly one owner. Issue #24 populates this set together with the role matrix that carries it.
/// </summary>
public sealed class IntegrationPermissions : IPermissionSource
{
    /// <inheritdoc />
    public IReadOnlyCollection<Permission> Permissions => [];
}
