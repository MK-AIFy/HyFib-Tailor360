using Tailor360.Modules.Billing.Domain.Invoicing;
using Tailor360.Platform.Abstractions.Money;
using Tailor360.Platform.Abstractions.Results;

namespace Tailor360.Modules.Billing.Domain.Payments;

/// <summary>Where a dispatch exception stands in its one-way lifecycle.</summary>
public enum DispatchExceptionStatus
{
    /// <summary>Approved and not yet acted on. The only status a dispatch may still be granted through.</summary>
    Approved = 0,

    /// <summary>Consumed by the dispatch authorisation it covered. Terminal.</summary>
    Consumed = 1,

    /// <summary>Past its expiry, never consumed. Terminal.</summary>
    Expired = 2,
}

/// <summary>
/// A single-use override of the dispatch policy for named jobs of one order, up to a stated outstanding
/// balance, approved by the Owner and good for at most 72 hours (plan lines 1908-1916,
/// <c>docs/prd/exceptions.md</c> EX-10). It never edits the balance it overrides: it is consumed exactly
/// once by the dispatch authorisation that relies on it, which re-validates everything it was bound to
/// against the state at that moment, because time has passed since it was approved.
/// </summary>
public sealed class DispatchException
{
    /// <summary>The longest a reason code holds.</summary>
    public const int MaximumReasonCodeLength = 60;

    /// <summary>The longest a reason's free text holds — the same as an invoice's.</summary>
    public const int MaximumReasonTextLength = Invoice.MaximumReasonLength;

    /// <summary>The longest an exception is ever valid for, from approval.</summary>
    public const int MaximumValidityHours = 72;

    private readonly List<DispatchExceptionJob> _jobs = [];

    private DispatchException()
    {
        // The persistence layer materialises instances through this constructor.
    }

    private DispatchException(
        Guid id, Guid organisationId, Guid branchId, Guid orderId, IReadOnlyCollection<Guid> jobIds,
        Money maxOutstandingAmount, string policyVersion, string reasonCode, string reasonText,
        Guid approvedBy, DateTimeOffset approvedAt, DateTimeOffset expiresAt)
    {
        Id = id;
        OrganisationId = organisationId;
        BranchId = branchId;
        OrderId = orderId;
        _jobs.AddRange(jobIds.Select(jobId => DispatchExceptionJob.For(id, jobId)));
        MaxOutstandingAmount = maxOutstandingAmount;
        PolicyVersion = policyVersion;
        ReasonCode = reasonCode;
        ReasonText = reasonText;
        ApprovedBy = approvedBy;
        ApprovedAt = approvedAt;
        ExpiresAt = expiresAt;
        Status = DispatchExceptionStatus.Approved;
    }

    /// <summary>Identifier.</summary>
    public Guid Id { get; private set; }

    /// <summary>The organisation.</summary>
    public Guid OrganisationId { get; private set; }

    /// <summary>The branch the order belongs to.</summary>
    public Guid BranchId { get; private set; }

    /// <summary>The order it covers.</summary>
    public Guid OrderId { get; private set; }

    /// <summary>The garment jobs it covers, as rows — written once at approval and never changed.</summary>
    public IReadOnlyCollection<DispatchExceptionJob> Jobs => _jobs;

    /// <summary>Exactly the garment jobs it covers; a dispatch of a different set is refused at consumption.</summary>
    public IReadOnlyCollection<Guid> JobIds => [.. _jobs.Select(job => job.GarmentJobId)];

    /// <summary>The most the order may still owe when it is consumed.</summary>
    public Money MaxOutstandingAmount { get; private set; } = Money.Zero;

    /// <summary>The dispatch policy version in force when it was approved.</summary>
    public string PolicyVersion { get; private set; } = string.Empty;

    /// <summary>The configured reason code the approval was given under.</summary>
    public string ReasonCode { get; private set; } = string.Empty;

    /// <summary>The approver's own free text.</summary>
    public string ReasonText { get; private set; } = string.Empty;

    /// <summary>The Owner who approved it; refused as the dispatcher at consumption (INV-CSH-04's shape, for dispatch).</summary>
    public Guid ApprovedBy { get; private set; }

    /// <summary>When it was approved.</summary>
    public DateTimeOffset ApprovedAt { get; private set; }

    /// <summary>When it stops being consumable; at most 72 hours after approval.</summary>
    public DateTimeOffset ExpiresAt { get; private set; }

    /// <summary>Approved, Consumed or Expired.</summary>
    public DispatchExceptionStatus Status { get; private set; }

    /// <summary>Who consumed it; null until consumed.</summary>
    public Guid? ConsumedBy { get; private set; }

    /// <summary>When it was consumed; null until then.</summary>
    public DateTimeOffset? ConsumedAt { get; private set; }

    /// <summary>When it was found expired and marked so; null until then.</summary>
    public DateTimeOffset? ExpiredAt { get; private set; }

    /// <summary>
    /// Approves a single-use dispatch exception: refused for an empty job set, a non-positive maximum,
    /// an expiry not strictly in the future or more than <see cref="MaximumValidityHours"/> hours ahead,
    /// a missing reason code, or a reason text that fails <see cref="Invoice.CheckReason"/>. The caller
    /// has already checked the jobs are of this order and the branch is the order's (the domain has no
    /// way to check that itself).
    /// </summary>
    public static Result<DispatchException> Approve(
        Guid id, Guid organisationId, Guid branchId, Guid orderId, IReadOnlyCollection<Guid> jobIds,
        Money maxOutstandingAmount, string policyVersion, string? reasonCode, string? reasonText,
        Guid approvedBy, DateTimeOffset expiresAt, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(jobIds);

        if (jobIds.Count == 0)
        {
            return Result.Failure<DispatchException>(BillingErrors.Required("jobIds"));
        }

        if (maxOutstandingAmount.IsNegative || maxOutstandingAmount.IsZero)
        {
            return Result.Failure<DispatchException>(BillingErrors.DispatchExceptionAmountNotPositive);
        }

        if (expiresAt <= now || expiresAt > now.AddHours(MaximumValidityHours))
        {
            return Result.Failure<DispatchException>(BillingErrors.DispatchExceptionExpiryNotWellFormed);
        }

        var trimmedCode = reasonCode?.Trim() ?? string.Empty;
        if (trimmedCode.Length == 0)
        {
            return Result.Failure<DispatchException>(BillingErrors.Required("reasonCode"));
        }

        if (trimmedCode.Length > MaximumReasonCodeLength)
        {
            return Result.Failure<DispatchException>(BillingErrors.TooLong("reasonCode", MaximumReasonCodeLength));
        }

        var checkedReason = Invoice.CheckReason(reasonText);
        if (checkedReason.IsFailure)
        {
            return Result.Failure<DispatchException>(checkedReason.Error);
        }

        return Result.Success(new DispatchException(
            id, organisationId, branchId, orderId, [.. new HashSet<Guid>(jobIds)],
            maxOutstandingAmount, policyVersion, trimmedCode, reasonText!.Trim(), approvedBy, now, expiresAt));
    }

    /// <summary>
    /// Consumes the exception exactly once: refused where it has been consumed already, where it is
    /// expired (by status or because time has passed it), where the dispatcher is the person who
    /// approved it, where the job set named no longer matches exactly, where the policy has moved on
    /// from the version it was approved under, or where the order now owes more than it was approved for.
    /// </summary>
    public Result Consume(Guid dispatcherId, Money currentOutstanding, IReadOnlyCollection<Guid> currentJobIds, string currentPolicyVersion, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(currentJobIds);

        if (Status == DispatchExceptionStatus.Consumed)
        {
            return Result.Failure(BillingErrors.DispatchExceptionAlreadyConsumed);
        }

        if (Status == DispatchExceptionStatus.Expired || now >= ExpiresAt)
        {
            return Result.Failure(BillingErrors.DispatchExceptionExpired);
        }

        if (dispatcherId == ApprovedBy)
        {
            return Result.Failure(BillingErrors.DispatchExceptionApproverIsDispatcher);
        }

        if (!MatchesJobSet(currentJobIds))
        {
            return Result.Failure(BillingErrors.DispatchExceptionJobSetChanged);
        }

        if (!string.Equals(currentPolicyVersion, PolicyVersion, StringComparison.Ordinal))
        {
            return Result.Failure(BillingErrors.DispatchExceptionPolicyVersionChanged);
        }

        if (currentOutstanding > MaxOutstandingAmount)
        {
            return Result.Failure(BillingErrors.DispatchExceptionBalanceExceeded);
        }

        Status = DispatchExceptionStatus.Consumed;
        ConsumedBy = dispatcherId;
        ConsumedAt = now;

        return Result.Success();
    }

    /// <summary>
    /// Marks a past-due, never-consumed exception expired. Already expired is a no-op (the worker's
    /// lease is not the only thing standing between two passes and a double write); already consumed is
    /// refused, because a consumed exception is never expired out from under the dispatch it authorised.
    /// </summary>
    public Result Expire(DateTimeOffset now)
    {
        if (Status == DispatchExceptionStatus.Expired)
        {
            return Result.Success();
        }

        if (Status == DispatchExceptionStatus.Consumed)
        {
            return Result.Failure(BillingErrors.DispatchExceptionAlreadyConsumed);
        }

        if (now < ExpiresAt)
        {
            return Result.Failure(BillingErrors.DispatchExceptionNotYetDue);
        }

        Status = DispatchExceptionStatus.Expired;
        ExpiredAt = now;

        return Result.Success();
    }

    /// <summary>Whether it is still approved and not past its expiry: the state a live answer requires.</summary>
    public bool IsLive(DateTimeOffset now) => Status == DispatchExceptionStatus.Approved && now < ExpiresAt;

    /// <summary>Whether this exception covers exactly this set of jobs — no more, no fewer.</summary>
    public bool MatchesJobSet(IReadOnlyCollection<Guid> jobIds)
    {
        ArgumentNullException.ThrowIfNull(jobIds);

        return _jobs.Count == jobIds.Count && new HashSet<Guid>(_jobs.Select(job => job.GarmentJobId)).SetEquals(jobIds);
    }
}

/// <summary>One garment job a dispatch exception covers, written once at approval and never changed.</summary>
public sealed class DispatchExceptionJob
{
    private DispatchExceptionJob()
    {
        // The persistence layer materialises instances through this constructor.
    }

    private DispatchExceptionJob(Guid dispatchExceptionId, Guid garmentJobId)
    {
        DispatchExceptionId = dispatchExceptionId;
        GarmentJobId = garmentJobId;
    }

    /// <summary>The exception.</summary>
    public Guid DispatchExceptionId { get; private set; }

    /// <summary>The garment job it covers.</summary>
    public Guid GarmentJobId { get; private set; }

    /// <summary>A job row.</summary>
    public static DispatchExceptionJob For(Guid dispatchExceptionId, Guid garmentJobId) => new(dispatchExceptionId, garmentJobId);
}
