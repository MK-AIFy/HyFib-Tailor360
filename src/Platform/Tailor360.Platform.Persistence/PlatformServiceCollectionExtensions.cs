using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Tailor360.Platform.Abstractions.Auditing;
using Tailor360.Platform.Abstractions.Events;
using Tailor360.Platform.Abstractions.FeatureFlags;
using Tailor360.Platform.Abstractions.Health;
using Tailor360.Platform.Abstractions.Idempotency;
using Tailor360.Platform.Abstractions.Identifiers;
using Tailor360.Platform.Abstractions.Outbox;
using Tailor360.Platform.Abstractions.Sequencing;
using Tailor360.Platform.Abstractions.Time;
using Tailor360.Platform.Persistence.Auditing;
using Tailor360.Platform.Persistence.Contexts;
using Tailor360.Platform.Persistence.Conventions;
using Tailor360.Platform.Persistence.FeatureFlags;
using Tailor360.Platform.Persistence.Health;
using Tailor360.Platform.Persistence.Idempotency;
using Tailor360.Platform.Persistence.Migrating;
using Tailor360.Platform.Persistence.Outbox;
using Tailor360.Platform.Persistence.Scheduling;
using Tailor360.Platform.Persistence.Sequencing;

namespace Tailor360.Platform.Persistence;

/// <summary>Registers the platform runtime services every host and every module depends on.</summary>
public static class PlatformServiceCollectionExtensions
{
    /// <summary>Registers the clock and the identifier generator, with no database dependency.</summary>
    public static IServiceCollection AddTailor360Core(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddSingleton<IClock, SystemClock>();
        services.TryAddSingleton<IIdGenerator, UuidV7IdGenerator>();

        return services;
    }

    /// <summary>
    /// Declares that a module context takes part in migrations, and where in the order it belongs.
    /// </summary>
    /// <remarks>
    /// Each module contributes its own context here, and the registry is composed from what the
    /// container holds. A module that adds a context and forgets this call has a schema no migration run
    /// will ever create, which the startup migration check turns into a refusal to serve rather than
    /// into a runtime error on the first query.
    /// </remarks>
    /// <typeparam name="TContext">The module's context.</typeparam>
    /// <param name="services">The service collection.</param>
    /// <param name="schema">The schema the module owns.</param>
    /// <param name="order">
    /// Migration order. Platform is 0 and Identity is 100; every other module leaves the default so
    /// that they are applied alphabetically among themselves.
    /// </param>
    public static IServiceCollection AddModuleContext<TContext>(
        this IServiceCollection services,
        string schema,
        int order = ModuleContextRegistry.DefaultModuleOrder)
        where TContext : DbContext
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(schema);

        services.AddSingleton(new ModuleContextRegistration(typeof(TContext), schema, order));
        services.TryAddSingleton(provider =>
            new ModuleContextRegistry(provider.GetServices<ModuleContextRegistration>()));

        return services;
    }

    /// <summary>
    /// Registers a module's own event publisher, over its own context and therefore over its own
    /// outbox.
    /// </summary>
    /// <remarks>
    /// Registered as the concrete <see cref="ModuleEventPublisher{TContext}"/> rather than as
    /// <c>IEventPublisher</c>, because that interface is shared and the container holds one binding for
    /// it. The module binds its own port to this type, which is what keeps a module's events in the
    /// module's own schema (#77).
    /// </remarks>
    /// <typeparam name="TContext">The module's context.</typeparam>
    /// <param name="services">The service collection.</param>
    /// <returns>The service collection.</returns>
    public static IServiceCollection AddModuleOutbox<TContext>(this IServiceCollection services)
        where TContext : ModuleDbContext
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddScoped<IOutboxCorrelation, NullOutboxCorrelation>();
        services.TryAddScoped<ModuleEventPublisher<TContext>>();

        return services;
    }

    /// <summary>
    /// Registers the platform database context and everything built on it: migrations, the audit
    /// writer, document sequences, idempotency, feature flags and the outbox.
    /// </summary>
    public static IServiceCollection AddTailor360Platform(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddTailor360Core();

        services.AddOptions<DatabaseOptions>()
            .BindConfiguration(DatabaseOptions.SectionName)
            .ValidateDataAnnotations()
            .Validate(
                options => options.BudgetFits,
                "The configured connection pools exceed the database server's capacity. Reduce " +
                "Database:MaxPoolSize or Database:ExpectedProcessCount, or raise the server's " +
                "max_connections. Discovering this under load instead is an outage.")
            .ValidateOnStart();

        services.AddOptions<IdempotencyOptions>()
            .BindConfiguration(IdempotencyOptions.SectionName)
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddOptions<FeatureFlagOptions>()
            .BindConfiguration(FeatureFlagOptions.SectionName)
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddOptions<OutboxOptions>()
            .BindConfiguration(OutboxOptions.SectionName)
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddDbContext<PlatformDbContext>((provider, builder) =>
        {
            var options = provider.GetRequiredService<IOptions<DatabaseOptions>>().Value;

            builder.UseNpgsql(options.BuildPooledConnectionString(), npgsql =>
            {
                npgsql.MigrationsHistoryTable(
                    ModuleDbContext.MigrationsHistoryTable, PlatformDbContext.SchemaName);
                npgsql.CommandTimeout(options.CommandTimeoutSeconds);

                // Retrying on a transient failure is right for reads and for the dispatcher, but the
                // execution strategy must not silently retry a user-visible command; call sites that
                // own a transaction opt out explicitly.
                npgsql.EnableRetryOnFailure(3, TimeSpan.FromSeconds(2), null);
            })
            .UseSnakeCaseNamingConvention();
        });

        services.AddModuleContext<PlatformDbContext>(
            PlatformDbContext.SchemaName, ModuleContextRegistry.PlatformOrder);

        services.TryAddScoped<MigrationRunner>();
        services.TryAddScoped<JobLeaseService>();

        services.TryAddScoped<IAuditContext, SystemAuditContext>();
        services.TryAddScoped<IAuditWriter, AuditWriter>();
        services.TryAddScoped<ISequenceAllocator, SequenceAllocator>();
        services.TryAddScoped<IIdempotencyStore, IdempotencyStore>();
        services.TryAddScoped<IdempotencyStore>();

        services.TryAddSingleton<FeatureFlagStore>();
        services.TryAddSingleton<IFeatureFlags>(sp => sp.GetRequiredService<FeatureFlagStore>());

        // Scoped, not singleton: administration reads and writes through the request's own context,
        // whereas evaluation answers from a snapshot the singleton holds.
        services.TryAddScoped<IFeatureFlagAdministration, FeatureFlagAdministration>();
        services.TryAddScoped<IAuditReader, AuditReader>();
        services.TryAddScoped<IOutboxAdministration, OutboxAdministration>();

        services.TryAddScoped<IOutboxCorrelation, NullOutboxCorrelation>();
        services.TryAddSingleton<OutboxDispatcher>();

        // There is deliberately no platform-wide IEventPublisher. It is one interface and the web host
        // composes every module at once, so a single registration would leave whichever module
        // registered last writing every module's events — into its own schema. A module registers its
        // own publisher over its own context, behind a port its Application project declares (#77).
        services.AddModuleOutbox<PlatformDbContext>();

        services.AddHealthChecks()
            .AddCheck<DatabaseHealthCheck>("database", tags: [HealthCheckTags.Ready])
            .AddCheck<MigrationStateHealthCheck>("migrations", tags: [HealthCheckTags.Startup])
            .AddCheck<OutboxBacklogHealthCheck>("outbox", tags: [HealthCheckTags.NonEssential])
            .AddCheck<AuditPartitionHealthCheck>("audit-partitions", tags: [HealthCheckTags.NonEssential]);

        return services;
    }

}
