namespace Tailor360.Platform.Abstractions.Time;

/// <summary>
/// The default branch timezone and the financial-year rules that follow from it.
/// Branches carry their own timezone (#25); this is the default applied when one is not set.
/// </summary>
public static class IndiaTimeZone
{
    /// <summary>The IANA identifier for Indian Standard Time.</summary>
    public const string Id = "Asia/Kolkata";

    private static readonly Lazy<TimeZoneInfo> Resolved = new(() => TimeZoneInfo.FindSystemTimeZoneById(Id));

    /// <summary>Indian Standard Time, resolved once, on first use.</summary>
    /// <remarks>
    /// <para>
    /// Resolved lazily rather than in a field initialiser so that the timezone lookup is not folded into this
    /// type's initialiser. A static field initialiser runs on the first touch of <em>any</em> member, which
    /// would make <see cref="FinancialYearStarting"/> and <see cref="FinancialYearLabel"/> — pure arithmetic
    /// that needs no timezone at all — throw on a host whose tz database cannot resolve
    /// <see cref="Id"/>. Composing a display number carries a financial year, so that coupling would let an
    /// order number fail to format for want of a timezone.
    /// </para>
    /// </remarks>
    public static TimeZoneInfo Instance => Resolved.Value;

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
