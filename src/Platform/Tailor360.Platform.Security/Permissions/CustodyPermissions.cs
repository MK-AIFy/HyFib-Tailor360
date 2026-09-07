namespace Tailor360.Platform.Security.Permissions;

/// <summary>
/// Permissions owned by the Custody and Barcode module: barcode identities, labels, scans, transfers,
/// dispatch, delivery outcomes and reconciliation.
/// </summary>
/// <remarks>
/// The flagged four — generating an identity by hand, reprinting a label, invalidating one, and
/// approving a reconciliation — are the actions that break or repair the one-to-one bond between a
/// garment and the identity printed on it. Each carries step-up and a reason for that reason, per
/// <c>docs/prd/raci.md</c> footnotes (7), (8) and (17).
/// <para>
/// <c>custody.recipient_confirmation</c> is deliberately absent. It reads like a permission and is not
/// one: it is the branch configuration value that decides whether a doorstep handover needs an OTP, a
/// signature or either (<c>docs/prd/raci.md</c> section 3).
/// </para>
/// </remarks>
public static class CustodyPermissions
{
    /// <summary>Record a scan event against a barcode identity.</summary>
    public const string Scan = "custody.scan";

    /// <summary>Resolve an identity typed by hand rather than scanned.</summary>
    public const string ManualLookup = "custody.manual_lookup";

    /// <summary>Print a label for a garment job.</summary>
    public const string PrintLabel = "custody.print_label";

    /// <summary>Print a batch of labels for an order.</summary>
    public const string BulkPrintLabel = "custody.bulk_print_label";

    /// <summary>Record that a printed label was verified against its garment.</summary>
    public const string VerifyLabel = "custody.verify_label";

    /// <summary>Reprint a label for an identity that already has one.</summary>
    public const string ReprintLabel = "custody.reprint_label";

    /// <summary>Invalidate a printed label and its identity.</summary>
    public const string InvalidateLabel = "custody.invalidate_label";

    /// <summary>Generate a barcode identity outside order confirmation.</summary>
    public const string GenerateIdentity = "custody.generate_identity";

    /// <summary>Transfer custody of a garment out of the branch.</summary>
    public const string TransferOut = "custody.transfer_out";

    /// <summary>Receive a garment into custody.</summary>
    public const string Receive = "custody.receive";

    /// <summary>Reject an incoming transfer.</summary>
    public const string RejectTransfer = "custody.reject_transfer";

    /// <summary>Dispatch a garment against a dispatch authorisation.</summary>
    public const string Dispatch = "custody.dispatch";

    /// <summary>Confirm a doorstep handover.</summary>
    public const string ConfirmDelivery = "custody.confirm_delivery";

    /// <summary>Record a failed or returned delivery.</summary>
    public const string RecordDeliveryOutcome = "custody.record_delivery_outcome";

    /// <summary>Open a reconciliation case for a disputed or missing garment.</summary>
    public const string OpenCase = "custody.open_case";

    /// <summary>Record a custody correction event on a reconciliation case.</summary>
    public const string Reconcile = "custody.reconcile";

    /// <summary>Approve a reconciliation above the configured threshold.</summary>
    public const string ApproveReconciliation = "custody.approve_reconciliation";

    /// <summary>Every permission in this group, in declaration order.</summary>
    public static IReadOnlyCollection<Permission> All { get; } =
    [
        new(Scan, "Record a scan event against a barcode identity.", PermissionModules.Custody),
        new(ManualLookup, "Resolve an identity typed by hand rather than scanned.",
            PermissionModules.Custody, RequiresReason: true),
        new(PrintLabel, "Print a label for a garment job.", PermissionModules.Custody),
        new(BulkPrintLabel, "Print a batch of labels for an order.", PermissionModules.Custody),
        new(VerifyLabel, "Record that a printed label was verified against its garment.",
            PermissionModules.Custody),
        new(ReprintLabel, "Reprint a label for an identity that already has one.",
            PermissionModules.Custody, RequiresMfa: true, RequiresStepUp: true, RequiresReason: true),
        new(InvalidateLabel, "Invalidate a printed label and the identity it carries.",
            PermissionModules.Custody, RequiresMfa: true, RequiresStepUp: true, RequiresReason: true),
        new(GenerateIdentity, "Generate a barcode identity outside order confirmation.",
            PermissionModules.Custody, RequiresMfa: true, RequiresStepUp: true, RequiresReason: true),
        new(TransferOut, "Transfer custody of a garment out of the branch.", PermissionModules.Custody),
        new(Receive, "Receive a garment into custody.", PermissionModules.Custody),
        new(RejectTransfer, "Reject an incoming transfer.",
            PermissionModules.Custody, RequiresReason: true),
        new(Dispatch, "Dispatch a garment against a dispatch authorisation.", PermissionModules.Custody),
        new(ConfirmDelivery, "Confirm a doorstep handover.", PermissionModules.Custody),
        new(RecordDeliveryOutcome, "Record a failed or returned delivery.",
            PermissionModules.Custody, RequiresReason: true),
        new(OpenCase, "Open a reconciliation case for a disputed or missing garment.",
            PermissionModules.Custody, RequiresReason: true),
        new(Reconcile, "Record a custody correction event on a reconciliation case.",
            PermissionModules.Custody, RequiresReason: true),
        new(ApproveReconciliation, "Approve a reconciliation above the configured threshold.",
            PermissionModules.Custody, RequiresMfa: true, RequiresStepUp: true, RequiresReason: true),
    ];
}
