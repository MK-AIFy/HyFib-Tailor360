namespace Tailor360.Platform.Security.Permissions;

/// <summary>
/// Permissions owned by the Catalog and Design module: stitching categories, service types, design
/// options and their rules, production workflows and quality checklists.
/// </summary>
/// <remarks>
/// Every <c>publish</c> in this group is flagged, because publishing is what a priced, confirmed order
/// is pinned to: a published version is quoted, worked to and invoiced against, and it cannot be
/// withdrawn from the orders that already reference it.
/// </remarks>
public static class CatalogPermissions
{
    /// <summary>Draft a catalogue version — categories, service types, design options and rules.</summary>
    public const string Edit = "catalog.edit";

    /// <summary>Publish or retire a catalogue version.</summary>
    public const string Publish = "catalog.publish";

    /// <summary>Draft a production workflow version.</summary>
    public const string EditWorkflows = "catalog.workflows.edit";

    /// <summary>Publish or retire a production workflow version.</summary>
    public const string PublishWorkflows = "catalog.workflows.publish";

    /// <summary>Draft a quality checklist version.</summary>
    public const string EditChecklists = "catalog.checklists.edit";

    /// <summary>Publish or retire a quality checklist version.</summary>
    public const string PublishChecklists = "catalog.checklists.publish";

    /// <summary>Every permission in this group, in declaration order.</summary>
    public static IReadOnlyCollection<Permission> All { get; } =
    [
        new(Edit, "Draft a catalogue version: categories, service types, design options and rules.",
            PermissionModules.Catalog, PermissionScope.Organisation),
        new(Publish, "Publish or retire a catalogue version.",
            PermissionModules.Catalog, PermissionScope.Organisation,
            RequiresMfa: true, RequiresStepUp: true, RequiresReason: true),
        new(EditWorkflows, "Draft a production workflow version.",
            PermissionModules.Catalog, PermissionScope.Organisation),
        new(PublishWorkflows, "Publish or retire a production workflow version.",
            PermissionModules.Catalog, PermissionScope.Organisation,
            RequiresMfa: true, RequiresStepUp: true, RequiresReason: true),
        new(EditChecklists, "Draft a quality checklist version.",
            PermissionModules.Catalog, PermissionScope.Organisation),
        new(PublishChecklists, "Publish or retire a quality checklist version.",
            PermissionModules.Catalog, PermissionScope.Organisation,
            RequiresMfa: true, RequiresStepUp: true, RequiresReason: true),
    ];
}
