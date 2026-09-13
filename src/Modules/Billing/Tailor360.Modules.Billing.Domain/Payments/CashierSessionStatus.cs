namespace Tailor360.Modules.Billing.Domain.Payments;

/// <summary>Where a cashier session is in its one-way life.</summary>
public enum CashierSessionStatus
{
    /// <summary>Opened with a float; payments may be recorded in it.</summary>
    Open = 0,

    /// <summary>Counted and closed; immutable from here.</summary>
    Closed = 1,
}
