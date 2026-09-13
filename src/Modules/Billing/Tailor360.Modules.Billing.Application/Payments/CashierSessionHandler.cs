using Microsoft.Extensions.Options;
using Tailor360.Modules.Billing.Application.Abstractions;
using Tailor360.Modules.Billing.Contracts.Events;
using Tailor360.Modules.Billing.Domain;
using Tailor360.Modules.Billing.Domain.Payments;
using Tailor360.Platform.Abstractions.Concurrency;
using Tailor360.Platform.Abstractions.Identifiers;
using Tailor360.Platform.Abstractions.Money;
using Tailor360.Platform.Abstractions.Results;
using Tailor360.Platform.Abstractions.Time;

namespace Tailor360.Modules.Billing.Application.Payments;

/// <summary>
/// Opens and closes cashier sessions. The permission that gates both is <c>payments.session</c>; the audit
/// actions are the two the matrix names. A payment recorded outside an open session is refused (E09-F03-2),
/// so the session is the first thing a cashier's day writes and the last thing it closes.
/// </summary>
/// <param name="sessions">The store.</param>
/// <param name="modes">The payment modes, deciding which modes a close counts.</param>
/// <param name="payments">The payments, whose sums per mode a close is counted against.</param>
/// <param name="batches">The reconciliation batches, one opened per close (INV-CSH-06).</param>
/// <param name="events">Billing's outbox.</param>
/// <param name="options">The variance threshold.</param>
/// <param name="audit">The audit trail.</param>
/// <param name="clock">The clock.</param>
/// <param name="ids">The identifier generator.</param>
public sealed class CashierSessionHandler(
    ICashierSessionStore sessions,
    IPaymentModeStore modes,
    IPaymentStore payments,
    IReconciliationBatchStore batches,
    IBillingEventPublisher events,
    IOptions<CashierOptions> options,
    IBillingAuditWriter audit,
    IClock clock,
    IIdGenerator ids)
{
    /// <summary>The audit action of an opening.</summary>
    public const string OpenedAction = "payments.open_session";

    /// <summary>The audit action of a close.</summary>
    public const string ClosedAction = "payments.close_session";

    /// <summary>Opens a session for the caller at their branch. A second open one at the branch is refused (INV-CSH-01).</summary>
    public async Task<Result<AdministeredCashierSession>> OpenAsync(OpenCashierSessionCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var opened = CashierSession.Open(ids.NewId(), command.OrganisationId, command.BranchId, command.CashierId, Money.Rupees(command.OpeningFloat), clock.UtcNow);
        if (opened.IsFailure)
        {
            return Result.Failure<AdministeredCashierSession>(opened.Error);
        }

        if (await sessions.FindOpenAsync(command.BranchId, command.CashierId, command.OrganisationId, cancellationToken) is not null)
        {
            return Result.Failure<AdministeredCashierSession>(BillingErrors.CashierSessionAlreadyOpen);
        }

        var session = opened.Value;
        sessions.Add(session);

        // Staged before the save so the entry rides the same SaveChangesAsync as the session it
        // describes, and the two commit or roll back together (issue #179).
        await BillingAudit.StageAsync(
            audit, OpenedAction, BillingAudit.CashierSessionEntity, session.Id,
            "Cashier session opened.",
            null, null, CashierSessionSnapshot.Of(session), cancellationToken);

        // The index is the guard; the read above only answers the ordinary case without a round trip
        // that ends in a constraint violation.
        var saved = await sessions.SaveAsync(cancellationToken);
        if (saved.IsFailure)
        {
            return Result.Failure<AdministeredCashierSession>(saved.Error);
        }

        return Result.Success(new AdministeredCashierSession(session, sessions.EntityTagOf(session)));
    }

    /// <summary>
    /// Closes a session against its count sheet. The expected totals are what the session took per mode,
    /// the float counted into cash, over every mode active at the branch, so the close sheet always has a
    /// line for each mode that could have taken money. The close, the counts, the event and the audit row
    /// all commit together.
    /// </summary>
    public async Task<Result<AdministeredCashierSession>> CloseAsync(CloseCashierSessionCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var found = await sessions.FindAsync(command.SessionId, command.OrganisationId, cancellationToken);
        if (found is null)
        {
            return Result.Failure<AdministeredCashierSession>(BillingErrors.CashierSessionNotFound);
        }

        var before = CashierSessionSnapshot.Of(found);

        // The session's row is held against every payment before the expected totals are read, so what
        // the close counts against is what the session holds when it commits (INV-CSH-03): a payment
        // under way commits first and is counted, or waits and is refused.
        var closed = await sessions.CloseInTransactionAsync(command.SessionId, async token =>
        {
            var session = await sessions.FindAsync(command.SessionId, command.OrganisationId, token);
            if (session is null)
            {
                return Result.Failure<CashierSession>(BillingErrors.CashierSessionNotFound);
            }

            var expected = await ExpectedByModeAsync(session, token);
            var now = clock.UtcNow;
            var close = session.Close(command.Denominations, command.ModeTotals, expected, command.Reason, Money.Rupees(options.Value.VarianceReasonThreshold), now, command.By);
            if (close.IsFailure)
            {
                return Result.Failure<CashierSession>(close.Error);
            }

            events.Publish(new CashierSessionClosed(
                ids.NewId(), now, session.Id, session.OrganisationId, session.BranchId, session.CashierId,
                session.OpenedAt, session.ClosedAt!.Value,
                session.ExpectedTotal.Amount, session.CountedTotal.Amount, session.Variance.Amount, session.CountedTotal.Currency));

            // Opened in the same transaction as the close it reconciles (INV-CSH-06); the batch itself
            // decides, from the same threshold, whether the variance it just recorded needs approval.
            batches.Add(ReconciliationBatch.OpenForClose(ids.NewId(), session, Money.Rupees(options.Value.VarianceReasonThreshold), now));

            // Staged before the save so the entry rides the same SaveChangesAsync as the close it
            // describes, and the two commit or roll back together (issue #179). No amount in the
            // summary: the trail is read by more people than the drawer is.
            await BillingAudit.StageAsync(
                audit, ClosedAction, BillingAudit.CashierSessionEntity, session.Id,
                session.Variance.IsZero ? "Cashier session closed; the count agreed." : "Cashier session closed with a variance.",
                command.Reason, before, CashierSessionSnapshot.Of(session), token);

            var saved = await sessions.SaveAsync(token);
            return saved.IsFailure ? Result.Failure<CashierSession>(saved.Error) : Result.Success(session);
        }, cancellationToken);
        if (closed.IsFailure)
        {
            return Result.Failure<AdministeredCashierSession>(closed.Error);
        }

        var session = closed.Value;
        return Result.Success(new AdministeredCashierSession(session, sessions.EntityTagOf(session)));
    }

    /// <remarks>
    /// The expected totals come from the session's own payments (INV-CSH-03), the float counted into cash.
    /// The key set is the modes with a payment in this session in union with the modes available at the
    /// branch today: a mode deactivated or restricted mid-shift after taking money still has a line to
    /// count, and a counted line for it is not refused as unknown.
    /// </remarks>
    private async Task<IReadOnlyDictionary<string, Money>> ExpectedByModeAsync(CashierSession session, CancellationToken cancellationToken)
    {
        var all = await modes.ListAsync(session.OrganisationId, cancellationToken);
        var expected = new Dictionary<string, Money>(StringComparer.Ordinal);
        foreach (var mode in all.Where(mode => mode.IsAvailableAt(session.BranchId)))
        {
            expected[mode.Code] = Money.Zero;
        }

        foreach (var (code, taken) in await payments.TakenByModeAsync(session.Id, cancellationToken))
        {
            expected[code] = taken;
        }

        // The drawer holds the float whether or not cash is a mode anyone configured: a sheet that could
        // not count it would hide the float from the reconciliation.
        expected[PaymentModeCodes.Cash] = expected.GetValueOrDefault(PaymentModeCodes.Cash, Money.Zero) + session.OpeningFloat;
        return expected;
    }
}

/// <summary>Open a session.</summary>
/// <param name="OrganisationId">The caller's organisation.</param>
/// <param name="BranchId">The caller's branch.</param>
/// <param name="CashierId">The caller.</param>
/// <param name="OpeningFloat">The cash put in the drawer, in rupees.</param>
public sealed record OpenCashierSessionCommand(Guid OrganisationId, Guid BranchId, Guid CashierId, decimal OpeningFloat);

/// <summary>Close a session.</summary>
/// <param name="SessionId">The session.</param>
/// <param name="OrganisationId">The caller's organisation.</param>
/// <param name="Denominations">The count sheet.</param>
/// <param name="ModeTotals">What was counted per mode other than cash.</param>
/// <param name="Reason">Why the count differs, where it does.</param>
/// <param name="By">The caller; must be the session's cashier.</param>
public sealed record CloseCashierSessionCommand(
    Guid SessionId,
    Guid OrganisationId,
    IReadOnlyList<DenominationCount> Denominations,
    IReadOnlyList<ModeCount> ModeTotals,
    string? Reason,
    Guid By);

/// <summary>A session and the token its next change is made against.</summary>
public sealed record AdministeredCashierSession(CashierSession Session, EntityTag Tag);

/// <summary>What the audit trail records of a session: identifiers, times and the status — no amount.</summary>
internal sealed record CashierSessionSnapshot(Guid BranchId, Guid CashierId, string Status, DateTimeOffset OpenedAt, DateTimeOffset? ClosedAt, int CountLines, int ModeLines, bool HasVariance)
{
    public static CashierSessionSnapshot Of(CashierSession session)
        => new(session.BranchId, session.CashierId, session.Status.ToString(), session.OpenedAt, session.ClosedAt, session.Counts.Count, session.ModeTotals.Count, !session.Variance.IsZero);
}
