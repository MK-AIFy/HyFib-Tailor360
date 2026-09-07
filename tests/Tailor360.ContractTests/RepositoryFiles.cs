namespace Tailor360.ContractTests;

/// <summary>Locates repository files the contract tier reads or rewrites.</summary>
public static class RepositoryFiles
{
    private static readonly Lazy<string> RootLazy = new(FindRepositoryRoot);

    /// <summary>The repository root.</summary>
    public static string Root => RootLazy.Value;

    /// <summary>The committed API document, the published contract for version 1.</summary>
    public static string ApiDocument => Path.Combine(Root, "docs", "api", "openapi.v1.json");

    /// <summary>The environment variable that turns the document check into a regeneration.</summary>
    public const string RegenerateVariable = "TAILOR360_WRITE_OPENAPI";

    /// <summary>The one-liner that rewrites the committed document.</summary>
    public const string RegenerateCommand =
        "TAILOR360_WRITE_OPENAPI=1 dotnet test "
        + "--project tests/Tailor360.ContractTests/Tailor360.ContractTests.csproj";

    /// <summary>True when this run is a regeneration rather than a check.</summary>
    public static bool Regenerating
        => Environment.GetEnvironmentVariable(RegenerateVariable) is { Length: > 0 } value
            && !string.Equals(value, "0", StringComparison.Ordinal)
            && !string.Equals(value, "false", StringComparison.OrdinalIgnoreCase);

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
}
