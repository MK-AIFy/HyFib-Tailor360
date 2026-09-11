using System.Globalization;
using System.Text.RegularExpressions;
using Tailor360.Platform.Abstractions.Results;

namespace Tailor360.Modules.Orders.Domain.DisplayNumbers;

/// <summary>
/// The number an order is known by: <c>O-&lt;branch&gt;-&lt;FY&gt;-000001</c>.
/// </summary>
/// <remarks>
/// <para>
/// Display numbers exist so people can talk to each other
/// (<c>docs/architecture/conventions.md</c> section 3.2). This one is allocated at confirmation from
/// the per-branch, per-financial-year sequence, is never reused, and is <strong>never a lookup key on
/// an unauthenticated surface</strong> (INV-ORD-03): authenticated staff search by it, a customer link
/// resolves by its own token, and the identifier in a path is always the UUIDv7.
/// </para>
/// <para>
/// A type rather than a string, so that a number cannot be assigned from an estimate number, a
/// customer number or a free-text field. The shape is checked here; that the sequence was allocated
/// under a row lock is the application layer's job, because that is where <c>ISequenceAllocator</c>
/// and the branch code live.
/// </para>
/// </remarks>
public sealed partial record OrderNumber
{
    /// <summary>The namespace letter of the order series.</summary>
    public const string Namespace = "O";

    /// <summary>The longest value the column holds.</summary>
    public const int MaximumLength = 40;

    private OrderNumber(string value, string branchCode, FinancialYear financialYear, long sequence)
    {
        Value = value;
        BranchCode = branchCode;
        FinancialYear = financialYear;
        Sequence = sequence;
    }

    /// <summary>The number as it is printed and searched, for example <c>O-CBE-2627-000001</c>.</summary>
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
    /// <c>ISequenceAllocator</c> and the branch code and runs inside the confirmation transaction; this
    /// only checks that it was given one. Nothing here caches or re-offers a number, which is how the
    /// interim position on FOC-04 is honoured: a rolled-back confirmation does not reuse its number.
    /// </remarks>
    /// <param name="branchCode">The branch code, as held by Identity.</param>
    /// <param name="financialYear">The financial year the sequence belongs to.</param>
    /// <param name="sequence">The allocated position, one-based.</param>
    /// <returns>The number, or the reason it could not be composed.</returns>
    public static Result<OrderNumber> Create(string? branchCode, FinancialYear financialYear, long sequence)
    {
        ArgumentNullException.ThrowIfNull(financialYear);

        var branch = DisplayNumberFormat.NormaliseBranchCode(branchCode);

        if (branch.IsFailure)
        {
            return Result.Failure<OrderNumber>(branch.Error);
        }

        if (sequence < DisplayNumberFormat.MinimumSequence)
        {
            return Result.Failure<OrderNumber>(OrdersErrors.SequenceOutOfRange("sequence"));
        }

        var value = Compose(branch.Value, financialYear, sequence);

        return value.Length > MaximumLength
            ? Result.Failure<OrderNumber>(OrdersErrors.TooLong("orderNumber", MaximumLength))
            : Result.Success(new OrderNumber(value, branch.Value, financialYear, sequence));
    }

    /// <summary>
    /// Reads a printed number back into its parts, or says why it is not one.
    /// </summary>
    /// <remarks>
    /// Upper-cased before it is read, because a number arrives typed into a search box or read off a
    /// receipt in whatever case the keyboard produced, and the value it resolves to is recomposed from
    /// its parts — so a number that parses always comes back in the one form the column holds.
    /// </remarks>
    /// <param name="value">The printed number.</param>
    /// <returns>The number, or the reason it is not one.</returns>
    public static Result<OrderNumber> Parse(string? value)
    {
        var trimmed = value?.Trim();

        if (string.IsNullOrEmpty(trimmed))
        {
            return Result.Failure<OrderNumber>(OrdersErrors.Required("orderNumber"));
        }

        if (trimmed.Length > MaximumLength)
        {
            return Result.Failure<OrderNumber>(OrdersErrors.TooLong("orderNumber", MaximumLength));
        }

        var match = Pattern().Match(trimmed.ToUpperInvariant());

        if (!match.Success)
        {
            return Result.Failure<OrderNumber>(OrdersErrors.DisplayNumberMalformed("orderNumber"));
        }

        var financialYear = FinancialYear.Create(match.Groups[2].Value);

        if (financialYear.IsFailure)
        {
            return Result.Failure<OrderNumber>(financialYear.Error);
        }

        // A sequence of more digits than a long holds is malformed rather than out of range: nothing
        // this system allocated could have produced it.
        return long.TryParse(
            match.Groups[3].ValueSpan, NumberStyles.None, CultureInfo.InvariantCulture, out var sequence)
            ? Create(match.Groups[1].Value, financialYear.Value, sequence)
            : Result.Failure<OrderNumber>(OrdersErrors.DisplayNumberMalformed("orderNumber"));
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
    [GeneratedRegex("^O-([A-Z0-9]{1,16})-([0-9]{4})-([0-9]{6,})$", RegexOptions.CultureInvariant)]
    private static partial Regex Pattern();
}
