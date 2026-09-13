using Tailor360.Modules.Billing.Domain.Invoicing;
using Tailor360.Platform.Abstractions.Money;
using Tailor360.Platform.Abstractions.Results;

namespace Tailor360.Modules.Billing.Domain.Payments;

/// <summary>What a reconciliation batch reconciles. One value today; the seed for a provider settlement
/// batch later (OD-03), which is why it is a column rather than an assumption.</summary>
public enum ReconciliationBatchType
{
    /// <summary>A cashier session's close.</summary>
    CashierSession = 0,
}

/// <summary>Whether a batch's variance needs a second person's approval, and whether it has one.</summary>
public enum ReconciliationBatchStatus
{
    /// <summary>No variance beyond the threshold; nothing to approve, and an approval is refused.</summary>
    NotRequired = 0,

    /// <summary>A variance beyond the threshold, awaiting approval by someone other than who closed it.</summary>
    Pending = 1,

    /// <summary>Approved, once, by someone other than who closed it.</summary>
    Approved = 2,
}

/// <summary>
/// What expected was checked against what was recorded, by mode, for one closed cashier session
/// (INV-CSH-06). Opened in the same transaction as the close it reconciles, from the session's own
/// totals — it duplicates them rather than reading the session again later, because the session is
/// immutable from the moment it closes and the batch is the record of what that close found. Never
/// rewrites the payments, the reversals or the refunds it reconciles; a variance beyond the
/// configured threshold (the same number <c>CashierSession.Close</c> asks a reason for, OD-24) is
/// approved by a different user from the cashier who closed it (INV-CSH-04), once.
/// </summary>
public sealed class ReconciliationBatch
{
    /// <summary>The longest reason the approval may carry — the same as an invoice's.</summary>
    public const int MaximumReasonLength = Invoice.MaximumReasonLength;

    private readonly List<ReconciliationBatchModeLine> _modeLines = [];

    private ReconciliationBatch()
    {
        // The persistence layer materialises instances through this constructor.
    }

    private ReconciliationBatch(
        Guid id, Guid organisationId, Guid branchId, Guid cashierSessionId, ReconciliationBatchType type, Guid closedBy,
        Money expectedTotal, Money recordedTotal, string? closeReason, ReconciliationBatchStatus status, DateTimeOffset now)
    {
        Id = id;
        OrganisationId = organisationId;
        BranchId = branchId;
        CashierSessionId = cashierSessionId;
        Type = type;
        ClosedBy = closedBy;
        ExpectedTotal = expectedTotal;
        RecordedTotal = recordedTotal;
        Variance = recordedTotal - expectedTotal;
        CloseReason = closeReason;
        Status = status;
        CreatedAt = now;
    }

    /// <summary>Identifier.</summary>
    public Guid Id { get; private set; }

    /// <summary>The organisation.</summary>
    public Guid OrganisationId { get; private set; }

    /// <summary>The branch whose drawer the reconciled session was.</summary>
    public Guid BranchId { get; private set; }

    /// <summary>The session it reconciles.</summary>
    public Guid CashierSessionId { get; private set; }

    /// <summary>What it reconciles.</summary>
    public ReconciliationBatchType Type { get; private set; }

    /// <summary>The cashier who closed the session; an approval by this person is refused (INV-CSH-04).</summary>
    public Guid ClosedBy { get; private set; }

    /// <summary>Over every mode, what the session should have held, as the close computed it.</summary>
    public Money ExpectedTotal { get; private set; } = Money.Zero;

    /// <summary>Over every mode, what was counted, as the close recorded it.</summary>
    public Money RecordedTotal { get; private set; } = Money.Zero;

    /// <summary>Recorded minus expected, over every mode.</summary>
    public Money Variance { get; private set; } = Money.Zero;

    /// <summary>Why the count differed, in the closing cashier's own words; null where it did not.</summary>
    public string? CloseReason { get; private set; }

    /// <summary>Whether the variance needs approval, and whether it has one.</summary>
    public ReconciliationBatchStatus Status { get; private set; }

    /// <summary>Who approved it; null until approved.</summary>
    public Guid? ApprovedBy { get; private set; }

    /// <summary>When it was approved; null until then.</summary>
    public DateTimeOffset? ApprovedAt { get; private set; }

    /// <summary>The approver's own reason; null until approved.</summary>
    public string? ApprovalReason { get; private set; }

    /// <summary>When the batch was opened, at the session's close.</summary>
    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>Expected against recorded, per mode, written once at open.</summary>
    public IReadOnlyCollection<ReconciliationBatchModeLine> ModeLines => _modeLines;

    /// <summary>Whether this batch was ever going to need an approval.</summary>
    public bool ApprovalRequired => Status != ReconciliationBatchStatus.NotRequired;

    /// <summary>Whether it has been approved.</summary>
    public bool IsApproved => Status == ReconciliationBatchStatus.Approved;

    /// <summary>
    /// Opens the batch for a session that has just closed, from the totals the close computed. Called
    /// inside the same transaction as the close (<c>CashierSessionHandler.CloseAsync</c>), against a
    /// session the caller has already closed successfully — there is nothing here for a caller to get
    /// wrong, so this returns the batch rather than a <see cref="Result{T}"/>.
    /// </summary>
    /// <param name="id">A freshly minted identifier.</param>
    /// <param name="session">The session, closed.</param>
    /// <param name="varianceThreshold">
    /// The largest per-mode variance, over or short, that needs no approval — the same figure the close
    /// asked a reason for (OD-24).
    /// </param>
    /// <param name="now">When.</param>
    public static ReconciliationBatch OpenForClose(Guid id, CashierSession session, Money varianceThreshold, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(session);

        if (session.IsOpen || session.ClosedBy is not { } closedBy)
        {
            throw new InvalidOperationException("A reconciliation batch is opened for a session that has just closed.");
        }

        var beyondThreshold = session.ModeTotals.Any(total => Math.Abs(total.Variance) > varianceThreshold.Amount);
        var batch = new ReconciliationBatch(
            id, session.OrganisationId, session.BranchId, session.Id, ReconciliationBatchType.CashierSession, closedBy,
            session.ExpectedTotal, session.CountedTotal, session.VarianceReason,
            beyondThreshold ? ReconciliationBatchStatus.Pending : ReconciliationBatchStatus.NotRequired, now);

        batch._modeLines.AddRange(session.ModeTotals
            .Select(total => ReconciliationBatchModeLine.Of(id, total.ModeCode, total.Expected, total.Counted)));

        return batch;
    }

    /// <summary>
    /// Approves the variance: refused where none was ever pending, where it is approved already, where
    /// the approver is the cashier who closed the session (INV-CSH-04), or without a reason.
    /// </summary>
    public Result Approve(Guid approvedBy, string? reason, DateTimeOffset now)
    {
        if (Status == ReconciliationBatchStatus.Approved)
        {
            return Result.Failure(BillingErrors.ReconciliationBatchAlreadyApproved);
        }

        if (Status == ReconciliationBatchStatus.NotRequired)
        {
            return Result.Failure(BillingErrors.ReconciliationApprovalNotRequired);
        }

        if (approvedBy == ClosedBy)
        {
            return Result.Failure(BillingErrors.ReconciliationApprovalBySameUser);
        }

        var checkedReason = Invoice.CheckReason(reason);
        if (checkedReason.IsFailure)
        {
            return checkedReason;
        }

        Status = ReconciliationBatchStatus.Approved;
        ApprovedBy = approvedBy;
        ApprovedAt = now;
        ApprovalReason = reason!.Trim();

        return Result.Success();
    }
}

/// <summary>One mode's expected against recorded, on a reconciliation batch.</summary>
public sealed class ReconciliationBatchModeLine
{
    private ReconciliationBatchModeLine()
    {
        // The persistence layer materialises instances through this constructor.
    }

    private ReconciliationBatchModeLine(Guid batchId, string modeCode, decimal expected, decimal recorded)
    {
        BatchId = batchId;
        ModeCode = modeCode;
        Expected = expected;
        Recorded = recorded;
        Variance = recorded - expected;
    }

    /// <summary>The batch.</summary>
    public Guid BatchId { get; private set; }

    /// <summary>The payment mode's code.</summary>
    public string ModeCode { get; private set; } = string.Empty;

    /// <summary>What the session's close expected in this mode.</summary>
    public decimal Expected { get; private set; }

    /// <summary>What the close recorded as counted in this mode.</summary>
    public decimal Recorded { get; private set; }

    /// <summary>Recorded minus expected.</summary>
    public decimal Variance { get; private set; }

    /// <summary>A line.</summary>
    public static ReconciliationBatchModeLine Of(Guid batchId, string modeCode, decimal expected, decimal recorded) => new(batchId, modeCode, expected, recorded);
}
