using Tailor360.Modules.Identity.Domain.Access;
using Tailor360.Platform.Security.Permissions;

namespace Tailor360.Modules.Identity.Application.Access;

/// <summary>
/// The roles this release ships and the permissions each grants by default.
/// </summary>
/// <remarks>
/// <para>
/// <b>Twelve entries, not ten.</b> The plan's issue blueprint names ten roles; the product
/// documentation names eleven and adds a vendor-side principal. This register carries the documentation's
/// list, because it is the only reading under which every existing document stays true —
/// <c>docs/prd/state-transitions.md</c> line 225 names Branch Manager as the sole actor for
/// <c>orders.reschedule</c>, <c>docs/prd/00-overview.md</c> defines Admin as "a superset of Branch
/// Manager", and <c>docs/prd/raci.md</c> makes Branch Manager accountable for four of its
/// twenty-eight rows. Dropping the role would have meant editing all three. This is a documented
/// default under open decision <b>OD-13</b>, which is still open; the choice, its consequence and what
/// would change if the owner decides otherwise are recorded in
/// <c>docs/security/permission-matrix.md</c>.
/// </para>
/// <para>
/// <b>Names are matched, not just displayed.</b> <c>Identity:Mfa:RequiredRoles</c> defaults to Owner,
/// Admin and Cashier and matches on the display name, case-insensitively. Renaming one of those three
/// here would silently stop demanding a second factor of it.
/// </para>
/// <para>
/// <b>Grants are the seed, not the law.</b> Every grant below is editable by an administrator holding
/// <c>admin.roles</c>, and custom roles may be created alongside these. What cannot be edited is the
/// catalogue: a role may only be granted a permission the application declares, which the seeder
/// checks against <see cref="PermissionCatalogue"/> before it writes anything.
/// </para>
/// </remarks>
public static class SystemRoles
{
    /// <summary>Business-wide approval of catalogue, prices, permissions and alerts.</summary>
    public const string Owner = "owner";

    /// <summary>Users, branches, roles and configuration; no shop-floor duties.</summary>
    public const string Admin = "admin";

    /// <summary>Supervises the day in the branches the holder is assigned to.</summary>
    public const string BranchManager = "branch_manager";

    /// <summary>The counter: intake, measurements, estimate, confirmation, labels.</summary>
    public const string Reception = "reception";

    /// <summary>The measurement bundle, for shops that staff it separately from the counter.</summary>
    public const string MeasurementStaff = "measurement_staff";

    /// <summary>The workshop lead: production, assignment, quality control, rework.</summary>
    public const string TailorMaster = "tailor_master";

    /// <summary>The stitching role: custody by scan, phases, material movements.</summary>
    public const string Tailor = "tailor";

    /// <summary>The store room: items, suppliers, locations, receipts, stocktakes.</summary>
    public const string InventoryClerk = "inventory_clerk";

    /// <summary>The money: invoices, payments, allocations, receipts, session reconciliation.</summary>
    public const string Cashier = "cashier";

    /// <summary>The delivery queue, dispatch and the doorstep handover.</summary>
    public const string DeliveryStaff = "delivery_staff";

    /// <summary>Read-only across the organisation: audit, financial records, exports.</summary>
    public const string Auditor = "auditor";

    /// <summary>Vendor-side principal, feature flags only.</summary>
    public const string HyFibSuperUser = "hyfib_super_user";

    /// <summary>Every system role, in the order the permission matrix lists them.</summary>
    public static IReadOnlyList<SystemRoleDefinition> All { get; } =
    [
        new(Owner, "Owner",
            "Approves the catalogue, prices, tax configuration, the permission matrix and alert "
            + "policies; approves dispatch exceptions; reads across every branch.",
            RoleReach.Organisation, AssignedByDefault: true,
            [
                PlatformPermissions.ReadAllBranches,
                PlatformPermissions.OutboxReplay,
                PlatformPermissions.FeatureFlags,
                PlatformPermissions.AuditRead,
                PlatformPermissions.AuditExport,
                PlatformPermissions.DiagnosticsRead,
                PlatformPermissions.HealthRead,
                IdentityPermissions.Users,
                IdentityPermissions.Roles,
                IdentityPermissions.Branches,
                CustomersPermissions.Read,
                CustomersPermissions.ReadContact,
                CustomersPermissions.ReadConsent,
                CustomersPermissions.ReadNotes,
                CustomersPermissions.Merge,
                CustomersPermissions.Export,
                CustomersPermissions.Restrict,
                CustomersPermissions.RequestDeletion,
                CustomersPermissions.ReadMeasurementSheet,
                CustomersPermissions.EditTemplates,
                CustomersPermissions.PublishTemplates,
                CatalogPermissions.Edit,
                CatalogPermissions.Publish,
                CatalogPermissions.EditWorkflows,
                CatalogPermissions.PublishWorkflows,
                CatalogPermissions.EditChecklists,
                CatalogPermissions.PublishChecklists,
                MediaPermissions.Read,
                MediaPermissions.Delete,
                OrdersPermissions.Read,
                OrdersPermissions.Cancel,
                CustodyPermissions.ApproveReconciliation,
                InventoryPermissions.ApproveVariance,
                InventoryPermissions.ApproveNegativeStock,
                InventoryPermissions.ViewReports,
                InventoryPermissions.ViewValuation,
                BillingPermissions.ManagePriceLists,
                BillingPermissions.PublishPriceList,
                BillingPermissions.OverridePrice,
                BillingPermissions.ApproveDispatchException,
                BillingPermissions.CancelInvoice,
                BillingPermissions.Refund,
                BillingPermissions.Reverse,
                ReportingPermissions.Read,
                ReportingPermissions.Export,
                NotificationsPermissions.ManageTemplates,
                NotificationsPermissions.Replay,
                NotificationsPermissions.ReadFeedback,
                IntegrationPermissions.ManageWebhooks,
                IntegrationPermissions.ReplayDelivery,
            ]),
        new(Admin, "Admin",
            "Administers users, branches, roles, templates and integrations across the organisation. "
            + "Holds no shop-floor permission and cannot publish the catalogue or change a price.",
            RoleReach.Organisation, AssignedByDefault: true,
            [
                PlatformPermissions.ReadAllBranches,
                PlatformPermissions.OutboxReplay,
                PlatformPermissions.DiagnosticsRead,
                PlatformPermissions.HealthRead,
                IdentityPermissions.Users,
                IdentityPermissions.Roles,
                IdentityPermissions.Branches,
                CustomersPermissions.Read,
                CustomersPermissions.Restrict,
                CustomersPermissions.RequestDeletion,
                CustomersPermissions.EditTemplates,
                CustomersPermissions.PublishTemplates,
                MediaPermissions.Read,
                OrdersPermissions.Read,
                CustodyPermissions.ReprintLabel,
                CustodyPermissions.InvalidateLabel,
                ReportingPermissions.Read,
                NotificationsPermissions.ManageTemplates,
                NotificationsPermissions.Replay,
                IntegrationPermissions.ManageWebhooks,
                IntegrationPermissions.ReplayDelivery,
            ]),
        new(BranchManager, "Branch Manager",
            "Supervises the day in the assigned branches: exception queues, holds, reschedules, "
            + "reconciliation cases, stocktake and variance approvals, service recovery.",
            RoleReach.Branch, AssignedByDefault: true,
            [
                CustomersPermissions.Read,
                CustomersPermissions.ReadContact,
                CustomersPermissions.ReadConsent,
                CustomersPermissions.ReadNotes,
                CustomersPermissions.Create,
                CustomersPermissions.Update,
                CustomersPermissions.Deactivate,
                CustomersPermissions.Merge,
                CustomersPermissions.CaptureMeasurements,
                CustomersPermissions.ReadMeasurementSheet,
                MediaPermissions.Upload,
                MediaPermissions.Read,
                MediaPermissions.Delete,
                OrdersPermissions.Read,
                OrdersPermissions.Intake,
                OrdersPermissions.Estimate,
                OrdersPermissions.Confirm,
                OrdersPermissions.Revise,
                OrdersPermissions.ReviseDesign,
                OrdersPermissions.Cancel,
                OrdersPermissions.CancelJob,
                OrdersPermissions.Hold,
                OrdersPermissions.Resume,
                OrdersPermissions.Reschedule,
                OrdersPermissions.StartProduction,
                OrdersPermissions.Assign,
                OrdersPermissions.PhaseTransition,
                OrdersPermissions.RecordQc,
                OrdersPermissions.OpenRework,
                OrdersPermissions.CompleteRework,
                OrdersPermissions.AlterationDecide,
                CustodyPermissions.Scan,
                CustodyPermissions.ManualLookup,
                CustodyPermissions.PrintLabel,
                CustodyPermissions.BulkPrintLabel,
                CustodyPermissions.VerifyLabel,
                CustodyPermissions.ReprintLabel,
                CustodyPermissions.InvalidateLabel,
                CustodyPermissions.GenerateIdentity,
                CustodyPermissions.TransferOut,
                CustodyPermissions.Receive,
                CustodyPermissions.RejectTransfer,
                CustodyPermissions.Dispatch,
                CustodyPermissions.ConfirmDelivery,
                CustodyPermissions.RecordDeliveryOutcome,
                CustodyPermissions.OpenCase,
                CustodyPermissions.Reconcile,
                CustodyPermissions.ApproveReconciliation,
                InventoryPermissions.ManageItems,
                InventoryPermissions.ManageSuppliers,
                InventoryPermissions.ManageLocations,
                InventoryPermissions.ManageReorderRules,
                InventoryPermissions.RecordMovement,
                InventoryPermissions.Stocktake,
                InventoryPermissions.ApproveVariance,
                InventoryPermissions.ApproveNegativeStock,
                InventoryPermissions.ViewReports,
                InventoryPermissions.ViewValuation,
                BillingPermissions.CreateInvoice,
                BillingPermissions.UpdateInvoice,
                BillingPermissions.PostInvoice,
                BillingPermissions.CancelInvoice,
                BillingPermissions.PostCreditNote,
                BillingPermissions.PrintReceipt,
                BillingPermissions.OverridePrice,
                BillingPermissions.RecordPayment,
                BillingPermissions.RecordPaymentIntent,
                BillingPermissions.AllocatePayment,
                BillingPermissions.AllocateManual,
                BillingPermissions.Refund,
                BillingPermissions.Reverse,
                BillingPermissions.Session,
                ReportingPermissions.Read,
                ReportingPermissions.Export,
                NotificationsPermissions.ReadFeedback,
                NotificationsPermissions.ManageCases,
            ]),
        new(Reception, "Reception",
            "The counter: finds or creates the customer, records consent, captures measurements and "
            + "images, builds and confirms the order, prints labels and takes the advance.",
            RoleReach.Branch, AssignedByDefault: true,
            [
                CustomersPermissions.Read,
                CustomersPermissions.ReadContact,
                CustomersPermissions.ReadConsent,
                CustomersPermissions.Create,
                CustomersPermissions.Update,
                CustomersPermissions.CaptureMeasurements,
                CustomersPermissions.ReadMeasurementSheet,
                MediaPermissions.Upload,
                MediaPermissions.Read,
                OrdersPermissions.Read,
                OrdersPermissions.Intake,
                OrdersPermissions.Estimate,
                OrdersPermissions.Confirm,
                OrdersPermissions.Revise,
                OrdersPermissions.ReviseDesign,
                CustodyPermissions.Scan,
                CustodyPermissions.ManualLookup,
                CustodyPermissions.PrintLabel,
                CustodyPermissions.BulkPrintLabel,
                CustodyPermissions.VerifyLabel,
                BillingPermissions.RecordPayment,
                BillingPermissions.RecordPaymentIntent,
                NotificationsPermissions.ReadFeedback,
                NotificationsPermissions.ManageCases,
            ]),
        new(MeasurementStaff, "Measurement Staff",
            "The measurement bundle on its own, for a shop that staffs the measurements queue "
            + "separately from the counter. Assigned to nobody by default: the same permissions are "
            + "granted to Reception, so either reading of OD-13 works without a change here.",
            RoleReach.Branch, AssignedByDefault: false,
            [
                CustomersPermissions.Read,
                CustomersPermissions.CaptureMeasurements,
                CustomersPermissions.ReadMeasurementSheet,
                MediaPermissions.Upload,
                MediaPermissions.Read,
                OrdersPermissions.Read,
            ]),
        new(TailorMaster, "Tailor Master",
            "The workshop lead: starts production, pins the workflow version, assigns garment jobs, "
            + "runs the workboard, records quality results and decides rework.",
            RoleReach.Branch, AssignedByDefault: true,
            [
                CustomersPermissions.ReadMeasurementSheet,
                MediaPermissions.Upload,
                MediaPermissions.Read,
                OrdersPermissions.Read,
                OrdersPermissions.StartProduction,
                OrdersPermissions.Assign,
                OrdersPermissions.PhaseTransition,
                OrdersPermissions.RecordQc,
                OrdersPermissions.OpenRework,
                OrdersPermissions.CompleteRework,
                OrdersPermissions.Hold,
                OrdersPermissions.Resume,
                CustodyPermissions.Scan,
                CustodyPermissions.ManualLookup,
                CustodyPermissions.VerifyLabel,
                CustodyPermissions.TransferOut,
                CustodyPermissions.Receive,
                CustodyPermissions.RejectTransfer,
                CustodyPermissions.OpenCase,
                ReportingPermissions.Read,
            ]),
        new(Tailor, "Tailor",
            "The stitching role: takes custody by scan, starts and completes phases, records material "
            + "issue, consumption, return and wastage.",
            RoleReach.Branch, AssignedByDefault: true,
            [
                CustomersPermissions.ReadMeasurementSheet,
                MediaPermissions.Upload,
                MediaPermissions.Read,
                OrdersPermissions.Read,
                OrdersPermissions.PhaseTransition,
                CustodyPermissions.Scan,
                CustodyPermissions.ManualLookup,
                InventoryPermissions.RecordMovement,
            ]),
        new(InventoryClerk, "Inventory Clerk",
            "The store room: items, suppliers, locations and reorder rules, purchase receipts, "
            + "low-stock response, stocktakes and variance explanations.",
            RoleReach.Branch, AssignedByDefault: true,
            [
                MediaPermissions.Upload,
                MediaPermissions.Read,
                OrdersPermissions.Read,
                InventoryPermissions.ManageItems,
                InventoryPermissions.ManageSuppliers,
                InventoryPermissions.ManageLocations,
                InventoryPermissions.ManageReorderRules,
                InventoryPermissions.RecordMovement,
                InventoryPermissions.Stocktake,
                InventoryPermissions.ViewReports,
                InventoryPermissions.ViewValuation,
            ]),
        new(Cashier, "Cashier",
            "The money: invoices, payments, allocations, receipts, and the cashier session with its "
            + "denomination count and reconciliation.",
            RoleReach.Branch, AssignedByDefault: true,
            [
                CustomersPermissions.Read,
                CustomersPermissions.ReadContact,
                OrdersPermissions.Read,
                BillingPermissions.CreateInvoice,
                BillingPermissions.UpdateInvoice,
                BillingPermissions.PostInvoice,
                BillingPermissions.CancelInvoice,
                BillingPermissions.PostCreditNote,
                BillingPermissions.PrintReceipt,
                BillingPermissions.RecordPayment,
                BillingPermissions.RecordPaymentIntent,
                BillingPermissions.AllocatePayment,
                BillingPermissions.AllocateManual,
                BillingPermissions.Refund,
                BillingPermissions.Reverse,
                BillingPermissions.Session,
            ]),
        new(DeliveryStaff, "Delivery Staff",
            "The delivery queue: the receive scan that evaluates the dispatch gate, dispatch, the "
            + "doorstep handover, and failed or returned deliveries.",
            RoleReach.Branch, AssignedByDefault: true,
            [
                CustomersPermissions.Read,
                CustomersPermissions.ReadContact,
                MediaPermissions.Upload,
                MediaPermissions.Read,
                OrdersPermissions.Read,
                CustodyPermissions.Scan,
                CustodyPermissions.ManualLookup,
                CustodyPermissions.Receive,
                CustodyPermissions.Dispatch,
                CustodyPermissions.ConfirmDelivery,
                CustodyPermissions.RecordDeliveryOutcome,
                CustodyPermissions.OpenCase,
            ]),
        new(Auditor, "Auditor",
            "Read-only across the organisation: audit events, financial records, reports and the "
            + "exports a review needs. Never a state-changing principal.",
            RoleReach.Organisation, AssignedByDefault: true,
            [
                PlatformPermissions.ReadAllBranches,
                PlatformPermissions.AuditRead,
                PlatformPermissions.AuditExport,
                CustomersPermissions.Read,
                CustomersPermissions.ReadContact,
                CustomersPermissions.ReadConsent,
                CustomersPermissions.Export,
                MediaPermissions.Read,
                OrdersPermissions.Read,
                InventoryPermissions.ViewReports,
                InventoryPermissions.ViewValuation,
                ReportingPermissions.Read,
                ReportingPermissions.Export,
            ]),
        new(HyFibSuperUser, "HyFib Super User",
            "The vendor-side principal, permitted to change feature flags with a mandatory reason and "
            + "an evaluation audit, and nothing else. Assigned to nobody by default.",
            RoleReach.Organisation, AssignedByDefault: false,
            [
                PlatformPermissions.FeatureFlags,
            ]),
    ];

    /// <summary>Finds a system role definition by key.</summary>
    public static SystemRoleDefinition? Find(string key)
        => All.FirstOrDefault(role => string.Equals(role.Key, key, StringComparison.Ordinal));
}
