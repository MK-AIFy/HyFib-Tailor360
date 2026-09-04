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

            builder.UseNpgsql(BuildConnectionString(options), npgsql =>
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

        services.TryAddSingleton(_ => new ModuleContextRegistry()
            .Add<PlatformDbContext>(PlatformDbContext.SchemaName, ModuleContextRegistry.PlatformOrder));

        services.TryAddScoped<MigrationRunner>();
        services.TryAddScoped<JobLeaseService>();

        services.TryAddScoped<IAuditContext, SystemAuditContext>();
        services.TryAddScoped<IAuditWriter, AuditWriter>();
        services.TryAddScoped<ISequenceAllocator, SequenceAllocator>();
        services.TryAddScoped<IIdempotencyStore, IdempotencyStore>();
        services.TryAddScoped<IdempotencyStore>();

        services.TryAddSingleton<FeatureFlagStore>();
        services.TryAddSingleton<IFeatureFlags>(sp => sp.GetRequiredService<FeatureFlagStore>());

        services.TryAddScoped<IOutboxCorrelation, NullOutboxCorrelation>();
        services.TryAddScoped<IEventPublisher, OutboxWriter>();
        services.TryAddSingleton<OutboxDispatcher>();

        services.AddHealthChecks()
            .AddCheck<DatabaseHealthCheck>("database", tags: [HealthCheckTags.Ready])
            .AddCheck<MigrationStateHealthCheck>("migrations", tags: [HealthCheckTags.Startup])
            .AddCheck<OutboxBacklogHealthCheck>("outbox", tags: [HealthCheckTags.NonEssential])
            .AddCheck<AuditPartitionHealthCheck>("audit-partitions", tags: [HealthCheckTags.NonEssential]);

        return services;
    }

    /// <summary>
    /// Builds the connection string with the pool size the budget check validated, so the value that was
    /// checked is the value that is used.
    /// </summary>
    private static string BuildConnectionString(DatabaseOptions options)
    {
        var builder = new Npgsql.NpgsqlConnectionStringBuilder(options.ConnectionString)
        {
            MaxPoolSize = options.MaxPoolSize,
            Pooling = true,
        };

        return builder.ConnectionString;
    }
}
