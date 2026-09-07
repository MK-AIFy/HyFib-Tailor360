namespace Tailor360.Platform.Security.Permissions;

/// <summary>
/// The module names a <see cref="Permission"/> is attributed to. A permission's module is the module
/// that owns the action, which is not always the module whose name the key starts with:
/// <c>admin.users</c> is an Identity permission and <c>admin.outbox.replay</c> is a Platform one,
/// because <c>admin.</c> is a vocabulary rather than an owner.
/// </summary>
/// <remarks>
/// The names match the module directories under <c>src/Modules/</c> and the section headings of
/// <c>docs/architecture/module-ownership.md</c>, so a reader can go from a permission to the document
/// that says who owns the data it touches without a translation step.
/// </remarks>
public static class PermissionModules
{
    /// <summary>Shared kernel: audit, outbox, feature flags, health and diagnostics.</summary>
    public const string Platform = "Platform";

    /// <summary>Staff accounts, roles, branch assignment and the branch register.</summary>
    public const string Identity = "Identity";

    /// <summary>Customer records, consent and the measurement record.</summary>
    public const string Customers = "Customers";

    /// <summary>Stitching categories, service types, design options, workflows and checklists.</summary>
    public const string Catalog = "Catalog";

    /// <summary>Stored images and their authorised streaming.</summary>
    public const string Media = "Media";

    /// <summary>Orders, garment jobs and the production workflow.</summary>
    public const string Orders = "Orders";

    /// <summary>Barcode identities, labels, scans, transfers and dispatch.</summary>
    public const string Custody = "Custody";

    /// <summary>Stock items, the immutable ledger, stocktakes and valuation.</summary>
    public const string Inventory = "Inventory";

    /// <summary>Pricing, invoices, payments and cashier sessions.</summary>
    public const string Billing = "Billing";

    /// <summary>Reporting and governed exports.</summary>
    public const string Reporting = "Reporting";

    /// <summary>Notification templates, delivery and customer feedback.</summary>
    public const string Notifications = "Notifications";

    /// <summary>Outbound webhooks and integration event delivery.</summary>
    public const string Integration = "Integration";

    /// <summary>
    /// Every module code, in the order the modules are composed.
    /// </summary>
    /// <remarks>
    /// A list rather than a reflection sweep, because these codes are a published vocabulary: a module
    /// toggle names one, and an operator quotes one. Reflecting over the assemblies present would make
    /// that vocabulary depend on which modules a particular host happened to compose.
    /// </remarks>
    public static IReadOnlyList<string> All { get; } =
    [
        Platform,
        Identity,
        Customers,
        Catalog,
        Media,
        Orders,
        Custody,
        Inventory,
        Billing,
        Reporting,
        Notifications,
        Integration,
    ];
}
