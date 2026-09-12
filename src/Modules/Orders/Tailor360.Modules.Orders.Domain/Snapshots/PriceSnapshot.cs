using Tailor360.Platform.Abstractions.Money;
using Tailor360.Platform.Abstractions.Results;

namespace Tailor360.Modules.Orders.Domain.Snapshots;

/// <summary>
/// The priced result copied onto an order, a garment job or an estimate.
/// </summary>
/// <remarks>
/// <para>
/// <strong>INV-ORD-02 lives here.</strong> Exactly one catalogue version, one price-list version and
/// one tax configuration version are recorded, so every figure on the document can be recomputed from
/// its own snapshot. A figure that cannot be is a defect
/// (<c>docs/architecture/conventions.md</c> section 1.2), and a missing version is what would make it
/// one — which is why all three are refused when absent rather than defaulted.
/// </para>
/// <para>
/// <strong>INV-ORD-07: these totals are for display and printing.</strong> The authoritative money
/// position is Billing's <c>IFinancialTotalsQuery</c>. This module performs no money arithmetic at all
/// beyond adding the four tax components it was handed: it stores what Billing returned. A figure
/// computed twice in two modules is a figure that will eventually disagree with itself, and the copy on
/// the order is the one that would be wrong.
/// </para>
/// <para>
/// <strong>Immutable.</strong> There is no mutator. A revision or a re-price appends a new snapshot on a
/// new revision row rather than editing this one, so the sequence of rows is the whole history of what
/// the order was priced at and when.
/// </para>
/// </remarks>
public sealed record PriceSnapshot
{
    /// <summary>
    /// The constructor the persistence layer materialises instances through.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Private, like the one below it, so it is no more a way in than that one is: it takes nothing, sets
    /// nothing, and <see cref="Create"/> remains the only route a caller has. <c>with</c> is still refused,
    /// because the properties are get-only rather than <c>init</c>.
    /// </para>
    /// <para>
    /// <strong>It exists because Entity Framework cannot bind a complex property to a constructor
    /// parameter.</strong> Each <see cref="Money"/> here is two columns — an amount and a currency — and a
    /// two-column value object is a complex type; "only mapped properties can be bound to constructor
    /// parameters", so the thirteen-parameter constructor below binds nine of its parameters to nothing and
    /// Entity Framework refuses the type outright. With this one present it materialises the row and writes each
    /// property through its backing field, which is the shape every other persisted type in this module already
    /// has (<c>OrderDraft</c>, <c>Estimate</c>, <c>Order</c>, <c>GarmentJob</c>).
    /// </para>
    /// </remarks>
    private PriceSnapshot()
    {
        // The persistence layer materialises instances through this constructor.
    }

    /// <summary>
    /// The only way to build one, and it is private so that <see cref="Create"/> is the only way in.
    /// </summary>
    /// <remarks>
    /// Not a positional record, and the properties are get-only rather than <c>init</c>, so <c>with</c>
    /// cannot move a total away from the versions it was calculated under.
    /// </remarks>
    private PriceSnapshot(
        Guid catalogVersionId,
        Guid priceListVersionId,
        Guid taxConfigurationVersionId,
        Money subtotal,
        Money discountTotal,
        Money taxableValue,
        Money centralTax,
        Money stateTax,
        Money integratedTax,
        Money cess,
        Money roundOff,
        Money grandTotal,
        DateTimeOffset calculatedAt)
    {
        CatalogVersionId = catalogVersionId;
        PriceListVersionId = priceListVersionId;
        TaxConfigurationVersionId = taxConfigurationVersionId;
        Subtotal = subtotal;
        DiscountTotal = discountTotal;
        TaxableValue = taxableValue;
        CentralTax = centralTax;
        StateTax = stateTax;
        IntegratedTax = integratedTax;
        Cess = cess;
        RoundOff = roundOff;
        GrandTotal = grandTotal;
        CalculatedAt = calculatedAt;
    }

    /// <summary>The published catalogue version the priced lines were resolved against (INV-ORD-02).</summary>
    public Guid CatalogVersionId { get; }

    /// <summary>The price-list version the rates came from (INV-ORD-02).</summary>
    public Guid PriceListVersionId { get; }

    /// <summary>The tax configuration version in force at the calculation (INV-ORD-02).</summary>
    public Guid TaxConfigurationVersionId { get; }

    /// <summary>Line amounts before discount.</summary>
    public Money Subtotal { get; }

    /// <summary>The discount given, as a positive amount.</summary>
    public Money DiscountTotal { get; }

    /// <summary>What tax was computed on.</summary>
    public Money TaxableValue { get; }

    /// <summary>CGST. Zero on an inter-state supply.</summary>
    public Money CentralTax { get; }

    /// <summary>SGST. Zero on an inter-state supply.</summary>
    public Money StateTax { get; }

    /// <summary>IGST. Zero on an intra-state supply.</summary>
    public Money IntegratedTax { get; }

    /// <summary>Cess, where the tax code carries one.</summary>
    public Money Cess { get; }

    /// <summary>
    /// The document round-off, shown and never absorbed. May be negative.
    /// </summary>
    /// <remarks>
    /// conventions.md section 1.2: the total is rounded to the nearest rupee under the configured rule
    /// and the difference is recorded explicitly. Rounding a total down produces a negative round-off,
    /// which is why it is the one amount exempt from the negative check.
    /// </remarks>
    public Money RoundOff { get; }

    /// <summary>What the document asks for.</summary>
    public Money GrandTotal { get; }

    /// <summary>When the calculation was made, in UTC.</summary>
    public DateTimeOffset CalculatedAt { get; }

    /// <summary>
    /// The sum of the four tax components.
    /// </summary>
    /// <remarks>
    /// Derived and never stored, so it cannot disagree with its parts. A stored total beside the
    /// components it was made of is two facts that drift apart the first time one of them is corrected.
    /// </remarks>
    public Money TaxTotal => CentralTax + StateTax + IntegratedTax + Cess;

    /// <summary>True when the supply was taxed as inter-state.</summary>
    public bool IsIntegratedSupply => !IntegratedTax.IsZero;

    /// <summary>
    /// Takes the copy of what Billing calculated.
    /// </summary>
    /// <remarks>
    /// Refuses a missing configuration version (INV-ORD-02), an amount carrying no currency, mixed
    /// currencies, a negative amount other than <see cref="RoundOff"/>, and both tax schemes on one
    /// document (INV-INV-04). It recomputes nothing: every amount is stored exactly as Billing returned
    /// it (INV-ORD-07).
    /// </remarks>
    /// <param name="catalogVersionId">The published catalogue version.</param>
    /// <param name="priceListVersionId">The price-list version.</param>
    /// <param name="taxConfigurationVersionId">The tax configuration version.</param>
    /// <param name="subtotal">Line amounts before discount.</param>
    /// <param name="discountTotal">The discount given, as a positive amount.</param>
    /// <param name="taxableValue">What tax was computed on.</param>
    /// <param name="centralTax">CGST.</param>
    /// <param name="stateTax">SGST.</param>
    /// <param name="integratedTax">IGST.</param>
    /// <param name="cess">Cess, where the tax code carries one.</param>
    /// <param name="roundOff">The document round-off, which may be negative.</param>
    /// <param name="grandTotal">What the document asks for.</param>
    /// <param name="calculatedAt">When the calculation was made, from <c>IClock</c>.</param>
    /// <returns>The snapshot, or the first failure found.</returns>
    public static Result<PriceSnapshot> Create(
        Guid catalogVersionId,
        Guid priceListVersionId,
        Guid taxConfigurationVersionId,
        Money subtotal,
        Money discountTotal,
        Money taxableValue,
        Money centralTax,
        Money stateTax,
        Money integratedTax,
        Money cess,
        Money roundOff,
        Money grandTotal,
        DateTimeOffset calculatedAt)
    {
        if (catalogVersionId == Guid.Empty)
        {
            return Result.Failure<PriceSnapshot>(
                OrdersErrors.ConfigurationVersionMissing("catalogVersionId"));
        }

        if (priceListVersionId == Guid.Empty)
        {
            return Result.Failure<PriceSnapshot>(
                OrdersErrors.ConfigurationVersionMissing("priceListVersionId"));
        }

        if (taxConfigurationVersionId == Guid.Empty)
        {
            return Result.Failure<PriceSnapshot>(
                OrdersErrors.ConfigurationVersionMissing("taxConfigurationVersionId"));
        }

        // A default Money struct never went through the constructor, so it carries no currency code at
        // all. Left alone it would reach TaxTotal and throw there, deep inside a job card being
        // rendered, rather than being refused at the boundary where the caller can still be told.
        if (string.IsNullOrEmpty(subtotal.Currency))
        {
            return Result.Failure<PriceSnapshot>(OrdersErrors.Required("currency"));
        }

        Money[] amounts =
            [subtotal, discountTotal, taxableValue, centralTax, stateTax, integratedTax, cess, roundOff, grandTotal];

        foreach (var amount in amounts)
        {
            if (!string.Equals(amount.Currency, subtotal.Currency, StringComparison.Ordinal))
            {
                return Result.Failure<PriceSnapshot>(OrdersErrors.CurrencyMismatch);
            }
        }

        // RoundOff is deliberately absent: it is the one amount that may be negative.
        var negative = FirstNegative(
            ("subtotal", subtotal),
            ("discountTotal", discountTotal),
            ("taxableValue", taxableValue),
            ("centralTax", centralTax),
            ("stateTax", stateTax),
            ("integratedTax", integratedTax),
            ("cess", cess),
            ("grandTotal", grandTotal));

        if (negative is not null)
        {
            return Result.Failure<PriceSnapshot>(OrdersErrors.AmountNegative(negative));
        }

        // INV-INV-04. Place of supply decides the scheme, so a document carrying CGST or SGST beside
        // IGST is one that claims the supply was both within the state and between states.
        if (!integratedTax.IsZero && !(centralTax.IsZero && stateTax.IsZero))
        {
            return Result.Failure<PriceSnapshot>(OrdersErrors.BothTaxSchemesPresent);
        }

        return Result.Success(new PriceSnapshot(
            catalogVersionId,
            priceListVersionId,
            taxConfigurationVersionId,
            subtotal,
            discountTotal,
            taxableValue,
            centralTax,
            stateTax,
            integratedTax,
            cess,
            roundOff,
            grandTotal,
            calculatedAt));
    }

    private static string? FirstNegative(params (string Field, Money Amount)[] amounts)
    {
        foreach (var (field, amount) in amounts)
        {
            if (amount.IsNegative)
            {
                return field;
            }
        }

        return null;
    }
}
