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
    public static string Version { get; } = ResolveVersion(
        typeof(BuildInformation).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion,
        typeof(BuildInformation).Assembly.GetName().Version?.ToString());

    /// <summary>
    /// The short build hash. Taken from the source-revision suffix the SDK appends to the informational
    /// version, falling back to a stable marker when the build is not from a repository.
    /// </summary>
    public static string BuildHash { get; } = ExtractHash(Version);

    /// <summary>
    /// The fallback chain <see cref="Version"/> reads, as a pure function so a unit test can drive it
    /// without controlling this assembly's own compiled attribute: the informational version — the
    /// whole string, `VersionPrefix` plus a `+&lt;revision&gt;` suffix when the SDK supplied one — the
    /// bare assembly version, then a stable default. Never truncated and never re-parsed.
    /// </summary>
    public static string ResolveVersion(string? informationalVersion, string? assemblyVersion)
        => informationalVersion is { Length: > 0 } value ? value
            : assemblyVersion is { Length: > 0 } fallback ? fallback
            : "0.0.0";

    /// <summary>
    /// Splits the source-revision suffix off an informational version. A pre-release identifier's own
    /// hyphen (<c>1.2.0-rc.1</c>) is never mistaken for the separator — only the first <c>+</c>, which is
    /// what the SDK's own <c>VersionPrefix+SourceRevisionId</c> shape guarantees, is.
    /// </summary>
    public static string ExtractHash(string informationalVersion)
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
