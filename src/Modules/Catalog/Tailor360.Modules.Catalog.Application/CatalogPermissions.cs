using Tailor360.Platform.Security.Permissions;

namespace Tailor360.Modules.Catalog.Application;

/// <summary>
/// The permissions the Catalog module owns, covering stitching categories, service types, design option groups and QC checklist templates.
/// The catalogue is composed at startup and rejects a key claimed by two modules, so every permission
/// has exactly one owner. Issue #24 populates this set together with the role matrix that carries it.
/// </summary>
public sealed class CatalogPermissions : IPermissionSource
{
    /// <inheritdoc />
    public IReadOnlyCollection<Permission> Permissions => [];
}
