namespace Tailor360.Web;

/// <summary>
/// Anchor type identifying this assembly to <c>WebApplicationFactory</c> in tests. A named type is used
/// rather than the compiler-generated <c>Program</c> class because several executables in this solution
/// use top-level statements, and their generated entry points would otherwise collide in a test project
/// that references more than one of them.
/// </summary>
public sealed class WebEntryPoint;
