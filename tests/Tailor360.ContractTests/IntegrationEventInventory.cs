using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
using Tailor360.Platform.Abstractions.Events;
using Tailor360.Platform.Persistence.Outbox;

namespace Tailor360.ContractTests;

/// <summary>
/// Every integration event the solution publishes, found the way a subscriber meets them: by their
/// wire name and the shape they serialise to.
/// </summary>
/// <remarks>
/// <para>
/// The inventory is built from assemblies on disk rather than from
/// <c>AppDomain.CurrentDomain.GetAssemblies()</c>, because an assembly whose types nothing in the test
/// has touched is not loaded — and an event nobody has published yet is exactly the one whose name and
/// schema this suite exists to check.
/// </para>
/// <para>
/// The serialised property names come from the real encoder,
/// <see cref="OutboxPayload.SerializerOptions"/>, and not from a naming convention reimplemented here.
/// A test that guessed <c>camelCase</c> would keep passing on the day somebody changed the encoder,
/// which is the day the schemas stop describing what is on the wire.
/// </para>
/// </remarks>
public static partial class IntegrationEventInventory
{
    /// <summary>Where an event's JSON Schema and example live.</summary>
    public static string DocumentationDirectory
        => Path.Combine(RepositoryFiles.Root, "docs", "integration", "events");

    /// <summary>
    /// <c>&lt;module&gt;.&lt;event-name&gt;.v&lt;major&gt;</c>, from
    /// <c>docs/architecture/conventions.md</c> section 5.5.
    /// </summary>
    /// <remarks>
    /// The event name is hyphenated and the whole thing is lower case, so that a name is the same
    /// string in a schema file, a routing key and a log line. The major has no leading zero and starts
    /// at one, because <c>v0</c> and <c>v01</c> are two ways of writing a version nobody agreed on.
    /// </remarks>
    [GeneratedRegex(@"^[a-z][a-z0-9]*\.[a-z0-9]+(-[a-z0-9]+)*\.v[1-9][0-9]*$")]
    public static partial Regex WireName();

    /// <summary>The major version named in a wire name.</summary>
    [GeneratedRegex(@"\.v([1-9][0-9]*)$")]
    public static partial Regex MajorInName();

    /// <summary>
    /// Field names that would mean a payload is carrying personal data.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A blunt instrument, and deliberately so. <c>docs/architecture/conventions.md</c> section 5.5
    /// admits "identifiers, codes, statuses, timestamps, amounts and branch codes only" unless the
    /// event is classified personal and the subscriber approved for it, and the judgement about a
    /// particular field belongs to a reviewer reading the schema. What this catches is the field
    /// somebody added without that conversation happening at all — a <c>phone</c> on an event is not
    /// a borderline call.
    /// </para>
    /// <para>
    /// Matching is on the whole serialised name, case-insensitively, and on a name that merely
    /// contains one of these as a word — so <c>customerName</c> is caught and <c>purposeKey</c> is
    /// not. A payload that genuinely needs one of these is the approval conversation section 5.5
    /// describes, and it is recorded by adding the field's event to the exemptions below rather than
    /// by widening the pattern.
    /// </para>
    /// </remarks>
    public static IReadOnlySet<string> PersonalFieldWords { get; } =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "name", "firstname", "lastname", "fullname", "displayname", "nativename",
            "phone", "telephone", "mobile", "msisdn", "email", "address", "postcode", "pincode",
            "dob", "birthdate", "measurement", "measurements", "photo", "image", "note", "notes",
        };

    /// <summary>
    /// Events allowed to carry a field the scan would otherwise reject, and why.
    /// </summary>
    /// <remarks>
    /// Empty, and adding to it is a decision about who may read personal data rather than a way to
    /// make a test pass: section 5.3 of <c>docs/nfr/data-classification.md</c> names who may access
    /// each class, and a payload that broadcasts one to every registered handler widens that list.
    /// An entry here belongs in the same pull request as the approval it records.
    /// </remarks>
    public static IReadOnlyDictionary<string, string> PersonalFieldExemptions { get; } =
        new Dictionary<string, string>(StringComparer.Ordinal);

    /// <summary>
    /// Splits a serialised property name into the words a reviewer would read in it, so that
    /// <c>customerName</c> is caught by the personal-data scan and <c>purposeKey</c> is not.
    /// </summary>
    /// <remarks>
    /// The whole name is yielded first, because a field called exactly <c>name</c> or <c>phone</c> has
    /// no camel hump to split on and is the most obvious case of all.
    /// </remarks>
    /// <param name="property">The serialised name.</param>
    /// <returns>The whole name, then each camel-cased word in it.</returns>
    public static IEnumerable<string> Words(string property)
    {
        ArgumentNullException.ThrowIfNull(property);

        yield return property;

        var start = 0;
        for (var index = 1; index <= property.Length; index++)
        {
            if (index == property.Length || char.IsUpper(property[index]))
            {
                yield return property[start..index];
                start = index;
            }
        }
    }

    /// <summary>Every concrete integration event declared in the solution.</summary>
    /// <returns>One entry per event type, ordered by wire name.</returns>
    public static IReadOnlyList<PublishedIntegrationEvent> All()
    {
        var events = new List<PublishedIntegrationEvent>();

        foreach (var assembly in ProductAssemblies())
        {
            foreach (var type in Types(assembly))
            {
                if (type.IsAbstract || type.IsInterface || !typeof(IIntegrationEvent).IsAssignableFrom(type))
                {
                    continue;
                }

                events.Add(Describe(type));
            }
        }

        return [.. events.OrderBy(published => published.EventType, StringComparer.Ordinal)];
    }

    /// <summary>
    /// Reads an event's wire name, version and serialised properties without constructing one.
    /// </summary>
    /// <remarks>
    /// <c>EventType</c> and <c>SchemaVersion</c> are instance properties, so reading them needs an
    /// instance — and an event's constructor takes only data, never a dependency, so an uninitialised
    /// one answers both correctly. That is a real constraint on an event rather than a trick: a type
    /// whose wire name depended on its data would be a type a subscriber could not route on.
    /// </remarks>
    /// <param name="type">The event type.</param>
    /// <returns>What the contract tier needs to know about it.</returns>
    private static PublishedIntegrationEvent Describe(Type type)
    {
        var instance = (IIntegrationEvent)System.Runtime.CompilerServices
            .RuntimeHelpers.GetUninitializedObject(type);

        var properties = JsonSerializer
            .SerializeToNode(instance, type, OutboxPayload.SerializerOptions)!
            .AsObject()
            .Select(property => property.Key)
            .ToList();

        return new PublishedIntegrationEvent(
            type,
            instance.EventType,
            instance.SchemaVersion,
            properties);
    }

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
}

/// <summary>One integration event, as the contract tier sees it.</summary>
/// <param name="Type">The declaring CLR type.</param>
/// <param name="EventType">The wire name it publishes under.</param>
/// <param name="SchemaVersion">The major version it declares.</param>
/// <param name="SerialisedProperties">
/// The property names it puts on the wire, from the real encoder. This is what a schema has to
/// describe and what an example has to carry.
/// </param>
public sealed record PublishedIntegrationEvent(
    Type Type,
    string EventType,
    int SchemaVersion,
    IReadOnlyList<string> SerialisedProperties)
{
    /// <summary>The module segment of the wire name.</summary>
    public string Module => EventType.Split('.', 2)[0];

    /// <summary>The schema file that must describe this event.</summary>
    public string SchemaPath
        => Path.Combine(IntegrationEventInventory.DocumentationDirectory, $"{EventType}.schema.json");

    /// <summary>The example file that must accompany it.</summary>
    public string ExamplePath
        => Path.Combine(IntegrationEventInventory.DocumentationDirectory, $"{EventType}.example.json");
}
