namespace Tailor360.Modules.Billing.Domain.Tax;

/// <summary>One component's rate on one tax code, as the version states it.</summary>
/// <param name="Kind">The component.</param>
/// <param name="RatePercent">The rate, as a percentage with at most three decimal places.</param>
public sealed record TaxRate(TaxComponentKind Kind, decimal RatePercent);

/// <summary>
/// The stored row behind a <see cref="TaxRate"/>: one component of one tax code.
/// </summary>
public sealed class TaxComponent
{
    private TaxComponent()
    {
        // The persistence layer materialises instances through this constructor.
    }

    private TaxComponent(Guid taxCodeId, TaxComponentKind kind, decimal ratePercent)
    {
        TaxCodeId = taxCodeId;
        Kind = kind;
        RatePercent = ratePercent;
    }

    /// <summary>The tax code this component belongs to.</summary>
    public Guid TaxCodeId { get; private set; }

    /// <summary>The component.</summary>
    public TaxComponentKind Kind { get; private set; }

    /// <summary>The rate, as a percentage.</summary>
    public decimal RatePercent { get; private set; }

    /// <summary>A component row for a code.</summary>
    /// <param name="taxCodeId">The code.</param>
    /// <param name="rate">The component and its rate.</param>
    /// <returns>The row.</returns>
    public static TaxComponent For(Guid taxCodeId, TaxRate rate)
    {
        ArgumentNullException.ThrowIfNull(rate);

        return new TaxComponent(taxCodeId, rate.Kind, rate.RatePercent);
    }

    /// <summary>The component as a value.</summary>
    public TaxRate ToRate() => new(Kind, RatePercent);
}
