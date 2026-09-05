namespace Tailor360.Platform.Abstractions.Time;

/// <summary>
/// The default branch timezone and the financial-year rules that follow from it.
/// Branches carry their own timezone (#25); this is the default applied when one is not set.
/// </summary>
public static class IndiaTimeZone
{
    /// <summary>The IANA identifier for Indian Standard Time.</summary>
    public const string Id = "Asia/Kolkata";

    /// <summary>Indian Standard Time, resolved once.</summary>
    public static TimeZoneInfo Instance { get; } = TimeZoneInfo.FindSystemTimeZoneById(Id);

    /// <summary>
    /// The Indian financial year containing <paramref name="date"/>, which runs 1 April to 31 March.
    /// A date of 2026-09-04 returns 2026, meaning the year 2026-27.
    /// </summary>
    public static int FinancialYearStarting(DateOnly date) => date.Month >= 4 ? date.Year : date.Year - 1;

    /// <summary>The label of the financial year containing <paramref name="date"/>, such as <c>2026-27</c>.</summary>
    public static string FinancialYearLabel(DateOnly date)
    {
        var start = FinancialYearStarting(date);
        return $"{start}-{(start + 1) % 100:D2}";
    }
}
