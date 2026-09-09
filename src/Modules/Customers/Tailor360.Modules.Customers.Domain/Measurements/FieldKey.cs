using System.Text.RegularExpressions;
using Tailor360.Platform.Abstractions.Results;

namespace Tailor360.Modules.Customers.Domain.Measurements;

/// <summary>
/// The stable name of one measurement field.
/// </summary>
/// <remarks>
/// <para>
/// The key is what a captured value is filed under, so it is immutable once its version is published and unique
/// within that version (<c>docs/prd/measurement-templates.md</c> section 3). The <em>label</em> is the editable,
/// localisable part: <c>blouse_full_length</c> is "Blouse length" on a blouse and "Choli length" on a lehenga, and
/// the same stored number means the same thing in both.
/// </para>
/// <para>
/// Keys are drawn from the shared dictionary in section 4 so that <c>chest_bust</c> means the same measurement in
/// every template. This type enforces the shape; that a key carries the dictionary's meaning is a review question,
/// not something a regular expression can check.
/// </para>
/// </remarks>
public sealed partial class FieldKey
{
    /// <summary>The shortest usable key.</summary>
    public const int MinimumLength = 2;

    /// <summary>The longest key the column holds.</summary>
    public const int MaximumLength = 60;

    private FieldKey(string value) => Value = value;

    /// <summary>The key itself, lower snake case.</summary>
    public string Value { get; }

    /// <summary>Reads a key, or says why it is not one.</summary>
    /// <param name="value">The candidate.</param>
    /// <returns>The key, or the reason it was refused.</returns>
    public static Result<FieldKey> Create(string? value)
    {
        var trimmed = value?.Trim() ?? string.Empty;

        if (trimmed.Length == 0)
        {
            return Result.Failure<FieldKey>(MeasurementErrors.Required("key"));
        }

        return trimmed.Length is < MinimumLength or > MaximumLength || !Pattern().IsMatch(trimmed)
            ? Result.Failure<FieldKey>(MeasurementErrors.FieldKeyMalformed(trimmed))
            : Result.Success(new FieldKey(trimmed));
    }

    /// <summary>Whether a string is a well-formed key.</summary>
    /// <param name="value">The candidate.</param>
    /// <returns>True when it is.</returns>
    public static bool IsWellFormed(string? value) => Create(value).IsSuccess;

    /// <inheritdoc />
    public override string ToString() => Value;

    // Lower snake case, ASCII, starting with a letter: the shape a JSON property, a column alias and a printed
    // sheet header can all carry without quoting or transliteration.
    [GeneratedRegex("^[a-z][a-z0-9_]*$", RegexOptions.CultureInvariant)]
    private static partial Regex Pattern();
}
