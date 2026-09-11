using System.Globalization;
using Tailor360.Platform.Abstractions.Results;

namespace Tailor360.Modules.Orders.Domain.DisplayNumbers;

/// <summary>
/// The pieces every Orders display number shares.
/// </summary>
/// <remarks>
/// <para>
/// The separator, the branch-code rule, the sequence padding and the job-index padding are declared
/// once so that <see cref="OrderNumber"/>, <see cref="EstimateNumber"/> and
/// <see cref="GarmentJobNumber"/> cannot drift apart. A number printed on a job card, read back at a
/// counter and searched for in a report is the same number in all three places or it is useless.
/// </para>
/// <para>
/// The lengths are also what the persistence layer reads its column widths from, so the column and the
/// rule that fills it cannot disagree — the same arrangement <c>Customer.MaximumCustomerNumberLength</c>
/// uses.
/// </para>
/// </remarks>
public static class DisplayNumberFormat
{
    /// <summary>What separates the parts of a display number.</summary>
    public const char Separator = '-';

    /// <summary>
    /// The longest branch code a display number carries. Mirrors Identity's
    /// <c>Branch.MaximumCodeLength</c> (16), declared locally because ARCH-001 forbids a Domain project
    /// referencing anything but <c>Platform.Abstractions</c>.
    /// </summary>
    public const int MaximumBranchCodeLength = 16;

    /// <summary>
    /// The digits a sequence is padded to. <c>docs/architecture/conventions.md</c> section 3.2 prints
    /// six, and the sequence widens past <c>999999</c> rather than wrapping — a seventh digit is a very
    /// good year, not a collision.
    /// </summary>
    public const int SequenceDigits = 6;

    /// <summary>The digits a garment's position within its order is padded to (conventions.md 3.2).</summary>
    public const int JobIndexDigits = 2;

    /// <summary>The first sequence position. Sequences start at one and are never reused (INV-ORD-03).</summary>
    public const long MinimumSequence = 1L;

    /// <summary>The first garment position within an order.</summary>
    public const int MinimumJobIndex = 1;

    /// <summary>
    /// Whether a branch code may appear inside a display number.
    /// </summary>
    /// <remarks>
    /// Upper-case ASCII letters and digits only. Anything else — a space, a slash, an accented letter —
    /// would either break the parser that reads a number back into its parts or survive a round trip
    /// through a barcode, a filename or a URL as something different from what was printed.
    /// </remarks>
    /// <param name="branchCode">The candidate, as it will appear.</param>
    /// <returns>True when it may be used.</returns>
    public static bool IsWellFormedBranchCode(string? branchCode)
        => branchCode is { Length: >= 1 and <= MaximumBranchCodeLength }
           && branchCode.All(character => char.IsAsciiLetterUpper(character) || char.IsAsciiDigit(character));

    /// <summary>
    /// Folds a branch code into the form a display number carries.
    /// </summary>
    /// <remarks>
    /// Upper-cased rather than refused for case, because the code arrives from Identity's branch record
    /// and a display number is compared as text: <c>cbe</c> and <c>CBE</c> reaching the same sequence
    /// under two spellings would produce two numbers that read as one.
    /// </remarks>
    /// <param name="branchCode">The branch code, as held.</param>
    /// <returns>The folded code, or the reason it is not one.</returns>
    public static Result<string> NormaliseBranchCode(string? branchCode)
    {
        var trimmed = branchCode?.Trim();

        if (string.IsNullOrEmpty(trimmed))
        {
            return Result.Failure<string>(OrdersErrors.Required("branchCode"));
        }

        var folded = trimmed.ToUpperInvariant();

        return IsWellFormedBranchCode(folded)
            ? Result.Success(folded)
            : Result.Failure<string>(OrdersErrors.BranchCodeNotWellFormed);
    }

    /// <summary>Renders a sequence as it is printed: invariant digits, zero-padded, never truncated.</summary>
    /// <param name="sequence">The position within the branch and financial year.</param>
    /// <returns>The padded digits.</returns>
    public static string FormatSequence(long sequence)
        => sequence.ToString(CultureInfo.InvariantCulture).PadLeft(SequenceDigits, '0');

    /// <summary>Renders a garment's position as it is printed on the job card.</summary>
    /// <param name="jobIndex">The one-based position within the order.</param>
    /// <returns>The padded digits.</returns>
    public static string FormatJobIndex(int jobIndex)
        => jobIndex.ToString(CultureInfo.InvariantCulture).PadLeft(JobIndexDigits, '0');
}
