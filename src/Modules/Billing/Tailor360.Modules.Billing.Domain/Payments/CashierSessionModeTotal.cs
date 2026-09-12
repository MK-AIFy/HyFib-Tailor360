namespace Tailor360.Modules.Billing.Domain.Payments;

/// <summary>What one payment mode should have taken in the session against what was counted for it.</summary>
public sealed class CashierSessionModeTotal
{
    private CashierSessionModeTotal()
    {
        // The persistence layer materialises instances through this constructor.
    }

    private CashierSessionModeTotal(Guid sessionId, string modeCode, decimal expected, decimal counted)
    {
        SessionId = sessionId;
        ModeCode = modeCode;
        Expected = expected;
        Counted = counted;
        Variance = counted - expected;
    }

    /// <summary>The session.</summary>
    public Guid SessionId { get; private set; }

    /// <summary>The payment mode's code.</summary>
    public string ModeCode { get; private set; } = string.Empty;

    /// <summary>The sum of the session's records in this mode — for cash, the float included.</summary>
    public decimal Expected { get; private set; }

    /// <summary>What the cashier counted or read off the terminal.</summary>
    public decimal Counted { get; private set; }

    /// <summary>Counted minus expected: positive is over, negative is short.</summary>
    public decimal Variance { get; private set; }

    /// <summary>A line.</summary>
    public static CashierSessionModeTotal Of(Guid sessionId, string modeCode, decimal expected, decimal counted) => new(sessionId, modeCode, expected, counted);
}
