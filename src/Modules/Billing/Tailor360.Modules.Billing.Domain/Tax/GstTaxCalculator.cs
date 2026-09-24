namespace Tailor360.Modules.Billing.Domain.Tax;

/// <summary>Whether the supplier and place of supply are in the same state.</summary>
public enum GstSupplyKind
{
    /// <summary>CGST and SGST apply.</summary>
    IntraState,

    /// <summary>IGST applies.</summary>
    InterState,
}

/// <summary>
/// The tax components of one invoice line. All amounts are final document amounts in INR,
/// rounded to paise; the total is the sum of those components.
/// </summary>
public sealed record GstTaxLine(
    decimal TaxableAmount,
    decimal Cgst,
    decimal Sgst,
    decimal Igst,
    decimal Cess,
    GstSupplyKind SupplyKind)
{
    /// <summary>The tax charged on the line.</summary>
    public decimal TotalTax => Cgst + Sgst + Igst + Cess;

    /// <summary>The amount payable for the line.</summary>
    public decimal Total => TaxableAmount + TotalTax;
}

/// <summary>
/// A document summary calculated only by adding already-rounded line components. The caller must
/// retain the individual lines and the tax-configuration version alongside this summary.
/// </summary>
public sealed record GstBillTotals(
    decimal TaxableAmount,
    decimal Cgst,
    decimal Sgst,
    decimal Igst,
    decimal Cess)
{
    /// <summary>Total tax on the bill.</summary>
    public decimal TotalTax => Cgst + Sgst + Igst + Cess;

    /// <summary>Total before any separately configured document round-off.</summary>
    public decimal Total => TaxableAmount + TotalTax;

    /// <summary>Add line components without re-rounding their sum.</summary>
    public static GstBillTotals FromLines(IEnumerable<GstTaxLine> lines)
    {
        ArgumentNullException.ThrowIfNull(lines);

        decimal taxableAmount = 0m;
        decimal cgst = 0m;
        decimal sgst = 0m;
        decimal igst = 0m;
        decimal cess = 0m;
        var hasLine = false;
        GstSupplyKind? supplyKind = null;

        foreach (var line in lines)
        {
            ArgumentNullException.ThrowIfNull(line);
            if (supplyKind is not null && supplyKind != line.SupplyKind)
            {
                throw new ArgumentException("A bill cannot mix intra-state and inter-state tax schemes.", nameof(lines));
            }

            supplyKind = line.SupplyKind;
            hasLine = true;
            taxableAmount += line.TaxableAmount;
            cgst += line.Cgst;
            sgst += line.Sgst;
            igst += line.Igst;
            cess += line.Cess;
        }

        if (!hasLine)
        {
            throw new ArgumentException("A bill must contain at least one line.", nameof(lines));
        }

        return new GstBillTotals(taxableAmount, cgst, sgst, igst, cess);
    }
}

/// <summary>
/// Calculates the GST components for an already-priced, tax-exclusive invoice line. Pricing,
/// discount allocation, tax-code selection and place-of-supply determination happen before this
/// calculation; they must be snapshotted with the bill so a posted bill never changes later.
/// </summary>
public static class GstTaxCalculator
{
    /// <summary>Calculate the rounded components of one taxable line.</summary>
    /// <exception cref="ArgumentOutOfRangeException">An amount or rate is outside its valid range.</exception>
    /// <exception cref="ArgumentException">The taxable amount has sub-paise precision.</exception>
    public static GstTaxLine Calculate(
        decimal taxableAmount,
        decimal gstRatePercent,
        decimal cessRatePercent,
        string supplierStateCode,
        string placeOfSupplyStateCode)
    {
        ValidateStateCode(supplierStateCode, nameof(supplierStateCode));
        ValidateStateCode(placeOfSupplyStateCode, nameof(placeOfSupplyStateCode));

        ArgumentOutOfRangeException.ThrowIfLessThan(taxableAmount, 0m);

        if (decimal.Round(taxableAmount, 2) != taxableAmount)
        {
            throw new ArgumentException("Taxable amount must be rounded to paise.", nameof(taxableAmount));
        }

        ValidateRate(gstRatePercent, nameof(gstRatePercent));
        ValidateRate(cessRatePercent, nameof(cessRatePercent));

        var supplyKind = string.Equals(supplierStateCode, placeOfSupplyStateCode, StringComparison.Ordinal)
            ? GstSupplyKind.IntraState
            : GstSupplyKind.InterState;
        var cess = RoundToPaise(taxableAmount * cessRatePercent / 100m);

        return supplyKind switch
        {
            GstSupplyKind.IntraState => new GstTaxLine(
                taxableAmount,
                RoundToPaise(taxableAmount * gstRatePercent / 200m),
                RoundToPaise(taxableAmount * gstRatePercent / 200m),
                0m,
                cess,
                supplyKind),
            GstSupplyKind.InterState => new GstTaxLine(
                taxableAmount,
                0m,
                0m,
                RoundToPaise(taxableAmount * gstRatePercent / 100m),
                cess,
                supplyKind),
            _ => throw new InvalidOperationException("The derived GST supply kind is unsupported."),
        };
    }

    private static void ValidateStateCode(string value, string parameterName)
    {
        if (value is null || value.Length != 2 || !char.IsAsciiDigit(value[0]) || !char.IsAsciiDigit(value[1]))
        {
            throw new ArgumentException("State code must contain two ASCII digits.", parameterName);
        }
    }

    private static void ValidateRate(decimal rate, string parameterName)
    {
        if (rate < 0m || rate > 100m || decimal.Round(rate, 3) != rate)
        {
            throw new ArgumentOutOfRangeException(parameterName, "Rate must be between 0 and 100 with at most three decimal places.");
        }
    }

    private static decimal RoundToPaise(decimal value)
        => decimal.Round(value, 2, MidpointRounding.AwayFromZero);
}
