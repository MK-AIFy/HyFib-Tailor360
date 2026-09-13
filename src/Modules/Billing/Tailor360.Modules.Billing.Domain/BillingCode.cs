using System.Text.RegularExpressions;

namespace Tailor360.Modules.Billing.Domain;

/// <summary>
/// The shape of a Billing code — a tax code, a price-list item code, a discount rule code.
/// </summary>
/// <remarks>
/// The same grammar the catalogue uses for a category code, so that a price-list item code and the
/// service-type reference that names it read alike in a seed file, an export and a report.
/// </remarks>
public static partial class BillingCode
{
    /// <summary>The shortest code accepted.</summary>
    public const int MinimumLength = 2;

    /// <summary>The longest code accepted.</summary>
    public const int MaximumLength = 60;

    /// <summary>Whether a code is upper snake case of an accepted length.</summary>
    /// <param name="code">The candidate.</param>
    /// <returns>True when it is.</returns>
    public static bool IsWellFormed(string? code)
        => code is not null
           && code.Length >= MinimumLength
           && code.Length <= MaximumLength
           && Shape().IsMatch(code);

    [GeneratedRegex("^[A-Z][A-Z0-9_]*$")]
    private static partial Regex Shape();
}
