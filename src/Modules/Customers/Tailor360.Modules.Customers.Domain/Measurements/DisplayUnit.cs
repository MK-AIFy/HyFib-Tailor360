namespace Tailor360.Modules.Customers.Domain.Measurements;

/// <summary>
/// The unit a value is shown and entered in.
/// </summary>
/// <remarks>
/// Millimetres are deliberately absent. They are the storage unit and the unit this repository's documents state
/// ranges in; a member of staff never sees one, so offering it as a display unit would only invite somebody to
/// type a millimetre value into a field labelled inches.
/// </remarks>
public enum DisplayUnit
{
    /// <summary>Inches, entered as a whole number plus a fraction.</summary>
    Inch = 0,

    /// <summary>Centimetres, entered as a decimal.</summary>
    Centimetre = 1,

    /// <summary>A count, shown as the whole number it is.</summary>
    Count = 2,
}
