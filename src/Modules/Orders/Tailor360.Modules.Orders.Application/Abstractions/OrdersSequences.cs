using System.Globalization;
using Tailor360.Modules.Orders.Domain.DisplayNumbers;

namespace Tailor360.Modules.Orders.Application.Abstractions;

/// <summary>
/// The two display-number sequences Orders allocates from, and the scope each is counted within.
/// </summary>
/// <remarks>
/// <para>
/// <strong>INV-ORD-03 is a per-branch, per-financial-year rule and <c>ISequenceAllocator</c> takes one scope
/// string</strong>, so both halves have to be encoded into that one value or the invariant cannot be expressed.
/// An order taken at Coimbatore in 2026-27 and one taken at Chennai in the same year must count separately, and
/// so must the same branch either side of the first of April
/// (<c>docs/architecture/conventions.md</c> sections 2.2 and 3.2).
/// </para>
/// <para>
/// <strong>Why a small static class rather than a constant on a handler.</strong> Customers keeps
/// <c>CustomerHandler.CustomerNumberSequence</c> beside the handler that allocates it. Orders has no handler yet
/// — the commands land with the endpoints — and two stores need the keys now, so they live here, in the layer
/// that owns the ports, and the handlers can go on using them when they arrive.
/// </para>
/// <para>
/// <strong>The separator is a free choice and is recorded as one.</strong> No document specifies how a sequence
/// scope is composed; <c>platform.sequences.scope</c> is <c>varchar(64)</c> and the longest value this produces
/// is sixteen characters of branch code, one separator and a four-character financial-year token — twenty-one,
/// well inside it. The hyphen matches how the same two parts are joined inside the display number itself
/// (<see cref="DisplayNumberFormat.Separator"/>), so a scope read out of the sequences table is recognisable
/// beside the numbers it produced.
/// </para>
/// </remarks>
public static class OrdersSequences
{
    /// <summary>
    /// The sequence an order number's position is taken from: <c>O-&lt;branch&gt;-&lt;year&gt;-000001</c>.
    /// </summary>
    public const string OrderNumber = "order";

    /// <summary>
    /// The sequence an estimate number's position is taken from. Separate from
    /// <see cref="OrderNumber"/> on purpose: INV-ORD-04 gives an estimate its own numbering, so converting an
    /// estimate into an order does not consume an order number and an unconverted estimate does not leave a gap
    /// in one.
    /// </summary>
    public const string EstimateNumber = "estimate";

    /// <summary>
    /// Composes the scope one of these sequences is counted within.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The branch code is folded to upper case, which is not cosmetic. <c>DisplayNumberFormat.NormaliseBranchCode</c>
    /// upper-cases before it composes a number, so <c>cbe</c> and <c>CBE</c> print identically; if they counted
    /// from two different scopes they would print the <em>same number twice</em>, which is precisely the reuse
    /// INV-ORD-03 forbids. Folding here keeps the scope and the printed number agreeing about what a branch is.
    /// </para>
    /// </remarks>
    /// <param name="branchCode">The branch's short code, as Identity holds it.</param>
    /// <param name="financialYear">The financial year the number belongs to, evaluated in the branch timezone.</param>
    /// <returns>The scope, <c>&lt;BRANCH&gt;-&lt;year token&gt;</c>.</returns>
    /// <exception cref="ArgumentException"><paramref name="branchCode"/> is null, empty or blank.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="financialYear"/> is null.</exception>
    public static string ScopeOf(string branchCode, FinancialYear financialYear)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(branchCode);
        ArgumentNullException.ThrowIfNull(financialYear);

        return string.Create(
            CultureInfo.InvariantCulture,
            $"{branchCode.Trim().ToUpperInvariant()}{DisplayNumberFormat.Separator}{financialYear.Token}");
    }
}
