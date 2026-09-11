using System.Globalization;
using System.Text.RegularExpressions;
using Tailor360.Platform.Abstractions.Results;

namespace Tailor360.Modules.Orders.Domain.DisplayNumbers;

/// <summary>
/// The number an estimate is known by: <c>E-&lt;branch&gt;-&lt;FY&gt;-000001</c>.
/// </summary>
/// <remarks>
/// <para>
/// Allocated when an estimate is issued, from a series that is entirely separate from the invoice
/// series. INV-ORD-04 says an estimate is never a tax document, is never posted and
/// <strong>never consumes an invoice number</strong>; a separate namespace letter and a separate
/// sequence are half of how that is kept true, and the absence of any posting path on the estimate is
/// the other half.
/// </para>
/// <para>
/// Deliberately a distinct type from <see cref="OrderNumber"/> even though the two share a shape, so
/// that the compiler refuses to put an estimate number where an order number belongs. Two numbers that
/// read alike and mean different things are exactly the pair a person at a counter would mix up, and
/// the type system can stop the code from doing so even though it cannot stop the person.
/// </para>
/// </remarks>
public sealed partial record EstimateNumber
{
    /// <summary>The namespace letter of the estimate series. Never the invoice series (INV-ORD-04).</summary>
    public const string Namespace = "E";

    /// <summary>The longest value the column holds.</summary>
    public const int MaximumLength = 40;

    private EstimateNumber(string value, string branchCode, FinancialYear financialYear, long sequence)
    {
        Value = value;
        BranchCode = branchCode;
        FinancialYear = financialYear;
        Sequence = sequence;
    }

    /// <summary>The number as it is printed on the estimate, for example <c>E-CBE-2627-000001</c>.</summary>
    public string Value { get; }

    /// <summary>The branch the sequence belongs to, upper case.</summary>
    public string BranchCode { get; }

    /// <summary>The financial year the sequence belongs to.</summary>
    public FinancialYear FinancialYear { get; }

    /// <summary>The position within that branch and financial year. One-based, never reused.</summary>
    public long Sequence { get; }

    /// <summary>
    /// Composes a number from its parts.
    /// </summary>
    /// <remarks>
    /// Allocation of <paramref name="sequence"/> belongs to the application layer, which holds
    /// <c>ISequenceAllocator</c> and the branch code; this only checks that it was given one.
    /// </remarks>
    /// <param name="branchCode">The branch code, as held by Identity.</param>
    /// <param name="financialYear">The financial year the sequence belongs to.</param>
    /// <param name="sequence">The allocated position, one-based.</param>
    /// <returns>The number, or the reason it could not be composed.</returns>
    public static Result<EstimateNumber> Create(string? branchCode, FinancialYear financialYear, long sequence)
    {
        ArgumentNullException.ThrowIfNull(financialYear);

        var branch = DisplayNumberFormat.NormaliseBranchCode(branchCode);

        if (branch.IsFailure)
        {
            return Result.Failure<EstimateNumber>(branch.Error);
        }

        if (sequence < DisplayNumberFormat.MinimumSequence)
        {
            return Result.Failure<EstimateNumber>(OrdersErrors.SequenceOutOfRange("sequence"));
        }

        var value = Compose(branch.Value, financialYear, sequence);

        return value.Length > MaximumLength
            ? Result.Failure<EstimateNumber>(OrdersErrors.TooLong("estimateNumber", MaximumLength))
            : Result.Success(new EstimateNumber(value, branch.Value, financialYear, sequence));
    }

    /// <summary>
    /// Reads a printed number back into its parts, or says why it is not one.
    /// </summary>
    /// <remarks>
    /// Upper-cased before it is read, and recomposed from its parts afterwards, so a number that parses
    /// always comes back in the one form the column holds.
    /// </remarks>
    /// <param name="value">The printed number.</param>
    /// <returns>The number, or the reason it is not one.</returns>
    public static Result<EstimateNumber> Parse(string? value)
    {
        var trimmed = value?.Trim();

        if (string.IsNullOrEmpty(trimmed))
        {
            return Result.Failure<EstimateNumber>(OrdersErrors.Required("estimateNumber"));
        }

        if (trimmed.Length > MaximumLength)
        {
            return Result.Failure<EstimateNumber>(OrdersErrors.TooLong("estimateNumber", MaximumLength));
        }

        var match = Pattern().Match(trimmed.ToUpperInvariant());

        if (!match.Success)
        {
            return Result.Failure<EstimateNumber>(OrdersErrors.DisplayNumberMalformed("estimateNumber"));
        }

        var financialYear = FinancialYear.Create(match.Groups[2].Value);

        if (financialYear.IsFailure)
        {
            return Result.Failure<EstimateNumber>(financialYear.Error);
        }

        // A sequence of more digits than a long holds is malformed rather than out of range: nothing
        // this system allocated could have produced it.
        return long.TryParse(
            match.Groups[3].ValueSpan, NumberStyles.None, CultureInfo.InvariantCulture, out var sequence)
            ? Create(match.Groups[1].Value, financialYear.Value, sequence)
            : Result.Failure<EstimateNumber>(OrdersErrors.DisplayNumberMalformed("estimateNumber"));
    }

    /// <summary>Whether a string is a number this system issues.</summary>
    /// <param name="value">The candidate.</param>
    /// <returns>True when it is.</returns>
    public static bool IsWellFormed(string? value) => Parse(value).IsSuccess;

    /// <inheritdoc />
    public override string ToString() => Value;

    private static string Compose(string branchCode, FinancialYear financialYear, long sequence)
        => string.Join(
            DisplayNumberFormat.Separator,
            Namespace,
            branchCode,
            financialYear.Token,
            DisplayNumberFormat.FormatSequence(sequence));

    /// <summary>
    /// The printed shape. The branch-code and sequence rules live in <see cref="DisplayNumberFormat"/>.
    /// </summary>
    [GeneratedRegex("^E-([A-Z0-9]{1,16})-([0-9]{4})-([0-9]{6,})$", RegexOptions.CultureInvariant)]
    private static partial Regex Pattern();
}
