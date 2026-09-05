using Tailor360.Platform.Security.Permissions;

namespace Tailor360.Modules.Media.Application;

/// <summary>
/// The permissions the Media module owns, covering the upload pipeline, object storage, authorised delivery and retention of images.
/// The catalogue is composed at startup and rejects a key claimed by two modules, so every permission
/// has exactly one owner. Issue #24 populates this set together with the role matrix that carries it.
/// </summary>
public sealed class MediaPermissions : IPermissionSource
{
    /// <inheritdoc />
    public IReadOnlyCollection<Permission> Permissions => [];
}
