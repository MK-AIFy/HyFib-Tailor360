using Tailor360.Platform.Security.Permissions;

namespace Tailor360.Platform.Security.FieldVisibility;

/// <summary>
/// The Customers view the workshop reads: the measurement sheet.
/// </summary>
/// <remarks>
/// The sheet is Sensitive Personal on paper as much as on screen. Its own permission,
/// <c>measurements.read_sheet</c>, is the gate on the figures themselves — Delivery Staff, Cashier and
/// Inventory Clerk hold no reason to read them and are not granted it — and reading it is audited
/// explicitly rather than left to the ordinary request log
/// (<c>docs/nfr/data-classification.md</c> section 5.4).
/// </remarks>
public static class CustomersResponseViews
{
    /// <summary>The printable measurement sheet for one garment job.</summary>
    public const string MeasurementSheet = "customers.measurement_sheet";

    /// <summary>The Customers views.</summary>
    public static IReadOnlyCollection<ResponseView> All { get; } =
    [
        new ResponseView(
            MeasurementSheet,
            PermissionModules.Customers,
            "The figures the garment is cut to, rendered for the workshop and for print.",
            CustomersPermissions.ReadMeasurementSheet,
            OrdersResponseViews.WithheldFromTheWorkshop,
            [
                new ViewField("jobNumber", FieldClassification.Operational, null,
                    "Which job the sheet belongs to, so a loose sheet is never anonymous."),
                new ViewField("templateName", FieldClassification.Operational, null,
                    "The measurement template the figures were captured against."),
                new ViewField("templateVersion", FieldClassification.Operational, null,
                    "The published version pinned at confirmation, so a sheet reprinted a year later "
                    + "renders exactly as it did."),
                new ViewField("capturedAt", FieldClassification.Operational, null,
                    "When the figures were taken, which is how a stale set is spotted."),
                new ViewField("capturedBy", FieldClassification.Operational, null,
                    "Who took them, so a question about a figure has somebody to ask."),
                new ViewField("customerName", FieldClassification.CustomerIdentity, null,
                    "Whose figures these are. Nothing else about the customer is on the sheet."),
                new ViewField("values", FieldClassification.Measurement, null,
                    "The measured figures, stored in millimetres and rendered in the branch's display "
                    + "unit. The view's own permission is the gate; there is no sheet without them."),
                new ViewField("easeNotes", FieldClassification.Measurement, null,
                    "Ease and growth-allowance notes, which are read with the figures and classified "
                    + "with them."),
                new ViewField("fieldDiagrams", FieldClassification.Media, MediaPermissions.Read,
                    "The template's line drawings, streamed by the authorising endpoint like all media."),
            ]),
    ];
}
