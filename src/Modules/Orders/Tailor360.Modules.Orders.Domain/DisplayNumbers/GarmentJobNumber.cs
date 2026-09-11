using System.Globalization;
using System.Text.RegularExpressions;
using Tailor360.Platform.Abstractions.Results;

namespace Tailor360.Modules.Orders.Domain.DisplayNumbers;

/// <summary>
/// The number a garment job is known by: <c>J-&lt;branch&gt;-&lt;FY&gt;-000001-01</c>.
/// </summary>
/// <remarks>
/// <para>
/// <c>docs/architecture/conventions.md</c> section 3.2 defines the sequence key as <em>the order number
/// plus a two-digit job index</em>, so the type carries the <see cref="DisplayNumbers.OrderNumber"/> it
/// was minted from rather than a copy of its text. That makes the relationship structural: confirming
/// an order can compare the job number's order against its own and refuse a number minted for a
/// different order, without parsing anything.
/// </para>
/// <para>
/// <strong>This is not the barcode payload.</strong> The payload is opaque — a namespace letter and
/// twelve random characters with a check character (conventions.md section 3.3, INV-BID-04) — and
/// carries no display number and no meaning of any kind. This number is printed on the job card so
/// that two people standing at a rail can agree which garment they mean.
/// </para>
/// </remarks>
public sealed partial record GarmentJobNumber
{
    /// <summary>The namespace letter of the garment job series.</summary>
    public const string Namespace = "J";

    /// <summary>The longest value the column holds.</summary>
    public const int MaximumLength = 48;

    private GarmentJobNumber(string value, OrderNumber orderNumber, int jobIndex)
    {
        Value = value;
        OrderNumber = orderNumber;
        JobIndex = jobIndex;
    }

    /// <summary>The number as it is printed on the job card, for example <c>J-CBE-2627-000001-01</c>.</summary>
    public string Value { get; }

    /// <summary>
    /// The order this garment belongs to, carried as its own number so the relationship is structural.
    /// </summary>
    public OrderNumber OrderNumber { get; }

    /// <summary>The one-based position of this garment within its order.</summary>
    public int JobIndex { get; }

    /// <summary>The branch the order's sequence belongs to, upper case.</summary>
    public string BranchCode => OrderNumber.BranchCode;

    /// <summary>The financial year the order's sequence belongs to.</summary>
    public FinancialYear FinancialYear => OrderNumber.FinancialYear;

    /// <summary>The order's position within that branch and financial year.</summary>
    public long Sequence => OrderNumber.Sequence;

    /// <summary>
    /// Mints the number for one garment of an order.
    /// </summary>
    /// <param name="orderNumber">The order's number, which this one is built from.</param>
    /// <param name="jobIndex">The one-based position of the garment within the order.</param>
    /// <returns>The number, or the reason it could not be minted.</returns>
    public static Result<GarmentJobNumber> For(OrderNumber orderNumber, int jobIndex)
    {
        ArgumentNullException.ThrowIfNull(orderNumber);

        if (jobIndex < DisplayNumberFormat.MinimumJobIndex)
        {
            return Result.Failure<GarmentJobNumber>(OrdersErrors.JobIndexOutOfRange);
        }

        var value = Compose(orderNumber, jobIndex);

        return value.Length > MaximumLength
            ? Result.Failure<GarmentJobNumber>(OrdersErrors.TooLong("jobNumber", MaximumLength))
            : Result.Success(new GarmentJobNumber(value, orderNumber, jobIndex));
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
    public static Result<GarmentJobNumber> Parse(string? value)
    {
        var trimmed = value?.Trim();

        if (string.IsNullOrEmpty(trimmed))
        {
            return Result.Failure<GarmentJobNumber>(OrdersErrors.Required("jobNumber"));
        }

        if (trimmed.Length > MaximumLength)
        {
            return Result.Failure<GarmentJobNumber>(OrdersErrors.TooLong("jobNumber", MaximumLength));
        }

        var match = Pattern().Match(trimmed.ToUpperInvariant());

        if (!match.Success)
        {
            return Result.Failure<GarmentJobNumber>(OrdersErrors.DisplayNumberMalformed("jobNumber"));
        }

        var financialYear = FinancialYear.Create(match.Groups[2].Value);

        if (financialYear.IsFailure)
        {
            return Result.Failure<GarmentJobNumber>(financialYear.Error);
        }

        // A sequence or an index of more digits than the type holds is malformed rather than out of
        // range: nothing this system allocated could have produced it.
        if (!long.TryParse(
                match.Groups[3].ValueSpan, NumberStyles.None, CultureInfo.InvariantCulture, out var sequence)
            || !int.TryParse(
                match.Groups[4].ValueSpan, NumberStyles.None, CultureInfo.InvariantCulture, out var jobIndex))
        {
            return Result.Failure<GarmentJobNumber>(OrdersErrors.DisplayNumberMalformed("jobNumber"));
        }

        var orderNumber = OrderNumber.Create(match.Groups[1].Value, financialYear.Value, sequence);

        return orderNumber.IsFailure
            ? Result.Failure<GarmentJobNumber>(orderNumber.Error)
            : For(orderNumber.Value, jobIndex);
    }

    /// <summary>Whether a string is a number this system issues.</summary>
    /// <param name="value">The candidate.</param>
    /// <returns>True when it is.</returns>
    public static bool IsWellFormed(string? value) => Parse(value).IsSuccess;

    /// <inheritdoc />
    public override string ToString() => Value;

    private static string Compose(OrderNumber orderNumber, int jobIndex)
        => string.Join(
            DisplayNumberFormat.Separator,
            Namespace,
            orderNumber.BranchCode,
            orderNumber.FinancialYear.Token,
            DisplayNumberFormat.FormatSequence(orderNumber.Sequence),
            DisplayNumberFormat.FormatJobIndex(jobIndex));

    /// <summary>
    /// The printed shape. The branch-code and sequence rules live in <see cref="DisplayNumberFormat"/>.
    /// </summary>
    [GeneratedRegex("^J-([A-Z0-9]{1,16})-([0-9]{4})-([0-9]{6,})-([0-9]{2,})$", RegexOptions.CultureInvariant)]
    private static partial Regex Pattern();
}
