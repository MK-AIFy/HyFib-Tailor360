namespace Tailor360.Modules.Orders.Contracts.Orders;

/// <summary>
/// One garment job as another module reads it: where it stands, when it was promised, and whether it may go.
/// </summary>
/// <remarks>
/// <para>
/// The tracked unit of the shop. Custody keys its chain of custody on a garment job, Billing raises an invoice
/// line per garment job, and Reporting counts pipeline and workload by garment job — so this record, and not
/// <see cref="OrderSnapshot"/>, is what most consumers actually read
/// (<c>docs/architecture/module-ownership.md</c> sections 5.5 and 5.6).
/// </para>
/// <para>
/// <strong>What it deliberately withholds.</strong> No measurement snapshot in any form — not the values, not the
/// template, not the version, not a count: a measurement is Sensitive Personal and
/// <c>docs/nfr/data-classification.md</c> section 5.4 keeps it off every export and every payload, and the
/// workshop reads the sheet through the job card rather than across a boundary. No design selections and no
/// garment instructions: the first is bulk nobody has asked for, the second is free text a person wrote
/// (OD-DES-06). No reference media identifiers: section 5.5 keeps customer material out of anything but a
/// data-subject export, and security rule 9 means an identifier on a broadcast row is the first half of a link
/// nobody re-authorised. No free-text reason of any kind — only the configured codes. And <strong>no
/// amount</strong>: the price snapshot is Confidential under section 5.7 and is answered by
/// <see cref="IOrderSnapshotQuery.GetPricedAsync"/>, so a dispatch scan and a workload count read a garment's
/// state without ever receiving one.
/// </para>
/// <para>
/// <strong>What it does carry beyond the keys are the labels.</strong> Category and service-type labels are
/// Public under <c>docs/nfr/data-classification.md</c> section 2, because they are what a customer-facing
/// estimate already shows, and Billing's invoice lines need something to print beside a garment job identifier.
/// The keys travel too, because a label is what a person reads and a key is what a projection groups by.
/// </para>
/// <para>
/// <strong>Ready state is three facts, not one.</strong> <see cref="IsReadyForDelivery"/> is the gate's
/// materialised verdict, <see cref="ReadyStateComputedAt"/> is the instant the facts behind it were gathered, and
/// <see cref="ReadyStateBlocks"/> is why it is closed. CI-03 requires that ready state never outlive the facts it
/// was computed from, so a consumer deciding whether to act on a stale <c>true</c> compares the instant rather
/// than trusting the boolean alone.
/// </para>
/// </remarks>
/// <param name="GarmentJobId">The garment job. A UUIDv7, and what Custody, Billing and Reporting all key on.</param>
/// <param name="OrderId">The order it belongs to.</param>
/// <param name="OrganisationId">The organisation.</param>
/// <param name="BranchId">The branch. What a caller evaluates its own reach against.</param>
/// <param name="GarmentJobNumber">
/// The display number, <c>J-&lt;branch&gt;-&lt;year&gt;-000001-01</c>
/// (<c>docs/architecture/conventions.md</c> section 3.2). The human reference, never a lookup key, and never the
/// barcode payload — a barcode carries an opaque identity instead (D9).
/// </param>
/// <param name="JobIndex">Its one-based position within the order, which is the order a job card prints in.</param>
/// <param name="CategoryKey">
/// The category as a concept, carried across catalogue versions. What Reporting groups by.
/// </param>
/// <param name="ServiceTypeKey">The service type as a concept, carried across catalogue versions.</param>
/// <param name="CategoryLabel">The category's label as the confirmation froze it. Public; safe to print.</param>
/// <param name="ServiceTypeLabel">The service type's label as the confirmation froze it. Public; safe to print.</param>
/// <param name="WorkflowDefinitionId">
/// The workflow definition recorded at confirmation. A definition and not a version: confirmation records which
/// process applies and instantiates nothing (issue #33).
/// </param>
/// <param name="WorkflowVersionId">
/// The version pinned when the job entered production, or null before it did. Once pinned it never migrates
/// (INV-JOB-02), which is what makes a job card render the same way two years later.
/// </param>
/// <param name="DueDate">
/// The garment's own promised date, already evaluated in the branch timezone against the branch working calendar
/// (<c>docs/architecture/conventions.md</c> section 2.2). The delivery queue is ordered by it, and Reporting
/// measures promised-versus-actual turnaround from it.
/// </param>
/// <param name="State">Where the garment stands in its own lifecycle.</param>
/// <param name="ConfirmedAt">When the job was created, inside the order confirmation, in UTC.</param>
/// <param name="ProductionStartedAt">When it entered production, or null before it has.</param>
/// <param name="DeliveredAt">When it was handed over at the door, or null.</param>
/// <param name="CancelledAt">When it was cancelled, or null.</param>
/// <param name="HoldReasonCode">
/// The configured hold reason code while a hold stands, and null otherwise. A code and never the free text beside
/// it (<c>docs/prd/state-transitions.md</c> section 3.2); the vocabulary is branch configuration, reviewed under
/// <strong>OD-10</strong> and seeded by issue #34.
/// </param>
/// <param name="HeldAt">When the standing hold was placed, or null when none stands.</param>
/// <param name="CancellationReasonCode">
/// The configured cancellation reason code, or null while the garment stands.
/// </param>
/// <param name="IsReadyForDelivery">
/// The gate's materialised verdict. Written by the ready gate alone and by no member of staff, however senior
/// (INV-JOB-07, <c>docs/prd/raci.md</c> row 16).
/// </param>
/// <param name="ReadyStateComputedAt">
/// When the gate that produced the current verdict was evaluated, or null before it has ever run. Not the same
/// instant as the read: CI-03 is checked against this.
/// </param>
/// <param name="ReadyStateBlocks">
/// Why the gate is closed, one entry per failing predicate, in <see cref="ReadyBlockReason"/> order. Empty when
/// the gate is open, and empty before it has ever run — which is why the boolean is read and not the count.
/// </param>
/// <param name="Dependencies">
/// What this garment waits for, or is delivered with (INV-JOB-09). The prerequisite is always another garment of
/// the same order.
/// </param>
public sealed record GarmentJobSnapshot(
    Guid GarmentJobId,
    Guid OrderId,
    Guid OrganisationId,
    Guid BranchId,
    string GarmentJobNumber,
    int JobIndex,
    string CategoryKey,
    string ServiceTypeKey,
    string CategoryLabel,
    string ServiceTypeLabel,
    Guid WorkflowDefinitionId,
    Guid? WorkflowVersionId,
    DateOnly DueDate,
    GarmentJobState State,
    DateTimeOffset ConfirmedAt,
    DateTimeOffset? ProductionStartedAt,
    DateTimeOffset? DeliveredAt,
    DateTimeOffset? CancelledAt,
    string? HoldReasonCode,
    DateTimeOffset? HeldAt,
    string? CancellationReasonCode,
    bool IsReadyForDelivery,
    DateTimeOffset? ReadyStateComputedAt,
    IReadOnlyList<ReadyStateBlock> ReadyStateBlocks,
    IReadOnlyList<GarmentJobDependency> Dependencies);

/// <summary>One failing ready-gate predicate, and the code or display number it names.</summary>
/// <remarks>
/// <para>
/// This is what a delivery-queue screen shows instead of a bare refusal. INV-JOB-07 requires each predicate to
/// return its own reason, and a reason on its own would still leave the reader asking <em>which</em> phase,
/// <em>which</em> sibling, <em>which</em> hold — so the reference travels with it.
/// </para>
/// <para>
/// <strong>The reference is a configured code or a display number, and the type refuses anything else.</strong>
/// The module's own <c>ReadyGateBlock</c> bounds it at two hundred characters and validates nothing about its
/// shape, because there it is an internal screen field; publishing it through
/// <see cref="IOrderSnapshotQuery"/> is what would carry it across a boundary to Custody, Billing and Reporting
/// on every ready-state read, so this projection is narrower than the domain value it is built from. The
/// <see cref="ReadyBlockReason.DocumentationComplete"/> predicate is the one that makes the difference: it names
/// the <em>kind</em> of evidence the checklist requires, by its configured code, and <strong>never a media object
/// identifier and never an evidence key</strong>. Evidence images are Sensitive Personal under
/// <c>docs/nfr/data-classification.md</c> section 5.6, section 5.5 keeps customer material out of anything but a
/// data-subject export, and security rule 9 makes an identifier on a row nobody re-authorised the first half of a
/// link — which is the same ground on which <see cref="GarmentJobSnapshot"/> refuses reference media identifiers
/// outright. <see cref="Of"/> enforces it: an identity in any GUID form is refused, so is anything
/// carrying a space, a full stop, a slash or any other character outside a code, and so is anything longer than
/// <see cref="MaximumReferenceLength"/>. Free text and an object key are both excluded by construction rather
/// than by asking a producer to be careful.
/// </para>
/// </remarks>
public sealed record ReadyStateBlock
{
    /// <summary>
    /// The longest reference this contract publishes. Shorter than the domain's two hundred characters on
    /// purpose: a code a person reads off a queue screen and a display number both fit, and a sentence does not.
    /// </summary>
    public const int MaximumReferenceLength = 40;

    private ReadyStateBlock(ReadyBlockReason reason, string? reference)
    {
        Reason = reason;
        Reference = reference;
    }

    /// <summary>Which predicate blocked. The member name is the published reason code.</summary>
    public ReadyBlockReason Reason { get; }

    /// <summary>
    /// What it names, where it names something: a phase code, a defect code, a required-evidence kind code, a
    /// hold reason code, a sibling's garment job number or a reconciliation case number — a configured code or a
    /// display number, and null where the predicate names nothing. <strong>Never an identity</strong>, so never a
    /// media object identifier, an evidence key, a filename or an object-storage key; <strong>never personal
    /// data</strong>, so never a name, a measurement or a contact detail (security rule 7); and never free text.
    /// </summary>
    public string? Reference { get; }

    /// <summary>Builds a block, refusing a reference this contract may not carry.</summary>
    /// <param name="reason">Which predicate blocked. One of the six <see cref="ReadyBlockReason"/> members.</param>
    /// <param name="reference">
    /// The configured code or display number it names, or null. Blank is null. See the remarks for what is
    /// refused and why.
    /// </param>
    /// <returns>The block.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="reason"/> is not one of the six.</exception>
    /// <exception cref="ArgumentException">
    /// <paramref name="reference"/> is longer than <see cref="MaximumReferenceLength"/>, parses as a GUID, or
    /// carries a character that is neither an ASCII letter or digit nor a hyphen or underscore.
    /// </exception>
    public static ReadyStateBlock Of(ReadyBlockReason reason, string? reference)
    {
        if (!Enum.IsDefined(reason))
        {
            throw new ArgumentOutOfRangeException(
                nameof(reason),
                reason,
                "A ready-state block names one of the six predicates of state-transitions.md section 9.1.");
        }

        var trimmed = reference?.Trim();

        if (string.IsNullOrEmpty(trimmed))
        {
            return new ReadyStateBlock(reason, null);
        }

        if (trimmed.Length > MaximumReferenceLength)
        {
            throw new ArgumentException(
                $"A ready-state block reference is at most {MaximumReferenceLength} characters.",
                nameof(reference));
        }

        if (Guid.TryParse(trimmed, out _))
        {
            throw new ArgumentException(
                "A ready-state block reference is a code or a display number, never an identity: a media object "
                + "identifier, an evidence key or any other GUID may not cross this boundary.",
                nameof(reference));
        }

        if (!IsCodeOrDisplayNumber(trimmed))
        {
            throw new ArgumentException(
                "A ready-state block reference carries ASCII letters, digits, hyphens and underscores only, so "
                + "that neither free text nor an object key can travel on it.",
                nameof(reference));
        }

        return new ReadyStateBlock(reason, trimmed);
    }

    /// <summary>Whether a value has the shape of a configured code or a display number.</summary>
    private static bool IsCodeOrDisplayNumber(string value)
    {
        if (!char.IsAsciiLetterOrDigit(value[0]))
        {
            return false;
        }

        foreach (var character in value)
        {
            if (!char.IsAsciiLetterOrDigit(character) && character is not ('-' or '_'))
            {
                return false;
            }
        }

        return true;
    }
}

/// <summary>One declared dependency of a garment job on another garment of the same order.</summary>
/// <remarks>
/// Published without the reason a person gave for declaring it, without who declared it and without when: the
/// reason is free text (<c>docs/nfr/data-classification.md</c> section 5.7) and the rest is the module's own
/// record. What a consumer needs is the shape of the binding, because that is what the gate and the door act on.
/// </remarks>
/// <param name="PrerequisiteGarmentJobId">The garment this one waits for, or is delivered with.</param>
/// <param name="Relation">Which relationship it is, and therefore which gate acts on it.</param>
public sealed record GarmentJobDependency(Guid PrerequisiteGarmentJobId, JobDependencyRelation Relation);

/// <summary>Where one garment job stands in its own lifecycle, as another module sees it.</summary>
/// <remarks>
/// <para>
/// A published mirror of the module's own <c>GarmentJobStatus</c>: same ordinals, same member names, one for one,
/// and its own type for the reason <see cref="OrderState"/> is.
/// </para>
/// <para>
/// <strong>Phases are not states.</strong> A phase lives inside <see cref="InProduction"/> and is instantiated
/// from the workflow version pinned at start of production, so cutting, stitching and finishing move a phase row
/// and never this enumeration (INV-JOB-02, INV-JOB-03). A screen that shows "at the finishing table" is rendering
/// a phase over a garment that is, to this type, simply in production.
/// </para>
/// <para>
/// <strong>There is no delete.</strong> A garment that will not be made is <see cref="Cancelled"/> and stays
/// readable, because an invoice line, a stock reservation and a custody chain all name it.
/// <see cref="Closed"/> is declared and unreachable while <strong>SQ-01</strong> is open, exactly as
/// <see cref="OrderState.Closed"/> is.
/// </para>
/// </remarks>
public enum GarmentJobState
{
    /// <summary>Created inside the order confirmation, with its snapshots frozen. Irreversible.</summary>
    Confirmed = 0,

    /// <summary>The workflow version is pinned and phases exist. Revision is refused from here (INV-JOB-02).</summary>
    InProduction = 1,

    /// <summary>Waiting on a recorded hold. The ready gate is closed while it stands.</summary>
    OnHold = 2,

    /// <summary>Every ready-gate predicate passed. Written by the gate alone (INV-JOB-07).</summary>
    Ready = 3,

    /// <summary>
    /// Handed over at the door. Irreversible; an accepted alteration is a new commitment, not an undo.
    /// </summary>
    Delivered = 4,

    /// <summary>Finished and out of the working set. Declared, and unreachable while SQ-01 is open.</summary>
    Closed = 5,

    /// <summary>Cancelled with a configured reason code. There is no un-cancel.</summary>
    Cancelled = 6,
}

/// <summary>The six predicates of the ready-for-delivery gate, as a consumer reads them.</summary>
/// <remarks>
/// <para>
/// <strong>The member name is the published reason code.</strong> These are byte-for-byte the domain's
/// <c>ReadyGatePredicate</c> members, which are in turn exactly the names
/// <c>docs/prd/state-transitions.md</c> section 9.1 gives, in that order. INV-JOB-07 requires each predicate to
/// return its own reason so that a queue screen can tell a Tailor Master which of six things to go and do —
/// which makes a rename here a change to a published reason code rather than a refactor.
/// </para>
/// <para>
/// Six and not seven: the set is closed by section 9.1, and an unnamed seventh reason is one nobody could act on.
/// </para>
/// </remarks>
public enum ReadyBlockReason
{
    /// <summary>
    /// A non-skippable phase of the pinned workflow version is not complete. The reference names the phase, by
    /// its configured code.
    /// </summary>
    WorkflowComplete = 0,

    /// <summary>
    /// The latest QC result is not a pass, or a rework task is open (INV-JOB-06). The reference names the defect,
    /// by its configured code.
    /// </summary>
    QcPassed = 1,

    /// <summary>
    /// A piece of evidence the workflow or the checklist requires is missing. The reference names the
    /// <em>kind</em> of evidence required, by its configured code — <strong>never the evidence itself</strong>:
    /// an evidence key or a media object identifier may not cross this boundary, and
    /// <see cref="ReadyStateBlock.Of"/> refuses one.
    /// </summary>
    DocumentationComplete = 2,

    /// <summary>
    /// A holds row is open on the garment. The reference names the hold reason, by its configured code.
    /// </summary>
    NoOpenHold = 3,

    /// <summary>
    /// Another garment of the <c>deliver_together</c> parcel has not met its own predicates in the same
    /// evaluation, and the branch policy does not permit partial delivery (INV-JOB-09, issue #48). The reference
    /// names the sibling's job number.
    /// </summary>
    DependenciesMet = 4,

    /// <summary>
    /// Custody does not report a consistent custodian with no open case. An unknown custody state counts as
    /// blocked, which is the fail-closed reading. The reference names the open reconciliation case, by its case
    /// number.
    /// </summary>
    CustodyReconciled = 5,
}

/// <summary>The two relationships one garment of an order may declare on another.</summary>
/// <remarks>
/// <para>
/// INV-JOB-09 names both, and they are enforced in <strong>different places</strong> — which is why a consumer
/// needs the distinction rather than a single "blocks the job" flag.
/// <see cref="FinishBefore"/> is checked at start of production; <see cref="DeliverTogether"/> is checked by the
/// ready gate and again at the door, and waived where the branch policy permits partial delivery (issue #48).
/// </para>
/// <para>
/// Neither value is a scheduling instruction. The promised date is a separate, reasoned decision
/// (<c>docs/prd/state-transitions.md</c> section 3.2), and a dependency never moves one.
/// </para>
/// </remarks>
public enum JobDependencyRelation
{
    /// <summary>The prerequisite must finish before this garment's first phase may start.</summary>
    FinishBefore = 0,

    /// <summary>The two garments go to the customer together. It never blocks production of either.</summary>
    DeliverTogether = 1,
}
