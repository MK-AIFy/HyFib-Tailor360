using Microsoft.Extensions.Options;
using Tailor360.Modules.Billing.Application.Abstractions;
using Tailor360.Modules.Billing.Contracts.Events;
using Tailor360.Modules.Billing.Domain;
using Tailor360.Modules.Billing.Domain.Payments;
using Tailor360.Platform.Abstractions.Auditing;
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
/// <param name="events">Billing's outbox.</param>
/// <param name="options">The variance threshold.</param>
/// <param name="audit">The audit trail.</param>
/// <param name="clock">The clock.</param>
/// <param name="ids">The identifier generator.</param>
public sealed class CashierSessionHandler(
    ICashierSessionStore sessions,
    IPaymentModeStore modes,
    IBillingEventPublisher events,
    IOptions<CashierOptions> options,
    IAuditWriter audit,
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

        // The index is the guard; the read above only answers the ordinary case without a round trip
        // that ends in a constraint violation.
        var saved = await sessions.SaveAsync(cancellationToken);
        if (saved.IsFailure)
        {
            return Result.Failure<AdministeredCashierSession>(saved.Error);
        }

        await BillingAudit.RecordAsync(
            audit, OpenedAction, BillingAudit.CashierSessionEntity, session.Id,
            "Cashier session opened.",
            null, null, CashierSessionSnapshot.Of(session), cancellationToken);

        return Result.Success(new AdministeredCashierSession(session, sessions.EntityTagOf(session)));
    }

    /// <summary>
    /// Closes a session against its count sheet. The expected totals are what the session recorded per
    /// mode — the float for cash, and nothing else until E09-F03-2 records payments in it — over every
    /// mode active at the branch, so the close sheet always has a line for each mode that could have taken
    /// money. The close, the counts and the event commit together; the audit row follows.
    /// </summary>
    public async Task<Result<AdministeredCashierSession>> CloseAsync(CloseCashierSessionCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var session = await sessions.FindAsync(command.SessionId, command.OrganisationId, cancellationToken);
        if (session is null)
        {
            return Result.Failure<AdministeredCashierSession>(BillingErrors.CashierSessionNotFound);
        }

        var expected = await ExpectedByModeAsync(session, cancellationToken);
        var before = CashierSessionSnapshot.Of(session);
        var now = clock.UtcNow;
        var closed = session.Close(command.Denominations, command.ModeTotals, expected, command.Reason, Money.Rupees(options.Value.VarianceReasonThreshold), now, command.By);
        if (closed.IsFailure)
        {
            return Result.Failure<AdministeredCashierSession>(closed.Error);
        }

        events.Publish(new CashierSessionClosed(
            ids.NewId(), now, session.Id, session.OrganisationId, session.BranchId, session.CashierId,
            session.OpenedAt, session.ClosedAt!.Value,
            session.ExpectedTotal.Amount, session.CountedTotal.Amount, session.Variance.Amount, session.CountedTotal.Currency));

        var saved = await sessions.SaveAsync(cancellationToken);
        if (saved.IsFailure)
        {
            return Result.Failure<AdministeredCashierSession>(saved.Error);
        }

        // No amount in the summary: the trail is read by more people than the drawer is.
        await BillingAudit.RecordAsync(
            audit, ClosedAction, BillingAudit.CashierSessionEntity, session.Id,
            session.Variance.IsZero ? "Cashier session closed; the count agreed." : "Cashier session closed with a variance.",
            command.Reason, before, CashierSessionSnapshot.Of(session), cancellationToken);

        return Result.Success(new AdministeredCashierSession(session, sessions.EntityTagOf(session)));
    }

    /// <remarks>
    /// The key set is the modes available at the branch today. When E09-F03-2 records payments in the
    /// session, the set must become the modes with a payment in this session in union with those — a mode
    /// deactivated or restricted mid-shift after taking money still has a line to count, and a counted line
    /// for it is not refused as unknown (INV-CSH-03: expected comes from the session's own payments).
    /// </remarks>
    private async Task<IReadOnlyDictionary<string, Money>> ExpectedByModeAsync(CashierSession session, CancellationToken cancellationToken)
    {
        var all = await modes.ListAsync(session.OrganisationId, cancellationToken);
        var expected = new Dictionary<string, Money>(StringComparer.Ordinal);
        foreach (var mode in all.Where(mode => mode.IsAvailableAt(session.BranchId)))
        {
            expected[mode.Code] = Money.Zero;
        }

        // The drawer holds the float whether or not cash is a mode anyone configured: a sheet that could
        // not count it would hide the float from the reconciliation.
        expected[PaymentModeCodes.Cash] = session.OpeningFloat;
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
