using Microsoft.Extensions.Options;
using Tailor360.Modules.Billing.Application.Abstractions;
using Tailor360.Modules.Billing.Contracts.Events;
using Tailor360.Modules.Billing.Domain;
using Tailor360.Modules.Billing.Domain.Payments;
using Tailor360.Platform.Abstractions.Auditing;
using Tailor360.Platform.Abstractions.Identifiers;
using Tailor360.Platform.Abstractions.Money;
using Tailor360.Platform.Abstractions.Results;
using Tailor360.Platform.Abstractions.Time;

namespace Tailor360.Modules.Billing.Application.Payments;

/// <summary>
/// Approves a single-use dispatch exception (#164, plan lines 1908-1916), and expires the ones the
/// worker finds past their expiry, never consumed.
/// </summary>
/// <param name="exceptions">The store.</param>
/// <param name="orders">What Billing knows about the order, to check the job set at approval.</param>
/// <param name="policy">The dispatch policy in force, for its version.</param>
/// <param name="events">Billing's outbox.</param>
/// <param name="audit">The audit trail.</param>
/// <param name="clock">The clock.</param>
/// <param name="ids">The identifier generator.</param>
public sealed class DispatchExceptionHandler(
    IDispatchExceptionStore exceptions,
    IOrderFactStore orders,
    IOptions<DispatchPolicyOptions> policy,
    IBillingEventPublisher events,
    IAuditWriter audit,
    IClock clock,
    IIdGenerator ids)
{
    /// <summary>The audit action; the matrix's row for <c>billing.approve_dispatch_exception</c>.</summary>
    public const string ApprovedAction = "billing.approve_dispatch_exception";

    /// <summary>The audit action for the worker's expiry pass.</summary>
    public const string ExpiredAction = "billing.expire_dispatch_exception";

    /// <summary>
    /// Approves a single-use dispatch exception: refused where the order is not Billing's, is at
    /// another branch, or names a job that is not a live job of the order, and refused by the domain's
    /// own checks on the amount, the expiry and the reason.
    /// </summary>
    public async Task<Result<DispatchException>> ApproveAsync(ApproveDispatchExceptionCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var order = await orders.FindAsync(command.OrderId, command.OrganisationId, cancellationToken);
        if (order is null)
        {
            return Result.Failure<DispatchException>(BillingErrors.OrderNotKnown);
        }

        if (order.BranchId != command.BranchId)
        {
            return Result.Failure<DispatchException>(BillingErrors.OrderAtAnotherBranch);
        }

        var jobIds = command.JobIds ?? [];
        foreach (var jobId in jobIds)
        {
            var job = order.FindJob(jobId);
            if (job is null || job.IsCancelled)
            {
                return Result.Failure<DispatchException>(BillingErrors.DispatchExceptionJobNotOfOrder("jobIds"));
            }
        }

        var exceptionId = ids.NewId();
        var now = clock.UtcNow;
        var approved = DispatchException.Approve(
            exceptionId, command.OrganisationId, command.BranchId, command.OrderId, jobIds,
            Money.Rupees(command.MaxOutstandingAmount), policy.Value.Version, command.ReasonCode, command.ReasonText,
            command.By, command.ExpiresAt, now);
        if (approved.IsFailure)
        {
            return approved;
        }

        exceptions.Add(approved.Value);
        events.Publish(new DispatchExceptionApproved(
            ids.NewId(), now, approved.Value.Id, command.OrganisationId, command.BranchId, command.OrderId, command.By,
            approved.Value.PolicyVersion, approved.Value.MaxOutstandingAmount.Amount, approved.Value.MaxOutstandingAmount.Currency, approved.Value.ExpiresAt));

        var saved = await exceptions.SaveAsync(cancellationToken);
        if (saved.IsFailure)
        {
            return Result.Failure<DispatchException>(saved.Error);
        }

        // No amount in the summary: the trail is read by more people than the approver is.
        await BillingAudit.RecordAsync(
            audit, ApprovedAction, BillingAudit.DispatchExceptionEntity, approved.Value.Id,
            $"Dispatch exception approved for {jobIds.Count} job(s) under reason {approved.Value.ReasonCode}.",
            command.ReasonText!.Trim(), null, DispatchExceptionSnapshot.Of(approved.Value), cancellationToken);

        return approved;
    }

    /// <summary>
    /// Expires every exception the worker finds still approved and past its expiry, up to
    /// <paramref name="batchSize"/> at a time. Never touches one already consumed or already expired.
    /// </summary>
    public async Task<Result<int>> ExpireDueAsync(int batchSize, CancellationToken cancellationToken = default)
    {
        var now = clock.UtcNow;
        var due = await exceptions.ListDueForExpiryAsync(now, batchSize, cancellationToken);
        var expired = 0;

        foreach (var candidate in due)
        {
            var outcome = await exceptions.RunExclusiveAsync(candidate.Id, async token =>
            {
                var fresh = await exceptions.FindAsync(candidate.Id, candidate.OrganisationId, token);
                if (fresh is null)
                {
                    return Result.Failure<DispatchException>(BillingErrors.DispatchExceptionNotFound);
                }

                var expire = fresh.Expire(clock.UtcNow);
                if (expire.IsFailure)
                {
                    return Result.Failure<DispatchException>(expire.Error);
                }

                events.Publish(new DispatchExceptionExpired(ids.NewId(), clock.UtcNow, fresh.Id, fresh.OrganisationId, fresh.BranchId, fresh.OrderId));

                var saved = await exceptions.SaveAsync(token);
                return saved.IsFailure ? Result.Failure<DispatchException>(saved.Error) : Result.Success(fresh);
            },
            async token => (await exceptions.FindAsync(candidate.Id, candidate.OrganisationId, token))?.Status == DispatchExceptionStatus.Expired,
            cancellationToken);

            if (outcome.IsFailure)
            {
                continue;
            }

            expired++;
            await BillingAudit.RecordAsync(
                audit, ExpiredAction, BillingAudit.DispatchExceptionEntity, outcome.Value.Id,
                "Dispatch exception expired, never consumed.", null, null, DispatchExceptionSnapshot.Of(outcome.Value), cancellationToken);
        }

        return Result.Success(expired);
    }
}

/// <summary>Approve a single-use dispatch exception.</summary>
/// <param name="OrganisationId">The caller's organisation.</param>
/// <param name="BranchId">The caller's branch; refused where it differs from the order's.</param>
/// <param name="OrderId">The order.</param>
/// <param name="JobIds">Exactly the garment jobs it covers; every one must be a live job of the order.</param>
/// <param name="MaxOutstandingAmount">The most the order may still owe when it is consumed, in rupees to the paisa.</param>
/// <param name="ReasonCode">The configured reason code.</param>
/// <param name="ReasonText">The approver's own free text.</param>
/// <param name="ExpiresAt">When it stops being consumable; at most 72 hours from now.</param>
/// <param name="By">The approver.</param>
public sealed record ApproveDispatchExceptionCommand(
    Guid OrganisationId, Guid BranchId, Guid OrderId, IReadOnlyList<Guid>? JobIds,
    decimal MaxOutstandingAmount, string? ReasonCode, string? ReasonText, DateTimeOffset ExpiresAt, Guid By);

/// <summary>What the audit trail records of a dispatch exception: identifiers, the reason code and status — no amount.</summary>
internal sealed record DispatchExceptionSnapshot(Guid OrderId, int JobCount, string ReasonCode, string Status, Guid? ConsumedBy)
{
    public static DispatchExceptionSnapshot Of(DispatchException exception)
        => new(exception.OrderId, exception.JobIds.Count, exception.ReasonCode, exception.Status.ToString(), exception.ConsumedBy);
}
