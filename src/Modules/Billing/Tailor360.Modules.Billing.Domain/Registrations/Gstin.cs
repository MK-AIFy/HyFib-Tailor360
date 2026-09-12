using System.Text.RegularExpressions;

namespace Tailor360.Modules.Billing.Domain.Registrations;

/// <summary>
/// The shape of a Goods and Services Tax Identification Number.
/// </summary>
/// <remarks>
/// A format check and nothing more: fifteen characters — a two-digit state code, a ten-character
/// PAN, an entity character, the letter <c>Z</c> and a check character computed over the first
/// fourteen by the published base-36 weighted algorithm. Whether the number is actually registered
/// is the tax portal's to say, not this class's; the check exists so that a transposed character is
/// caught at the keyboard rather than on a printed invoice.
/// </remarks>
public static partial class Gstin
{
    /// <summary>The length of a GSTIN.</summary>
    public const int Length = 15;

    private const string Alphabet = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ";

    /// <summary>Whether a value has the shape and check character of a GSTIN.</summary>
    /// <param name="value">The candidate.</param>
    /// <returns>True when it does.</returns>
    public static bool IsWellFormed(string? value)
        => value is { Length: Length }
           && Shape().IsMatch(value)
           && CheckCharacter(value.AsSpan(0, Length - 1)) == value[Length - 1];

    /// <summary>The state code a GSTIN begins with.</summary>
    /// <param name="value">A well-formed GSTIN.</param>
    /// <returns>Its first two characters.</returns>
    public static string StateCodeOf(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);

        return value[..2];
    }

    /// <summary>The check character for the first fourteen characters of a GSTIN.</summary>
    /// <param name="prefix">The fourteen characters.</param>
    /// <returns>The check character.</returns>
    public static char CheckCharacter(ReadOnlySpan<char> prefix)
    {
        var total = 0;
        for (var index = 0; index < prefix.Length; index++)
        {
            var value = Alphabet.IndexOf(char.ToUpperInvariant(prefix[index]), StringComparison.Ordinal);
            if (value < 0)
            {
                return '\0';
            }

            var factor = index % 2 == 0 ? 1 : 2;
            var product = value * factor;
            total += (product / Alphabet.Length) + (product % Alphabet.Length);
        }

        return Alphabet[(Alphabet.Length - (total % Alphabet.Length)) % Alphabet.Length];
    }

    [GeneratedRegex("^[0-9]{2}[A-Z]{5}[0-9]{4}[A-Z][1-9A-Z]Z[0-9A-Z]$")]
    private static partial Regex Shape();
}
