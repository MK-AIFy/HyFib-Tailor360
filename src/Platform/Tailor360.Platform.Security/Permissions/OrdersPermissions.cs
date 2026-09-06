namespace Tailor360.Platform.Security.Permissions;

/// <summary>
/// Permissions owned by the Orders and Workflow module: the draft, the estimate, confirmation,
/// production, quality control, and the exception paths of hold, rework, alteration and cancellation.
/// </summary>
/// <remarks>
/// <para>
/// Two names a reader will look for and not find. <c>orders.ready_state_changed</c> is an audit action,
/// not a permission: <c>docs/prd/raci.md</c> row 16 says explicitly that no role, however senior,
/// declares a garment ready — the ready-for-delivery gate computes it. <c>orders.draft_create</c>,
/// <c>orders.issue_estimate</c> and <c>orders.close</c> are likewise the audit actions of the
/// transitions in <c>docs/prd/state-transitions.md</c>; the permissions those transitions demand are
/// <see cref="Intake"/>, <see cref="Estimate"/> and — for closure — none at all, because it is a
/// computed transition.
/// </para>
/// </remarks>
public static class OrdersPermissions
{
    /// <summary>Read an order, its garment jobs and their state.</summary>
    public const string Read = "orders.read";

    /// <summary>Create and edit an order draft.</summary>
    public const string Intake = "orders.intake";

    /// <summary>Issue or reissue a priced estimate for a draft.</summary>
    public const string Estimate = "orders.estimate";

    /// <summary>Confirm an order, which freezes its snapshots and allocates its numbers.</summary>
    public const string Confirm = "orders.confirm";

    /// <summary>Revise a confirmed order — add, remove or reprice a garment.</summary>
    public const string Revise = "orders.revise";

    /// <summary>Change the design selections on a confirmed garment before cutting.</summary>
    public const string ReviseDesign = "orders.revise_design";

    /// <summary>Cancel a confirmed order.</summary>
    public const string Cancel = "orders.cancel";

    /// <summary>Cancel one garment job on an otherwise live order.</summary>
    public const string CancelJob = "orders.cancel_job";

    /// <summary>Put a garment job on hold.</summary>
    public const string Hold = "orders.hold";

    /// <summary>Resume a garment job that was on hold.</summary>
    public const string Resume = "orders.resume";

    /// <summary>Move a garment job's promised date.</summary>
    public const string Reschedule = "orders.reschedule";

    /// <summary>Start production and pin the workflow version.</summary>
    public const string StartProduction = "orders.start_production";

    /// <summary>Assign a garment job to a tailor.</summary>
    public const string Assign = "orders.assign";

    /// <summary>Start or complete a workflow phase.</summary>
    public const string PhaseTransition = "orders.phase_transition";

    /// <summary>Record a quality-control result against the pinned checklist version.</summary>
    public const string RecordQc = "orders.record_qc";

    /// <summary>Open a rework after a failed quality check.</summary>
    public const string OpenRework = "orders.open_rework";

    /// <summary>Complete an open rework.</summary>
    public const string CompleteRework = "orders.complete_rework";

    /// <summary>Decide an alteration: its price, its due date and who bears the cost.</summary>
    public const string AlterationDecide = "orders.alteration_decide";

    /// <summary>Every permission in this group, in declaration order.</summary>
    public static IReadOnlyCollection<Permission> All { get; } =
    [
        new(Read, "Read an order, its garment jobs and their state.", PermissionModules.Orders),
        new(Intake, "Create and edit an order draft.", PermissionModules.Orders),
        new(Estimate, "Issue or reissue a priced estimate for a draft.", PermissionModules.Orders),
        new(Confirm, "Confirm an order, freezing its snapshots and allocating its numbers.",
            PermissionModules.Orders),
        new(Revise, "Revise a confirmed order.", PermissionModules.Orders, RequiresReason: true),
        new(ReviseDesign, "Change design selections on a confirmed garment before cutting.",
            PermissionModules.Orders, RequiresReason: true),
        new(Cancel, "Cancel a confirmed order.", PermissionModules.Orders, RequiresReason: true),
        new(CancelJob, "Cancel one garment job on a live order.",
            PermissionModules.Orders, RequiresReason: true),
        new(Hold, "Put a garment job on hold.", PermissionModules.Orders, RequiresReason: true),
        new(Resume, "Resume a garment job that was on hold.",
            PermissionModules.Orders, RequiresReason: true),
        new(Reschedule, "Move a garment job's promised date.",
            PermissionModules.Orders, RequiresReason: true),
        new(StartProduction, "Start production and pin the workflow version.", PermissionModules.Orders),
        new(Assign, "Assign a garment job to a tailor.", PermissionModules.Orders),
        new(PhaseTransition, "Start or complete a workflow phase.", PermissionModules.Orders),
        new(RecordQc, "Record a quality-control result.", PermissionModules.Orders),
        new(OpenRework, "Open a rework after a failed quality check.",
            PermissionModules.Orders, RequiresReason: true),
        new(CompleteRework, "Complete an open rework.",
            PermissionModules.Orders, RequiresReason: true),
        new(AlterationDecide, "Decide an alteration's price, due date and who bears the cost.",
            PermissionModules.Orders, RequiresReason: true),
    ];
}
