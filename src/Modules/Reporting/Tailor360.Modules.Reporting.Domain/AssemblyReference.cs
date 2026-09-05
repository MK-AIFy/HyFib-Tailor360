using System.Reflection;

namespace Tailor360.Modules.Reporting.Domain;

/// <summary>
/// Anchor type for this assembly. Architecture tests load the assembly through this type rather than
/// by name, so renaming a project cannot silently drop a rule from the suite.
/// </summary>
public static class AssemblyReference
{
    /// <summary>This assembly.</summary>
    public static Assembly Assembly { get; } = typeof(AssemblyReference).Assembly;
}
