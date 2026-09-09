using Tailor360.Platform.Security.Permissions;

namespace Tailor360.Platform.Security.FieldVisibility;

/// <summary>
/// The Orders views the workshop reads: the job card and the work queue.
/// </summary>
/// <remarks>
/// <para>
/// Both withhold the same four classes, and the reason is one sentence in
/// <c>docs/nfr/data-classification.md</c>: "a job card shows the customer's name and job number and
/// never their phone number, <b>which is why a Tailor can be shown a job card at all</b>". Minimisation
/// here is not a restriction bolted onto a screen; it is what makes the screen shareable with the
/// people who do the work.
/// </para>
/// <para>
/// The customer's name carries no permission of its own. That is deliberate and it is not an oversight:
/// <c>customers.read</c> opens the customer <em>record</em>, and neither Tailor nor Tailor Master holds
/// it, yet <c>docs/prd/glossary.md</c> defines a job card as showing "the customer name and job number
/// only". Requiring <c>customers.read</c> for the name would empty the job card for exactly the two
/// roles it is printed for. The gate on the name is the view's own permission, and what keeps it safe
/// is that contact details cannot appear beside it.
/// </para>
/// </remarks>
public static class OrdersResponseViews
{
    /// <summary>The workshop's view of one garment job, on screen and in print.</summary>
    public const string JobCard = "orders.job_card";

    /// <summary>The shop-floor list of jobs in a branch.</summary>
    public const string WorkQueue = "orders.work_queue";

    /// <summary>
    /// The classes no Tailor-facing surface may carry.
    /// </summary>
    /// <remarks>
    /// It is <see cref="SurfaceRules.ForbiddenOnAWorkshopSurface"/> and not a list of its own. The set
    /// is a property of the workshop rather than of this module — the measurement sheet is a workshop
    /// surface owned by Customers — and a second copy here is a second place for it to be wrong.
    /// <see cref="ResponseView"/> refuses a workshop view that withholds less, so the alias is a
    /// convenience and never the authority.
    /// </remarks>
    public const FieldClassification WithheldFromTheWorkshop = SurfaceRules.ForbiddenOnAWorkshopSurface;

    /// <summary>The Orders views.</summary>
    public static IReadOnlyCollection<ResponseView> All { get; } =
    [
        new ResponseView(
            JobCard,
            PermissionModules.Orders,
            ViewSurface.Workshop,
            "One garment job as the workshop needs it: what to make, from whose measurements, by when.",
            OrdersPermissions.Read,
            WithheldFromTheWorkshop,
            [
                new ViewField("jobNumber", FieldClassification.Operational, null,
                    "The job's own number, which is how the workshop, the label and the customer all refer to it."),
                new ViewField("orderNumber", FieldClassification.Operational, null,
                    "The order the job belongs to, so a multi-garment order can be kept together."),
                new ViewField("categoryLabel", FieldClassification.Operational, null,
                    "The stitching category, from the catalogue snapshot frozen at confirmation."),
                new ViewField("serviceLabel", FieldClassification.Operational, null,
                    "The service type within the category, likewise from the snapshot."),
                new ViewField("designSnapshot", FieldClassification.Operational, null,
                    "The design selections as agreed, embedding labels and illustration references so the "
                    + "card renders identically after the catalogue changes (docs/prd/design-options.md)."),
                new ViewField("garmentInstructions", FieldClassification.Operational, null,
                    "Free-text craft instructions carried into the snapshot; they never price and never "
                    + "move the due date (OD-DES-06)."),
                new ViewField("dueDate", FieldClassification.Operational, null,
                    "The promised date the workshop works to."),
                new ViewField("priority", FieldClassification.Operational, null,
                    "Priority and any rush marking, which decides the order of work."),
                new ViewField("currentPhase", FieldClassification.Operational, null,
                    "Where the job has got to in its pinned workflow version."),
                new ViewField("assignedTo", FieldClassification.Operational, null,
                    "Who the job is assigned to. A name, not a measure: throughput about a named person "
                    + "is a separate class and is not on this card."),
                new ViewField("barcodePayload", FieldClassification.Operational, null,
                    "The label's opaque payload. It carries no personal data by construction "
                    + "(docs/nfr/data-classification.md section 9)."),
                new ViewField("customerName", FieldClassification.CustomerIdentity, null,
                    "Whose garment this is. The whole of the customer that reaches the workshop "
                    + "(docs/prd/glossary.md, job card)."),
                new ViewField("measurements", FieldClassification.Measurement,
                    CustomersPermissions.ReadMeasurementSheet,
                    "The measurement snapshot frozen at confirmation. Sensitive personal data, and the "
                    + "read is audited explicitly (docs/nfr/data-classification.md section 5.4)."),
                new ViewField("referenceImages", FieldClassification.Media, MediaPermissions.Read,
                    "Reference photographs, streamed and re-authorised per request — never a URL "
                    + "(docs/nfr/data-classification.md section 5.5)."),
            ]),

        new ResponseView(
            WorkQueue,
            PermissionModules.Orders,
            ViewSurface.Workshop,
            "The branch's jobs as a list, for picking up the next piece of work.",
            OrdersPermissions.Read,
            WithheldFromTheWorkshop,
            [
                new ViewField("jobNumber", FieldClassification.Operational, null,
                    "The job's own number."),
                new ViewField("categoryLabel", FieldClassification.Operational, null,
                    "What kind of garment it is, so the queue can be scanned at a glance."),
                new ViewField("dueDate", FieldClassification.Operational, null,
                    "The promised date, which is what the queue is ordered by."),
                new ViewField("priority", FieldClassification.Operational, null,
                    "Priority and rush marking."),
                new ViewField("currentPhase", FieldClassification.Operational, null,
                    "The phase the job is waiting in."),
                new ViewField("holdState", FieldClassification.Operational, null,
                    "Whether the job is held and under which reason code, so nobody starts work on a "
                    + "garment that is waiting for a decision."),
                new ViewField("assignedTo", FieldClassification.Operational, null,
                    "Who has it, which is how a queue distinguishes unassigned work from somebody else's."),
                new ViewField("customerName", FieldClassification.CustomerIdentity, null,
                    "Whose garment it is. The same single identifying field the job card carries."),
                new ViewField("assigneeThroughput", FieldClassification.StaffPerformance,
                    ReportingPermissions.Read,
                    "Completed-per-day and on-time rate for a named member of staff. Decision DC-11 says "
                    + "these are for the Tailor Master, the Branch Manager and the Owner and are never a "
                    + "wall display, so they are gated rather than shown beside the name."),
            ]),
    ];
}
