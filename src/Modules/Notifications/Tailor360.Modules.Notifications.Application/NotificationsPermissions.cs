using Tailor360.Platform.Security.Permissions;

namespace Tailor360.Modules.Notifications.Application;

/// <summary>
/// The permissions the Notifications module owns, covering templates, notification intents, deliveries, customer links, feedback and service recovery.
/// The catalogue is composed at startup and rejects a key claimed by two modules, so every permission
/// has exactly one owner. Issue #24 populates this set together with the role matrix that carries it.
/// </summary>
public sealed class NotificationsPermissions : IPermissionSource
{
    /// <inheritdoc />
    public IReadOnlyCollection<Permission> Permissions => [];
}
