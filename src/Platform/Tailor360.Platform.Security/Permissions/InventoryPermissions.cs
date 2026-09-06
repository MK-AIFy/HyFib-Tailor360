namespace Tailor360.Platform.Security.Permissions;

/// <summary>
/// Permissions owned by the Inventory module: items, suppliers, locations, reorder rules, the
/// immutable stock ledger, stocktakes and valuation.
/// </summary>
/// <remarks>
/// The two approvals are separated from the work they approve on purpose. Counting a stocktake needs
/// <see cref="Stocktake"/>; approving the variance it found needs <see cref="ApproveVariance"/>, and
/// the endpoint additionally refuses an approver who is the person that counted
/// (<c>docs/prd/raci.md</c> footnote (17)).
/// </remarks>
public static class InventoryPermissions
{
    /// <summary>Create and maintain stock items and their units.</summary>
    public const string ManageItems = "inventory.manage_items";

    /// <summary>Create and maintain suppliers.</summary>
    public const string ManageSuppliers = "inventory.manage_suppliers";

    /// <summary>Create and maintain storage locations.</summary>
    public const string ManageLocations = "inventory.manage_locations";

    /// <summary>Create and maintain reorder rules and low-stock thresholds.</summary>
    public const string ManageReorderRules = "inventory.manage_reorder_rules";

    /// <summary>Record a stock movement: receipt, reservation, issue, return or wastage.</summary>
    public const string RecordMovement = "inventory.record_movement";

    /// <summary>Run a stocktake: open it, count, recount and explain a variance.</summary>
    public const string Stocktake = "inventory.stocktake";

    /// <summary>Approve a stocktake variance so that it may be posted.</summary>
    public const string ApproveVariance = "inventory.approve_variance";

    /// <summary>Approve a movement that would take a stock balance negative.</summary>
    public const string ApproveNegativeStock = "inventory.approve_negative_stock";

    /// <summary>Read stock reports: balances, movement history and low-stock queues.</summary>
    public const string ViewReports = "inventory.view_reports";

    /// <summary>Read stock valuation, which carries cost prices.</summary>
    public const string ViewValuation = "inventory.view_valuation";

    /// <summary>Every permission in this group, in declaration order.</summary>
    public static IReadOnlyCollection<Permission> All { get; } =
    [
        new(ManageItems, "Create and maintain stock items and their units.", PermissionModules.Inventory),
        new(ManageSuppliers, "Create and maintain suppliers.", PermissionModules.Inventory),
        new(ManageLocations, "Create and maintain storage locations.", PermissionModules.Inventory),
        new(ManageReorderRules, "Create and maintain reorder rules and low-stock thresholds.",
            PermissionModules.Inventory),
        new(RecordMovement, "Record a receipt, reservation, issue, return or wastage.",
            PermissionModules.Inventory, RequiresReason: true),
        new(Stocktake, "Open a stocktake, count, recount and explain a variance.",
            PermissionModules.Inventory, RequiresReason: true),
        new(ApproveVariance, "Approve a stocktake variance so that it may be posted.",
            PermissionModules.Inventory, RequiresMfa: true, RequiresStepUp: true, RequiresReason: true),
        new(ApproveNegativeStock, "Approve a movement that would take a balance negative.",
            PermissionModules.Inventory, RequiresMfa: true, RequiresStepUp: true, RequiresReason: true),
        new(ViewReports, "Read stock balances, movement history and low-stock queues.",
            PermissionModules.Inventory),
        new(ViewValuation, "Read stock valuation, which carries cost prices.",
            PermissionModules.Inventory),
    ];
}
