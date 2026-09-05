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

        if (_registrations.Any(r => r.ContextType == typeof(TContext)))
        {
            throw new InvalidOperationException($"The context {typeof(TContext).Name} is already registered.");
        }

        if (_registrations.Any(r => string.Equals(r.Schema, schema, StringComparison.Ordinal)))
        {
            throw new InvalidOperationException(
                $"Schema '{schema}' is already owned by another context. One module owns one schema (ARCH-005).");
        }

        _registrations.Add(new ModuleContextRegistration(typeof(TContext), schema, order));
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
