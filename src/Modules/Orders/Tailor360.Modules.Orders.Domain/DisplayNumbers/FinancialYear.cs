using System.Globalization;
using System.Text.RegularExpressions;
using Tailor360.Platform.Abstractions.Results;

namespace Tailor360.Modules.Orders.Domain.DisplayNumbers;

/// <summary>
/// The <c>&lt;FY&gt;</c> token inside an Orders display number.
/// </summary>
/// <remarks>
/// <para>
/// The financial year runs 1 April to 31 March (<c>docs/architecture/conventions.md</c> section 2.3)
/// and is part of every document sequence key. The token itself is the two-digit year it opens in
/// followed by the two-digit year it closes in — <c>2627</c> for 2026-27 — which the same section
/// records as <strong>proposed, to be confirmed</strong> with issue #42 and the accountant (COD-03).
/// This type implements exactly that proposal and nothing more; if COD-03 settles on another shape,
/// this is the one file that changes.
/// </para>
/// <para>
/// <strong>The two-digit token is why the range is closed.</strong> <c>0001</c> could mean 2000-01 or
/// 1900-01, and a display number that could mean either is not a number anybody can quote down a
/// telephone. <see cref="Create"/> therefore refuses a year outside
/// <see cref="MinimumStartYear"/>..<see cref="MaximumStartYear"/> rather than guessing a century.
/// </para>
/// <para>
/// <strong>This type never touches a clock</strong> (ARCH-014). <see cref="Containing"/> takes the
/// branch-local date, because a financial year is a business date and is evaluated in the branch
/// timezone (conventions.md section 2.2); resolving that timezone belongs to the caller.
/// </para>
/// </remarks>
public sealed partial record FinancialYear
{
    /// <summary>The characters the token occupies inside a display number.</summary>
    public const int TokenLength = 4;

    /// <summary>
    /// The earliest financial year the two-digit token can address without ambiguity.
    /// </summary>
    public const int MinimumStartYear = 2000;

    /// <summary>
    /// The latest. A year opening in 2099 would close in 2100 and its token would read <c>9900</c>,
    /// which is indistinguishable from a transposition, so the range stops one year short.
    /// </summary>
    public const int MaximumStartYear = 2098;

    /// <summary>
    /// The month the financial year opens in: April (conventions.md section 2.3).
    /// </summary>
    /// <remarks>
    /// The same boundary Platform's <c>IndiaTimeZone.FinancialYearStarting</c> applies, and stated here
    /// rather than borrowed from it deliberately: every static member of that class shares a type
    /// initialiser with <c>IndiaTimeZone.Instance</c>, which resolves <c>Asia/Kolkata</c> from the
    /// timezone database and throws where the ICU data is absent — the failure
    /// <c>Directory.Build.props</c> keeps <c>InvariantGlobalization</c> false to avoid. Composing a
    /// display number is arithmetic over a date the caller already resolved, and it should not be able
    /// to fail for want of a timezone. The two constants must agree, and a unit test asserting that they
    /// do belongs with this layer's tests.
    /// </remarks>
    public const int StartMonth = 4;

    private FinancialYear(string token) => Token = token;

    /// <summary>The token as it appears inside a display number, for example <c>2627</c>.</summary>
    public string Token { get; }

    /// <summary>
    /// Reads a token, or says why it is not one.
    /// </summary>
    /// <remarks>
    /// Refuses anything but four digits whose second pair is the first pair plus one, so <c>2726</c>
    /// — the same year typed backwards — is caught at the boundary rather than printed on a document
    /// and quoted back by a customer a year later.
    /// </remarks>
    /// <param name="token">The candidate token.</param>
    /// <returns>The financial year, or the reason it is not one.</returns>
    public static Result<FinancialYear> Create(string? token)
    {
        var trimmed = token?.Trim();

        if (string.IsNullOrEmpty(trimmed))
        {
            return Result.Failure<FinancialYear>(OrdersErrors.Required("financialYear"));
        }

        if (!Pattern().IsMatch(trimmed))
        {
            return Result.Failure<FinancialYear>(OrdersErrors.FinancialYearNotWellFormed);
        }

        var opens = int.Parse(trimmed.AsSpan(0, 2), CultureInfo.InvariantCulture);
        var closes = int.Parse(trimmed.AsSpan(2, 2), CultureInfo.InvariantCulture);

        // The year it closes in is the year it opens in plus one, counted in the two digits the token
        // carries, so 9900 would have to mean 2099-2100 and is refused by the range check below.
        if (closes != (opens + 1) % 100)
        {
            return Result.Failure<FinancialYear>(OrdersErrors.FinancialYearNotWellFormed);
        }

        return IsAddressable(2000 + opens)
            ? Result.Success(new FinancialYear(trimmed))
            : Result.Failure<FinancialYear>(OrdersErrors.FinancialYearNotWellFormed);
    }

    /// <summary>Builds the token for the financial year opening in a given year.</summary>
    /// <param name="startYear">The calendar year the financial year opens in, such as 2026 for 2026-27.</param>
    /// <returns>The financial year, or the reason that year has no token.</returns>
    public static Result<FinancialYear> FromStartYear(int startYear)
    {
        if (!IsAddressable(startYear))
        {
            return Result.Failure<FinancialYear>(OrdersErrors.FinancialYearNotWellFormed);
        }

        var opens = (startYear % 100).ToString(CultureInfo.InvariantCulture).PadLeft(2, '0');
        var closes = ((startYear + 1) % 100).ToString(CultureInfo.InvariantCulture).PadLeft(2, '0');

        return Result.Success(new FinancialYear(opens + closes));
    }

    /// <summary>
    /// The financial year containing a branch-local date.
    /// </summary>
    /// <remarks>
    /// The caller resolves the branch timezone and hands the date it produced (conventions.md
    /// section 2.2). A confirmation taken at half past eleven at night on 31 March belongs to the year
    /// that is closing, and taken half an hour later to the one that is opening — which is only true
    /// of the branch's own clock, never of UTC.
    /// </remarks>
    /// <param name="branchLocalDate">The date, already evaluated in the branch timezone.</param>
    /// <returns>The financial year, or the reason that date has no token.</returns>
    public static Result<FinancialYear> Containing(DateOnly branchLocalDate)
        => FromStartYear(branchLocalDate.Month >= StartMonth
            ? branchLocalDate.Year
            : branchLocalDate.Year - 1);

    /// <summary>Whether a string is a token this system issues.</summary>
    /// <param name="token">The candidate token.</param>
    /// <returns>True when it is.</returns>
    public static bool IsWellFormed(string? token) => Create(token).IsSuccess;

    /// <inheritdoc />
    public override string ToString() => Token;

    private static bool IsAddressable(int startYear)
        => startYear is >= MinimumStartYear and <= MaximumStartYear;

    /// <summary>Four digits and nothing else; what they mean is checked by <see cref="Create"/>.</summary>
    [GeneratedRegex("^[0-9]{4}$", RegexOptions.CultureInvariant)]
    private static partial Regex Pattern();
}
