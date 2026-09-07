using System.Reflection;
using System.Text.RegularExpressions;
using Shouldly;

namespace Tailor360.ArchitectureTests;

/// <summary>
/// Every configuration key a compose file sets reaches an option the code actually binds.
/// </summary>
/// <remarks>
/// <para>
/// A misspelled environment variable is the quietest failure this system has. Nothing rejects it: the
/// configuration provider reads it, no options type asks for that path, and the host starts perfectly
/// on its defaults. The symptom appears somewhere else entirely and much later — a recovery e-mail that
/// never arrives, because the web container is talking to `127.0.0.1:1025` instead of the mailpit
/// service, and the key that was meant to say so was spelled for a section that does not exist.
/// </para>
/// <para>
/// That is not hypothetical: `Email__SmtpHost` and `Email__SmtpPort` were set by both compose files
/// while <c>EmailDeliveryOptions</c> binds <c>Identity:Email</c> and exposes <c>Host</c> and
/// <c>Port</c>, so every recovery and security message in a composed deployment was dropped. The keys
/// had been correct when they were written, as a declaration for an issue that had not landed; they
/// were never revisited when it did. This test is what makes that revisiting compulsory.
/// </para>
/// <para>
/// It is deliberately not a spell-checker over every key. It asserts the one thing that can be asserted
/// mechanically: a key whose first segment matches a section some options type declares must name a
/// property that type really has. A key whose first segment matches nothing is either a framework key
/// or a declaration for an issue still to come, and both are listed below with the reason.
/// </para>
/// </remarks>
[Trait("Category", "Architecture")]
public sealed class ComposeConfigurationTests
{
    /// <summary>The environment-variable separator the configuration provider maps onto <c>:</c>.</summary>
    private const string Separator = "__";

    /// <summary>
    /// First segments that belong to no options type of ours, with why each is allowed to.
    /// </summary>
    /// <remarks>
    /// Every entry here is a key the code does not read today. Two are deliberate declarations for work
    /// that has not landed, kept so that the deployment shape does not change when it does — the compose
    /// files say so beside them. The rest are read by the framework or by Serilog rather than by an
    /// options type of ours.
    /// </remarks>
    private static readonly Dictionary<string, string> Unbound = new(StringComparer.Ordinal)
    {
        ["ObjectStorage"] = "Declared for #31; no options type binds it yet.",
        ["MalwareScanner"] = "Declared for #31; no options type binds it yet.",
        ["Secrets"] = "Read directly by each host before the key-per-file provider is added, so there "
            + "is no options type to bind: it is the setting that says where the other settings come "
            + "from, and it has to be read before them.",
        ["ForwardedHeaders"] = "Read by hand by ForwardedHeadersOptionsSetup into the framework's own "
            + "ForwardedHeadersOptions, which is not a type this repository declares. The section is "
            + "checked by ForwardedHeaderTests against the composed options instead.",
        ["ASPNETCORE"] = "Read by the ASP.NET Core host before configuration binding.",
        ["DOTNET"] = "Read by the .NET runtime.",
        ["Serilog"] = "Read by Serilog's own configuration provider, not by an options type.",
        ["TAILOR360"] = "A compose interpolation variable, not a configuration key.",
    };

    /// <summary>Matches an indented `Some__Key: value` line in a compose environment block.</summary>
    private static readonly Regex EnvironmentKey = new(
        @"^\s{4,}(?<key>[A-Za-z][A-Za-z0-9_]*__[A-Za-z0-9_]+(?:__[A-Za-z0-9_]+)*)\s*:",
        RegexOptions.Compiled | RegexOptions.Multiline);

    [Fact]
    public void EveryComposeKeyBindsToAnOptionThatExists()
    {
        var sections = OptionSections();
        var complaints = new List<string>();

        foreach (var (file, key) in ComposeKeys().Distinct())
        {
            var path = key.Split(Separator, StringSplitOptions.RemoveEmptyEntries);
            if (Unbound.ContainsKey(path[0]))
            {
                continue;
            }

            var match = sections
                .Where(section => PathStartsWith(path, section.Key))
                .OrderByDescending(section => section.Key.Length)
                .Select(section => (Section: section.Key, section.Value))
                .FirstOrDefault();

            if (match.Section is null)
            {
                complaints.Add(
                    $"{file}: '{key}' begins with '{path[0]}', which is not a section any options type "
                    + "declares and is not listed as deliberately unbound. Either the key is misspelled, "
                    + "or it is a declaration for work still to come and belongs in Unbound with its "
                    + "reason.");
                continue;
            }

            var remaining = path.Skip(match.Section.Split(':').Length).ToArray();
            if (!BindsTo(match.Value, remaining))
            {
                complaints.Add(
                    $"{file}: '{key}' binds section '{match.Section}' ({match.Value.Name}), which has no "
                    + $"'{string.Join('.', remaining)}'. The host will start and ignore it.");
            }
        }

        complaints.ShouldBeEmpty(
            "A compose key that binds nothing is the quietest failure this system has:\n"
            + string.Join('\n', complaints));
    }

    /// <summary>The test is worth nothing if it reads no keys.</summary>
    [Fact]
    public void TheComposeFilesAreActuallyRead()
    {
        var keys = ComposeKeys().ToList();

        keys.Count.ShouldBeGreaterThan(8);
        keys.Select(entry => entry.Key).ShouldContain("Identity__Email__Host");
    }

    /// <summary>Negative control: a key for a section nobody declares is caught.</summary>
    [Fact]
    public void AKeyForAnUnknownSectionWouldBeCaught()
    {
        var sections = OptionSections();

        sections.Keys.ShouldNotContain(
            "Email", "the section the misspelled key implied must genuinely not exist");
        Unbound.ShouldNotContainKey("Email");
    }

    /// <summary>Negative control: a key naming a property the options type lacks is caught.</summary>
    [Fact]
    public void AKeyForAnUnknownPropertyWouldBeCaught()
    {
        var sections = OptionSections();
        var email = sections["Identity:Email"];

        BindsTo(email, ["Host"]).ShouldBeTrue();
        BindsTo(email, ["SmtpHost"]).ShouldBeFalse("the old, unbound spelling must not resolve");
    }

    /// <summary>Whether the key path begins with the section path.</summary>
    private static bool PathStartsWith(string[] path, string section)
    {
        var segments = section.Split(':');
        return path.Length > segments.Length
            && segments.Select((segment, index) => (segment, index))
                .All(entry => string.Equals(path[entry.index], entry.segment, StringComparison.Ordinal));
    }

    /// <summary>Whether a configuration path names a settable property, following nested types.</summary>
    private static bool BindsTo(Type type, string[] path)
    {
        var current = type;
        for (var index = 0; index < path.Length; index += 1)
        {
            // A numeric segment is a collection index: the segment before it already matched, and what
            // the element type accepts is the provider's business rather than this test's.
            if (int.TryParse(path[index], out _))
            {
                return true;
            }

            var property = current.GetProperty(
                path[index], BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);

            if (property is null)
            {
                return false;
            }

            current = property.PropertyType;
        }

        return true;
    }

    /// <summary>Every options type that declares a section, by the section it declares.</summary>
    private static Dictionary<string, Type> OptionSections()
    {
        var sections = new Dictionary<string, Type>(StringComparer.Ordinal);

        foreach (var assembly in ProductAssemblies())
        {
            foreach (var type in Types(assembly))
            {
                // A `…OptionsSetup` configures the framework's options type; it is not the shape the
                // section binds to, and reflecting over it would report every real key as missing.
                if (type.Name.EndsWith("Setup", StringComparison.Ordinal))
                {
                    continue;
                }

                var field = type.GetField(
                    "SectionName", BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy);

                if (field is { IsLiteral: true } && field.GetRawConstantValue() is string section
                    && section.Length > 0)
                {
                    sections[section] = type;
                }
            }
        }

        return sections;
    }

    /// <summary>
    /// Every product assembly beside the test, loaded from the output directory.
    /// </summary>
    /// <remarks>
    /// Read off disk rather than from <c>AppDomain.CurrentDomain</c>, because an assembly whose types
    /// nothing in the test has touched yet is not loaded — and an options type nobody referenced is
    /// exactly the one whose section this test needs to know about.
    /// </remarks>
    private static IEnumerable<Assembly> ProductAssemblies()
    {
        foreach (var file in Directory
            .EnumerateFiles(AppContext.BaseDirectory, "Tailor360.*.dll")
            .Order(StringComparer.Ordinal))
        {
            if (Path.GetFileNameWithoutExtension(file).EndsWith("Tests", StringComparison.Ordinal))
            {
                continue;
            }

            Assembly? assembly = null;
            try
            {
                assembly = Assembly.LoadFrom(file);
            }
            catch (BadImageFormatException)
            {
                // A native or mixed-mode file that happens to match the pattern.
            }

            if (assembly is not null)
            {
                yield return assembly;
            }
        }
    }

    /// <summary>
    /// The types an assembly can load. A reflection-only failure on one type must not hide the sections
    /// declared by the rest of the assembly.
    /// </summary>
    private static IEnumerable<Type> Types(Assembly assembly)
    {
        try
        {
            return assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException exception)
        {
            return exception.Types.OfType<Type>();
        }
    }

    /// <summary>Every `A__B` key set in a compose file, with the file it came from.</summary>
    private static IEnumerable<(string File, string Key)> ComposeKeys()
    {
        var directory = Path.Combine(RepositoryLayout.Root, "infra", "compose");

        foreach (var file in Directory.EnumerateFiles(directory, "docker-compose*.yml").Order(StringComparer.Ordinal))
        {
            var text = File.ReadAllText(file);
            foreach (Match match in EnvironmentKey.Matches(text))
            {
                yield return (Path.GetFileName(file), match.Groups["key"].Value);
            }
        }
    }
}
