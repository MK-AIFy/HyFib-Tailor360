using System.Text.RegularExpressions;

namespace Tailor360.Modules.Catalog.Domain.Design;

/// <summary>
/// The shape of a design group code, a design option code and an illustration reference.
/// </summary>
/// <remarks>
/// <para>
/// Section 2 of <c>docs/prd/design-options.md</c>: a group code is <c>lower_snake_case</c> and unique
/// within its category; an option code is <c>UPPER_SNAKE_CASE</c> and unique within its group. Both
/// are immutable once the version that introduced them is published, and both are what rules, seed
/// files, event payloads and exports refer to — a label is what people read.
/// </para>
/// <para>
/// <c>NONE</c> is a <em>reserved option code</em> with one fixed meaning — "the customer chose not to
/// have this feature" — and is selectable in every group; it is the one reserved word of
/// <see cref="Catalogue.CatalogCode"/> that an option may carry. <c>DEFAULT</c>, <c>ALL</c> and
/// <c>UNKNOWN</c> stay forbidden everywhere, and no group may be called <c>none</c> either, so that
/// <c>design.none = NONE</c> can never be written.
/// </para>
/// </remarks>
public static partial class DesignCode
{
    /// <summary>The shortest code accepted.</summary>
    public const int MinimumLength = 2;

    /// <summary>The longest code accepted.</summary>
    public const int MaximumLength = 40;

    /// <summary>The longest illustration reference accepted.</summary>
    public const int MaximumIllustrationKeyLength = 200;

    /// <summary>The option code meaning "the customer chose not to have this feature".</summary>
    public const string None = "NONE";

    private static readonly HashSet<string> ForbiddenOptionCodes =
        new HashSet<string>(StringComparer.Ordinal) { "DEFAULT", "ALL", "UNKNOWN" };

    private static readonly HashSet<string> ForbiddenGroupCodes =
        new HashSet<string>(StringComparer.Ordinal) { "none", "default", "all", "unknown" };

    /// <summary>Whether a string may be a group code.</summary>
    /// <param name="code">The candidate.</param>
    /// <returns>True for <c>lower_snake_case</c> of the permitted length that is not a reserved word.</returns>
    public static bool IsWellFormedGroupCode(string? code)
        => code is not null
           && code.Length is >= MinimumLength and <= MaximumLength
           && GroupShape().IsMatch(code)
           && !ForbiddenGroupCodes.Contains(code);

    /// <summary>Whether a string may be an option code.</summary>
    /// <param name="code">The candidate.</param>
    /// <returns>True for <c>UPPER_SNAKE_CASE</c> of the permitted length that is not a forbidden word.</returns>
    public static bool IsWellFormedOptionCode(string? code)
        => code is not null
           && code.Length is >= MinimumLength and <= MaximumLength
           && OptionShape().IsMatch(code)
           && !ForbiddenOptionCodes.Contains(code);

    /// <summary>Whether a string is an illustration reference, <c>sheet_key#group_code.OPTION_CODE</c>.</summary>
    /// <param name="key">The candidate.</param>
    /// <returns>True when it names a sheet, a group and an option in the form section 6 fixes.</returns>
    public static bool IsWellFormedIllustrationKey(string? key)
        => key is not null
           && key.Length <= MaximumIllustrationKeyLength
           && IllustrationShape().IsMatch(key);

    /// <summary>
    /// Whether an illustration reference is anchored on this group and this option — the two parts after
    /// the sheet key. A reference of the right shape that names another option would show the customer
    /// another option's drawing.
    /// </summary>
    /// <param name="key">The reference.</param>
    /// <param name="groupCode">The owning group's code.</param>
    /// <param name="optionCode">This option's code.</param>
    /// <returns>True when the anchor is <c>groupCode.optionCode</c>.</returns>
    public static bool IllustrationKeyNames(string key, string groupCode, string optionCode)
    {
        ArgumentNullException.ThrowIfNull(key);

        var anchor = key.IndexOf('#', StringComparison.Ordinal);
        return anchor >= 0
               && string.Equals(key[(anchor + 1)..], $"{groupCode}.{optionCode}", StringComparison.Ordinal);
    }

    [GeneratedRegex("^[a-z][a-z0-9_]*$", RegexOptions.CultureInvariant)]
    private static partial Regex GroupShape();

    [GeneratedRegex("^[A-Z][A-Z0-9_]*$", RegexOptions.CultureInvariant)]
    private static partial Regex OptionShape();

    [GeneratedRegex("^[a-z][a-z0-9_]*#[a-z][a-z0-9_]*\\.[A-Z][A-Z0-9_]*$", RegexOptions.CultureInvariant)]
    private static partial Regex IllustrationShape();
}
