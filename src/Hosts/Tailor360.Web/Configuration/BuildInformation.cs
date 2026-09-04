using System.Reflection;

namespace Tailor360.Web.Configuration;

/// <summary>
/// The identity of this build. The progressive web application compares the hash it was built with
/// against this value to detect that it is running against a newer server and to prompt a safe reload
/// (#51), so the value must change with every build.
/// </summary>
public static class BuildInformation
{
    /// <summary>The informational version, which carries the source revision when the build sets it.</summary>
    public static string Version { get; } =
        typeof(BuildInformation).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
        ?? typeof(BuildInformation).Assembly.GetName().Version?.ToString()
        ?? "0.0.0";

    /// <summary>
    /// The short build hash. Taken from the source-revision suffix the SDK appends to the informational
    /// version, falling back to a stable marker when the build is not from a repository.
    /// </summary>
    public static string BuildHash { get; } = ExtractHash(Version);

    private static string ExtractHash(string informationalVersion)
    {
        var plus = informationalVersion.IndexOf('+', StringComparison.Ordinal);
        if (plus < 0 || plus == informationalVersion.Length - 1)
        {
            return "local";
        }

        var hash = informationalVersion[(plus + 1)..];
        return hash.Length > 12 ? hash[..12] : hash;
    }
}
