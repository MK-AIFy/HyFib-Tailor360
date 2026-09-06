namespace Tailor360.Platform.Security.Permissions;

/// <summary>
/// Permissions owned by the Reporting module: reading the reports and exporting them.
/// </summary>
/// <remarks>
/// <para>
/// Holding <see cref="Read"/> does not decide what the report shows. Branch scope narrows the rows and
/// field-level minimisation narrows the columns, so a Tailor Master reading the workload report sees
/// throughput and not money (<c>docs/nfr/data-classification.md</c> DC-11).
/// </para>
/// <para>
/// <see cref="Export"/> is flagged for multi-factor authentication outright. The plan's wording is
/// "<c>reports.export</c> above the row threshold", and a flag is a property of a permission rather
/// than of a request, so the stricter reading is the one that can actually be enforced: the export
/// permission demands a second factor, and issue #46 keeps the row threshold as the separate control
/// that decides which exports need an approval on top.
/// </para>
/// </remarks>
public static class ReportingPermissions
{
    /// <summary>Read reports on screen.</summary>
    public const string Read = "reports.read";

    /// <summary>Generate and download a report export.</summary>
    public const string Export = "reports.export";

    /// <summary>Every permission in this group, in declaration order.</summary>
    public static IReadOnlyCollection<Permission> All { get; } =
    [
        new(Read, "Read reports on screen, within branch scope and field minimisation.",
            PermissionModules.Reporting),
        new(Export, "Generate and download a report export.",
            PermissionModules.Reporting, RequiresMfa: true, RequiresReason: true),
    ];
}
