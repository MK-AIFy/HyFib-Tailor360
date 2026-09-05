using System.Xml.Linq;

namespace Tailor360.ArchitectureTests;

/// <summary>
/// Reads the project graph from the repository on disk. Project references are checked from the
/// <c>.csproj</c> files rather than from compiled metadata, because the compiler omits a reference that
/// happens to be unused; a forbidden reference must fail the build on the day it is added, not on the
/// day someone first calls into it.
/// </summary>
public static class RepositoryLayout
{
    private static readonly Lazy<string> RootLazy = new(FindRepositoryRoot);
    private static readonly Lazy<List<ProjectInfo>> ProjectsLazy = new(LoadProjects);

    /// <summary>The repository root.</summary>
    public static string Root => RootLazy.Value;

    /// <summary>Every project in the solution.</summary>
    public static IReadOnlyList<ProjectInfo> Projects => ProjectsLazy.Value;

    /// <summary>The eleven business modules.</summary>
    public static IReadOnlyList<string> ModuleNames { get; } =
    [
        "Identity", "Customers", "Catalog", "Media", "Orders", "Custody",
        "Inventory", "Billing", "Reporting", "Notifications", "Integration",
    ];

    /// <summary>Projects belonging to a module layer, for example every <c>Domain</c> project.</summary>
    public static IEnumerable<ProjectInfo> ModuleLayer(string layer)
        => Projects.Where(p => p.IsModuleProject && string.Equals(p.Layer, layer, StringComparison.Ordinal));

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "HyFib.Tailor360.slnx")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException(
            "Could not locate the repository root: no HyFib.Tailor360.slnx above " + AppContext.BaseDirectory);
    }

    private static List<ProjectInfo> LoadProjects()
    {
        var projects = new List<ProjectInfo>();

        foreach (var csproj in Directory.EnumerateFiles(Root, "*.csproj", SearchOption.AllDirectories))
        {
            if (csproj.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                || csproj.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            {
                continue;
            }

            var document = XDocument.Load(csproj);
            var name = Path.GetFileNameWithoutExtension(csproj);
            var directory = Path.GetDirectoryName(csproj)!;

            var projectReferences = document.Descendants("ProjectReference")
                .Select(e => e.Attribute("Include")?.Value)
                .Where(v => !string.IsNullOrWhiteSpace(v))
                .Select(v => Path.GetFileNameWithoutExtension(v!.Replace('\\', Path.DirectorySeparatorChar)))
                .OrderBy(v => v, StringComparer.Ordinal)
                .ToArray();

            var packageReferences = document.Descendants("PackageReference")
                .Select(e => e.Attribute("Include")?.Value)
                .Where(v => !string.IsNullOrWhiteSpace(v))
                .Select(v => v!)
                .OrderBy(v => v, StringComparer.Ordinal)
                .ToArray();

            projects.Add(new ProjectInfo(name, csproj, directory, projectReferences, packageReferences));
        }

        return projects;
    }
}

/// <summary>One project in the solution, as declared on disk.</summary>
/// <param name="Name">The project name, which is also its assembly name.</param>
/// <param name="FilePath">Absolute path to the project file.</param>
/// <param name="Directory">Absolute path to the project directory.</param>
/// <param name="ProjectReferences">Names of the projects it references.</param>
/// <param name="PackageReferences">Names of the packages it references.</param>
public sealed record ProjectInfo(
    string Name,
    string FilePath,
    string Directory,
    IReadOnlyList<string> ProjectReferences,
    IReadOnlyList<string> PackageReferences)
{
    private const string ModulePrefix = "Tailor360.Modules.";

    /// <summary>True when this is one of the module layer projects.</summary>
    public bool IsModuleProject => Name.StartsWith(ModulePrefix, StringComparison.Ordinal);

    /// <summary>True when this is a platform library.</summary>
    public bool IsPlatformProject => Name.StartsWith("Tailor360.Platform.", StringComparison.Ordinal);

    /// <summary>True when this is a test project.</summary>
    public bool IsTestProject => Name.EndsWith("Tests", StringComparison.Ordinal);

    /// <summary>The module this project belongs to, or null when it is not a module project.</summary>
    public string? Module => IsModuleProject ? Name[ModulePrefix.Length..].Split('.')[0] : null;

    /// <summary>The layer this project is, for example <c>Domain</c>, or null when it is not a module project.</summary>
    public string? Layer => IsModuleProject ? Name[ModulePrefix.Length..].Split('.').ElementAtOrDefault(1) : null;

    /// <summary>Every C# source file in the project, excluding generated output.</summary>
    public IEnumerable<string> SourceFiles =>
        System.IO.Directory.EnumerateFiles(Directory, "*.cs", SearchOption.AllDirectories)
            .Where(path =>
                !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                && !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal));
}
