using Microsoft.EntityFrameworkCore;

namespace Tailor360.Platform.Persistence.Migrating;

/// <summary>
/// The ordered list of module contexts to migrate. The order is fixed rather than discovered, because
/// a run that applied schemas in a different order on two machines would be impossible to reason about:
/// <c>platform</c> first because everything depends on the outbox and the audit trail, then
/// <c>identity</c> because users and branches are referenced everywhere, then the remaining modules in
/// alphabetical order so the sequence is stable and reviewable.
/// </summary>
public sealed class ModuleContextRegistry
{
    private readonly List<ModuleContextRegistration> _registrations = [];

    /// <summary>Creates an empty registry, for a caller that adds its contexts by hand.</summary>
    public ModuleContextRegistry()
    {
    }

    /// <summary>
    /// Composes the registry from the registrations each module contributed to the container. The
    /// registry is built this way rather than listing the contexts in one place because a module
    /// registers itself: the platform's registration runs before any module's, so a closed list built
    /// there could never include them, and a module whose context was missing from the migration order
    /// would fail at the first request rather than at the build.
    /// </summary>
    /// <param name="registrations">Every context registered by the platform and by the modules.</param>
    public ModuleContextRegistry(IEnumerable<ModuleContextRegistration> registrations)
    {
        ArgumentNullException.ThrowIfNull(registrations);

        foreach (var registration in registrations)
        {
            Add(registration);
        }
    }

    /// <summary>The registered contexts in migration order.</summary>
    public IReadOnlyList<ModuleContextRegistration> Registrations =>
        [.. _registrations.OrderBy(r => r.Order).ThenBy(r => r.Schema, StringComparer.Ordinal)];

    /// <summary>Registers a module context.</summary>
    /// <typeparam name="TContext">The context type.</typeparam>
    /// <param name="schema">The schema it owns.</param>
    /// <param name="order">
    /// Migration order. Platform is 0 and Identity is 100; every other module uses 1000 so that they
    /// are applied alphabetically among themselves.
    /// </param>
    public ModuleContextRegistry Add<TContext>(string schema, int order = DefaultModuleOrder)
        where TContext : DbContext
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(schema);
        return Add(new ModuleContextRegistration(typeof(TContext), schema, order));
    }

    /// <summary>
    /// Registers a module context. Registering the same context for the same schema twice is a no-op,
    /// because a host that composes a module twice is doing something harmless; a schema claimed by two
    /// different contexts, or a context claiming two schemas, is the ownership rule being broken and
    /// fails immediately.
    /// </summary>
    /// <param name="registration">The context, its schema and its place in the order.</param>
    public ModuleContextRegistry Add(ModuleContextRegistration registration)
    {
        ArgumentNullException.ThrowIfNull(registration);
        ArgumentException.ThrowIfNullOrWhiteSpace(registration.Schema);

        if (_registrations.Contains(registration))
        {
            return this;
        }

        var sameContext = _registrations.FirstOrDefault(r => r.ContextType == registration.ContextType);
        if (sameContext is not null)
        {
            throw new InvalidOperationException(
                $"The context {registration.ContextType.Name} is already registered for schema " +
                $"'{sameContext.Schema}' and cannot also own '{registration.Schema}'.");
        }

        var sameSchema = _registrations.FirstOrDefault(
            r => string.Equals(r.Schema, registration.Schema, StringComparison.Ordinal));
        if (sameSchema is not null)
        {
            throw new InvalidOperationException(
                $"Schema '{registration.Schema}' is already owned by {sameSchema.ContextType.Name}. " +
                "One module owns one schema (ARCH-005).");
        }

        _registrations.Add(registration);
        return this;
    }

    /// <summary>The order value for the platform context.</summary>
    public const int PlatformOrder = 0;

    /// <summary>The order value for the identity context.</summary>
    public const int IdentityOrder = 100;

    /// <summary>The order value every other module uses.</summary>
    public const int DefaultModuleOrder = 1000;
}

/// <summary>One registered module context.</summary>
/// <param name="ContextType">The context type.</param>
/// <param name="Schema">The schema it owns.</param>
/// <param name="Order">Its position in the migration order.</param>
public sealed record ModuleContextRegistration(Type ContextType, string Schema, int Order);
