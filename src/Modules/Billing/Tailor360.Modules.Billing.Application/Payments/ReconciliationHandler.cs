using Tailor360.Modules.Billing.Application.Abstractions;
using Tailor360.Modules.Billing.Contracts.Events;
using Tailor360.Modules.Billing.Domain;
using Tailor360.Modules.Billing.Domain.Payments;
using Tailor360.Platform.Abstractions.Identifiers;
using Tailor360.Platform.Abstractions.Results;
using Tailor360.Platform.Abstractions.Time;

namespace Tailor360.Modules.Billing.Application.Payments;

/// <summary>
/// Approves the variance a closed cashier session's reconciliation batch carries, by someone other than
/// the cashier who closed it (INV-CSH-04, INV-CSH-06). The permission is <c>payments.approve_reconciliation</c>.
/// </summary>
/// <param name="batches">The store.</param>
/// <param name="events">Billing's outbox.</param>
/// <param name="audit">The audit trail.</param>
/// <param name="clock">The clock.</param>
/// <param name="ids">The identifier generator.</param>
public sealed class ReconciliationHandler(
    IReconciliationBatchStore batches,
    IBillingEventPublisher events,
    IBillingAuditWriter audit,
    IClock clock,
    IIdGenerator ids)
{
    /// <summary>The audit action; the matrix's row for <c>payments.approve_reconciliation</c>.</summary>
    public const string ApprovedAction = "payments.approve_reconciliation";

    /// <summary>
    /// Approves the session's batch: refused where the session has none (closed before this shipped),
    /// where nothing was ever pending, where it is approved already, or where the approver is the
    /// cashier who closed the session.
    /// </summary>
    public async Task<Result<ReconciliationBatch>> ApproveAsync(ApproveReconciliationCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var found = await batches.FindBySessionAsync(command.SessionId, command.OrganisationId, cancellationToken);
        if (found is null)
        {
            return Result.Failure<ReconciliationBatch>(BillingErrors.ReconciliationBatchNotFound);
        }

        var before = ReconciliationBatchSnapshot.Of(found);

        var approved = await batches.ApproveInTransactionAsync(command.SessionId, async token =>
        {
            var batch = await batches.FindBySessionAsync(command.SessionId, command.OrganisationId, token);
            if (batch is null)
            {
                return Result.Failure<ReconciliationBatch>(BillingErrors.ReconciliationBatchNotFound);
            }

            var now = clock.UtcNow;
            var approve = batch.Approve(command.By, command.Reason, now);
            if (approve.IsFailure)
            {
                return Result.Failure<ReconciliationBatch>(approve.Error);
            }

            events.Publish(new ReconciliationApproved(
                ids.NewId(), now, batch.Id, batch.OrganisationId, batch.BranchId, batch.CashierSessionId,
                command.By, batch.Variance.Amount, batch.Variance.Currency));

            // Staged before the save so the entry rides the same SaveChangesAsync as the approval it
            // describes, and the two commit or roll back together (issue #179). No amount in the
            // summary: the trail is read by more people than the drawer is.
            await BillingAudit.StageAsync(
                audit, ApprovedAction, BillingAudit.ReconciliationBatchEntity, batch.Id,
                "Cashier session variance approved.",
                command.Reason!.Trim(), before, ReconciliationBatchSnapshot.Of(batch), token);

            var saved = await batches.SaveAsync(token);
            return saved.IsFailure ? Result.Failure<ReconciliationBatch>(saved.Error) : Result.Success(batch);
        }, cancellationToken);

        return approved;
    }
}

/// <summary>Approve a session's reconciliation batch.</summary>
/// <param name="SessionId">The session.</param>
/// <param name="OrganisationId">The caller's organisation.</param>
/// <param name="Reason">Why, as the trail records it.</param>
/// <param name="By">The caller; refused where it is the cashier who closed the session.</param>
public sealed record ApproveReconciliationCommand(Guid SessionId, Guid OrganisationId, string? Reason, Guid By);

/// <summary>What the audit trail records of a batch: identifiers, status and times — no amount.</summary>
internal sealed record ReconciliationBatchSnapshot(Guid CashierSessionId, string Status, Guid? ApprovedBy, DateTimeOffset? ApprovedAt)
{
    public static ReconciliationBatchSnapshot Of(ReconciliationBatch batch)
        => new(batch.CashierSessionId, batch.Status.ToString(), batch.ApprovedBy, batch.ApprovedAt);
}
