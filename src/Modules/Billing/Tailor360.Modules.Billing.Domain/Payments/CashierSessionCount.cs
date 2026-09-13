namespace Tailor360.Modules.Billing.Domain.Payments;

/// <summary>One line of the denomination count sheet: how many of one note or coin were in the drawer.</summary>
public sealed class CashierSessionCount
{
    private CashierSessionCount()
    {
        // The persistence layer materialises instances through this constructor.
    }

    private CashierSessionCount(Guid sessionId, decimal denomination, int quantity)
    {
        SessionId = sessionId;
        Denomination = denomination;
        Quantity = quantity;
    }

    /// <summary>The session.</summary>
    public Guid SessionId { get; private set; }

    /// <summary>The note or coin, in rupees.</summary>
    public decimal Denomination { get; private set; }

    /// <summary>How many.</summary>
    public int Quantity { get; private set; }

    /// <summary>What the line is worth.</summary>
    public decimal Value => Denomination * Quantity;

    /// <summary>A line.</summary>
    public static CashierSessionCount Of(Guid sessionId, decimal denomination, int quantity) => new(sessionId, denomination, quantity);
}
