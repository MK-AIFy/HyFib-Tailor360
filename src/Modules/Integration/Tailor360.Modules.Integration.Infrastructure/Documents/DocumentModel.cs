using System.Globalization;

namespace Tailor360.Modules.Integration.Infrastructure.Documents;

/// <summary>
/// Reads the model a document is rendered from — the port hands it over as a dictionary so the rendering
/// library never sees a module's types — leniently and by name, in the invariant culture: a missing key
/// renders as nothing, and a value renders as the template says rather than as the caller typed it.
/// </summary>
/// <param name="values">The model, or a section of it.</param>
public sealed class DocumentModel(IReadOnlyDictionary<string, object?> values)
{
    private static readonly IReadOnlyDictionary<string, object?> Empty = new Dictionary<string, object?>(StringComparer.Ordinal);

    /// <summary>A model over nothing.</summary>
    public static DocumentModel None { get; } = new(Empty);

    /// <summary>A string, or empty.</summary>
    public string Text(string key) => values.TryGetValue(key, out var value) && value is not null ? Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty : string.Empty;

    /// <summary>A boolean, or false.</summary>
    public bool Flag(string key) => values.TryGetValue(key, out var value) && value is bool flag && flag;

    /// <summary>A decimal, or zero.</summary>
    public decimal Amount(string key)
        => values.TryGetValue(key, out var value) && value is not null && decimal.TryParse(Convert.ToString(value, CultureInfo.InvariantCulture), NumberStyles.Number, CultureInfo.InvariantCulture, out var amount)
            ? amount
            : 0m;

    /// <summary>A nested section, or an empty one.</summary>
    public DocumentModel Section(string key)
        => values.TryGetValue(key, out var value) && value is IReadOnlyDictionary<string, object?> section ? new DocumentModel(section) : None;

    /// <summary>A list of sections, or none.</summary>
    public IReadOnlyList<DocumentModel> Sections(string key)
        => values.TryGetValue(key, out var value) && value is IEnumerable<object?> items
            ? [.. items.OfType<IReadOnlyDictionary<string, object?>>().Select(item => new DocumentModel(item))]
            : [];

    /// <summary>
    /// An amount as an Indian document writes it: two decimals, grouped by lakh and crore — <c>1,13,400.00</c> —
    /// with the currency's symbol where the currency is the rupee.
    /// </summary>
    /// <param name="amount">The amount.</param>
    /// <param name="currency">The ISO code.</param>
    public static string Money(decimal amount, string currency)
    {
        var negative = amount < 0m;
        var rounded = decimal.Round(Math.Abs(amount), 2, MidpointRounding.AwayFromZero);
        var whole = decimal.Truncate(rounded);
        var fraction = (rounded - whole).ToString("0.00", CultureInfo.InvariantCulture)[1..];
        var digits = whole.ToString("0", CultureInfo.InvariantCulture);
        string grouped;
        if (digits.Length <= 3)
        {
            grouped = digits;
        }
        else
        {
            var last = digits[^3..];
            var rest = digits[..^3];
            var parts = new List<string>();
            while (rest.Length > 2)
            {
                parts.Insert(0, rest[^2..]);
                rest = rest[..^2];
            }

            if (rest.Length > 0)
            {
                parts.Insert(0, rest);
            }

            grouped = string.Join(',', parts) + "," + last;
        }

        var symbol = currency == "INR" ? "₹" : currency + " ";
        return $"{(negative ? "-" : string.Empty)}{symbol}{grouped}{fraction}";
    }
}
