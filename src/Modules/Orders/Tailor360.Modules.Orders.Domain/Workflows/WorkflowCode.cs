using System.Text.RegularExpressions;

namespace Tailor360.Modules.Orders.Domain.Workflows;

/// <summary>
/// The machine key that identifies a workflow definition or one of its phases.
/// </summary>
/// <remarks>
/// Mirrors <c>Catalog.Domain.Catalogue.CatalogCode</c>'s shape rather than referencing it (ARCH-004: a
/// <c>Domain</c> project reaches nothing but <c>Platform.Abstractions</c>). The convention it mirrors —
/// upper snake case, a handful of reserved sentinel words — is the same one every machine key in this system
/// follows, from a catalogue code to <c>OrderDraftGarment.CategoryKey</c>; a code is what an event payload, a
/// seed file and an administration screen refer to, so its shape has one home per module rather than one
/// definition the whole system trusts across a boundary it may not cross.
/// </remarks>
public static partial class WorkflowCode
{
    /// <summary>The shortest code the convention allows.</summary>
    public const int MinimumLength = 2;

    /// <summary>The longest code the column holds.</summary>
    public const int MaximumLength = 40;

    /// <summary>
    /// Words that may not be a code, because a filter or an export already uses them as a sentinel.
    /// </summary>
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

    /// <summary>Upper snake case, ASCII only, beginning with a letter.</summary>
    [GeneratedRegex("^[A-Z][A-Z0-9_]*$", RegexOptions.CultureInvariant)]
    private static partial Regex Shape();
}
