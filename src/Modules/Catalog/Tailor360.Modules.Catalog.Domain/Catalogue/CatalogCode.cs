using System.Text.RegularExpressions;

namespace Tailor360.Modules.Catalog.Domain.Catalogue;

/// <summary>
/// The machine key that identifies a category or a service type.
/// </summary>
/// <remarks>
/// <para>
/// Codes and labels are deliberately separate (<c>docs/prd/category-hierarchy.md</c> section 4). A code
/// is what a price list, a report, an event payload, a seed file and an export refer to; a label is
/// what staff and customers read. Renaming "Blouse — Pattern" to "Blouse — Pattern cut" is a label
/// change and must break nothing, which is only true while nothing downstream depends on the label.
/// </para>
/// <para>
/// A code is therefore immutable once the version that introduced it is published, and is never
/// re-used for a different concept after retirement. Neither rule can be enforced here — both are
/// about the code's history rather than its shape — so both are publish-time validations. What this
/// type enforces is the shape and the reserved words.
/// </para>
/// </remarks>
public static partial class CatalogCode
{
    /// <summary>The shortest code the convention allows.</summary>
    public const int MinimumLength = 2;

    /// <summary>The longest code the column holds.</summary>
    public const int MaximumLength = 40;

    /// <summary>
    /// Words that may not be a code, because filters and exports already use them as sentinels.
    /// </summary>
    /// <remarks>
    /// A category legitimately named "All" would collide with the export column that means "every
    /// category", and the collision would show up as a wrong total in a report rather than as an
    /// error. Refusing the four words at the point of entry is cheaper than teaching every reader to
    /// disambiguate them.
    /// </remarks>
    public static readonly IReadOnlySet<string> Reserved =
        new HashSet<string>(StringComparer.Ordinal) { "NONE", "DEFAULT", "ALL", "UNKNOWN" };

    /// <summary>Whether a code is well formed and not reserved.</summary>
    /// <param name="code">The candidate code.</param>
    /// <returns>True when the code may be used.</returns>
    public static bool IsWellFormed(string? code)
        => code is not null
           && code.Length is >= MinimumLength and <= MaximumLength
           && Shape().IsMatch(code)
           && !Reserved.Contains(code);

    /// <summary>
    /// Whether a sub-category's code carries its parent's code as a prefix.
    /// </summary>
    /// <remarks>
    /// A convention rather than a rule, and the difference matters: section 4 makes this a
    /// <em>warning</em> at publish, not an error, so that a code inherited from a shop's existing
    /// paperwork stays valid. It is checked because lineage a reader can see without a lookup is worth
    /// having, and it is a warning because a code that is already published can never be corrected.
    /// </remarks>
    /// <param name="code">The sub-category's code.</param>
    /// <param name="parentCode">The parent category's code.</param>
    /// <returns>True when the convention is followed.</returns>
    public static bool FollowsParentPrefix(string code, string parentCode)
    {
        ArgumentNullException.ThrowIfNull(code);
        ArgumentNullException.ThrowIfNull(parentCode);

        return code.StartsWith(parentCode + '_', StringComparison.Ordinal)
               && code.Length > parentCode.Length + 1;
    }

    /// <summary>Upper snake case, ASCII only, beginning with a letter.</summary>
    [GeneratedRegex("^[A-Z][A-Z0-9_]*$", RegexOptions.CultureInvariant)]
    private static partial Regex Shape();
}
