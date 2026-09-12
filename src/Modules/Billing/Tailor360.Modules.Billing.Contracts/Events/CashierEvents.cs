using Tailor360.Platform.Abstractions.Events;

namespace Tailor360.Modules.Billing.Contracts.Events;

/// <summary>
/// A cashier session was counted and closed: what it should have held, what was counted, and the
/// difference. Identifiers and figures only; the denomination sheet and the reason stay on the record.
/// </summary>
/// <param name="EventId">Identity of this occurrence.</param>
/// <param name="OccurredAt">When.</param>
/// <param name="AggregateId">The session.</param>
/// <param name="OrganisationId">The organisation.</param>
/// <param name="BranchId">The branch whose drawer it was.</param>
/// <param name="CashierId">The cashier accountable for it.</param>
/// <param name="OpenedAt">When it opened.</param>
/// <param name="ClosedAt">When it closed.</param>
/// <param name="ExpectedTotal">Over every mode, what the session should have held.</param>
/// <param name="CountedTotal">Over every mode, what was counted.</param>
/// <param name="Variance">Counted minus expected.</param>
/// <param name="Currency">The currency of the three figures.</param>
public sealed record CashierSessionClosed(
    Guid EventId,
    DateTimeOffset OccurredAt,
    Guid AggregateId,
    Guid OrganisationId,
    Guid BranchId,
    Guid CashierId,
    DateTimeOffset OpenedAt,
    DateTimeOffset ClosedAt,
    decimal ExpectedTotal,
    decimal CountedTotal,
    decimal Variance,
    string Currency)
    : IntegrationEvent(EventId, OccurredAt, AggregateId)
{
    /// <summary>The wire name. Subscribe by this constant rather than by a literal.</summary>
    public const string Type = "billing.cashier-session-closed.v1";

    /// <inheritdoc />
    public override string EventType => Type;
}
