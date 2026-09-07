using System.Collections;
using System.Reflection;

namespace Tailor360.ContractTests;

/// <summary>
/// The ARCH-013 detector: where a type published on the HTTP surface is allowed to be declared, and how
/// to find every type a payload actually publishes.
/// </summary>
/// <remarks>
/// <para>
/// It is two separable pieces on purpose. <see cref="Classify"/> decides whether an assembly may declare
/// a published payload, which is the judgement the rule is about; <see cref="Walk"/> finds the types a
/// payload reaches, which is the part that is easy to get subtly wrong — a domain entity two properties
/// deep inside a response is exactly as published as one returned directly, and a walker that stops at
/// the first level would never say so. Each piece has its own control in
/// <c>EndpointPayloadTests</c>.
/// </para>
/// </remarks>
public static class PayloadTypeInspector
{
    /// <summary>
    /// The platform types that exist precisely in order to be serialised, and are therefore allowed on
    /// the wire despite not living in an <c>Api</c> project.
    /// </summary>
    /// <remarks>
    /// The list is closed and short by design: it is the ARCH-013 exception register, and a type is added
    /// to it only in a pull request that says why publishing that shape is safe. Adding a type here is a
    /// decision about the public API, not a convenience.
    /// </remarks>
    public static IReadOnlySet<string> SerialisablePlatformTypes { get; } =
        new HashSet<string>(StringComparer.Ordinal)
        {
            // An amount and its currency, with the rounding rules of conventions.md section 1. Every
            // money field on the wire is this shape, and re-declaring it per module is how two of them
            // start rounding differently.
            "Tailor360.Platform.Abstractions.Money.Money",

            // The branch reach an endpoint declares. It is an enumeration of three values with no
            // behaviour, published so that a client can say which scope it is asking within.
            "Tailor360.Platform.Abstractions.Multitenancy.BranchScope",
        };

    /// <summary>Where a type is declared, from the point of view of the rule.</summary>
    /// <param name="assemblyName">The declaring assembly's simple name.</param>
    /// <param name="fullTypeName">The type's full name, for the platform allowlist.</param>
    /// <returns>The classification.</returns>
    public static PayloadOrigin Classify(string assemblyName, string fullTypeName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(assemblyName);

        if (!assemblyName.StartsWith("Tailor360.", StringComparison.Ordinal))
        {
            // The framework and the runtime. Primitives, collections, System.Text.Json's element type
            // and the framework's own payloads are not this rule's business.
            return PayloadOrigin.Framework;
        }

        if (assemblyName.EndsWith(".Domain", StringComparison.Ordinal))
        {
            return PayloadOrigin.Domain;
        }

        if (assemblyName.EndsWith(".Api", StringComparison.Ordinal)
            || string.Equals(assemblyName, "Tailor360.Web", StringComparison.Ordinal))
        {
            return PayloadOrigin.PublishedApi;
        }

        if (SerialisablePlatformTypes.Contains(fullTypeName))
        {
            return PayloadOrigin.SerialisablePlatform;
        }

        return assemblyName.EndsWith(".Contracts", StringComparison.Ordinal)
            ? PayloadOrigin.ModuleContracts
            : PayloadOrigin.InternalLayer;
    }

    /// <summary>
    /// Every type a payload publishes: the type itself, the types of its properties and constructor
    /// parameters, the element types of its collections, and so on transitively.
    /// </summary>
    /// <param name="root">The declared request or response type.</param>
    /// <returns>The reachable declared types, including the root.</returns>
    public static IReadOnlyList<Type> Walk(Type root)
    {
        ArgumentNullException.ThrowIfNull(root);

        var seen = new HashSet<Type>();
        var reached = new List<Type>();
        var queue = new Queue<Type>();
        queue.Enqueue(root);

        while (queue.Count > 0)
        {
            foreach (var type in Unwrap(queue.Dequeue()))
            {
                if (!seen.Add(type))
                {
                    continue;
                }

                reached.Add(type);

                if (type.IsPrimitive || type.IsEnum || type == typeof(string))
                {
                    continue;
                }

                foreach (var member in Members(type))
                {
                    queue.Enqueue(member);
                }
            }
        }

        return reached;
    }

    /// <summary>Reduces a type to the types actually serialised in its place.</summary>
    /// <remarks>
    /// A nullable is its underlying type; an array or a sequence is its element type — and both, when
    /// the sequence itself is a declared type such as a paged envelope, because a payload can be both a
    /// collection and a shape of its own.
    /// </remarks>
    private static IEnumerable<Type> Unwrap(Type type)
    {
        if (Nullable.GetUnderlyingType(type) is { } underlying)
        {
            yield return underlying;
            yield break;
        }

        if (type.IsArray)
        {
            if (type.GetElementType() is { } element)
            {
                yield return element;
            }

            yield break;
        }

        if (type.IsGenericType)
        {
            foreach (var argument in type.GetGenericArguments())
            {
                yield return argument;
            }

            if (!typeof(IEnumerable).IsAssignableFrom(type))
            {
                yield return type;
            }

            yield break;
        }

        yield return type;
    }

    private static IEnumerable<Type> Members(Type type)
    {
        foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(property => property.GetIndexParameters().Length == 0))
        {
            yield return property.PropertyType;
        }

        // Constructor parameters as well as properties, because a positional record's parameters are
        // what the deserialiser binds, and a payload could carry one that no property exposes.
        foreach (var parameter in type.GetConstructors()
            .SelectMany(constructor => constructor.GetParameters()))
        {
            yield return parameter.ParameterType;
        }
    }
}

/// <summary>Where a published type is declared, from the point of view of ARCH-013.</summary>
public enum PayloadOrigin
{
    /// <summary>An <c>Api</c> project or the web host: the only place a payload may be declared.</summary>
    PublishedApi,

    /// <summary>A framework or runtime type. Outside the rule.</summary>
    Framework,

    /// <summary>A platform value object that exists to be serialised, from the closed allowlist.</summary>
    SerialisablePlatform,

    /// <summary>A module's <c>Domain</c> project. Never publishable.</summary>
    Domain,

    /// <summary>A module's <c>Contracts</c> project: for module-to-module use, not for HTTP.</summary>
    ModuleContracts,

    /// <summary>An <c>Application</c>, <c>Infrastructure</c> or platform type that is not on the list.</summary>
    InternalLayer,
}
