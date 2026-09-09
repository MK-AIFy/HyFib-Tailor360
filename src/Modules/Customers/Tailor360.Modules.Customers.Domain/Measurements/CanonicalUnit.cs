namespace Tailor360.Modules.Customers.Domain.Measurements;

/// <summary>
/// The unit a captured value is <em>stored</em> in. Never shown to anybody.
/// </summary>
/// <remarks>
/// <para>
/// Every length is stored in millimetres and rendered in the display unit the reader chose
/// (<c>docs/prd/measurement-templates.md</c> section 2). Storing the display unit alongside the number is the
/// mistake this enum exists to prevent: two records in different units cannot be compared, and a template that
/// changed its default would silently reinterpret everything captured before it.
/// </para>
/// <para>
/// <see cref="None"/> is a choice field — <c>waist_finish</c>, <c>age_band</c> — which stores an option code and
/// has no unit at all. Open decision <strong>OD-MEA-03</strong> asks whether choice fields belong in a template or
/// in the design options; until it is settled the seed keeps them here, which is what section 10 instructs.
/// </para>
/// </remarks>
public enum CanonicalUnit
{
    /// <summary>A length, stored in millimetres to two decimal places.</summary>
    Millimetre = 0,

    /// <summary>A whole number of things, such as the kali panels of a lehenga.</summary>
    Count = 1,

    /// <summary>No unit: a choice field storing an option code.</summary>
    None = 2,
}
