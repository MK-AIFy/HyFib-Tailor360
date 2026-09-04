using System.Reflection;

namespace Tailor360.Platform.Observability;

/// <summary>
/// Anchor type for this assembly, used by architecture tests to load it without relying on its name.
/// </summary>
public static class AssemblyReference
{
    /// <summary>This assembly.</summary>
    public static Assembly Assembly { get; } = typeof(AssemblyReference).Assembly;
}
