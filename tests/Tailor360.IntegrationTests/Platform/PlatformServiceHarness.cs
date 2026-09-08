using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Tailor360.Platform.Abstractions.Auditing;
using Tailor360.Platform.Abstractions.Events;
using Tailor360.Platform.Abstractions.Identifiers;
using Tailor360.Platform.Abstractions.Time;
using Tailor360.Platform.Persistence.Contexts;
using Tailor360.Platform.Persistence.Conventions;
using Tailor360.Platform.Persistence.Migrating;
using Tailor360.Platform.Persistence.Outbox;

namespace Tailor360.IntegrationTests.Platform;

/// <summary>
/// Wires the platform services against one test database. Building the real graph rather than mocking
/// it is the point: the behaviours under test are the ones that only appear when Entity Framework, the
/// connection pool and PostgreSQL are all in play.
/// </summary>
public static class PlatformServiceHarness
{
    /// <summary>Builds a provider for the database the supplied context is attached to.</summary>
    public static ServiceProvider Build(
        PlatformDbContext context,
        Action<IServiceCollection>? configure = null)
    {
        var connectionString = context.Database.GetConnectionString()!;
        var services = new ServiceCollection();

        services.AddLogging(logging => logging.SetMinimumLevel(LogLevel.Warning));
        services.AddSingleton<IClock, SystemClock>();
        services.AddSingleton<IIdGenerator, UuidV7IdGenerator>();
        services.AddScoped<IAuditContext, SystemAuditContext>();
        services.AddScoped<IOutboxCorrelation, NullOutboxCorrelation>();
        services.AddSingleton(Options.Create(new OutboxOptions()));
        services.AddSingleton<OutboxDispatcher>();

        services.AddDbContext<PlatformDbContext>(builder => builder
            .UseNpgsql(connectionString, npgsql => npgsql.MigrationsHistoryTable(
                ModuleDbContext.MigrationsHistoryTable, PlatformDbContext.SchemaName))
            .UseSnakeCaseNamingConvention());

        // The publisher is bound to one context, and this harness composes one: the platform's own.
        // A test publishing here writes to platform.outbox_messages and could not reach another
        // module's if it tried, which is the property #77 was about.
        services.AddScoped<ModuleEventPublisher<PlatformDbContext>>();
        services.AddScoped<IEventPublisher>(
            provider => provider.GetRequiredService<ModuleEventPublisher<PlatformDbContext>>());

        services.AddSingleton(new ModuleContextRegistry(
            [new ModuleContextRegistration(
                typeof(PlatformDbContext),
                PlatformDbContext.SchemaName,
                ModuleContextRegistry.PlatformOrder)]));

        configure?.Invoke(services);

        return services.BuildServiceProvider();
    }
}

/// <summary>A handler that records what it was given, so a test can assert on delivery.</summary>
/// <param name="eventType">The event type it consumes.</param>
/// <param name="handlerName">Its inbox name.</param>
/// <param name="schema">The module whose inbox records it. The platform's, in this harness.</param>
public sealed class RecordingHandler(
    string eventType,
    string handlerName,
    string schema = PlatformDbContext.SchemaName)
    : IOutboxMessageHandler
{
    private readonly List<OutboxDelivery> _deliveries = [];
    private readonly Lock _gate = new();

    /// <inheritdoc />
    public string EventType { get; } = eventType;

    /// <inheritdoc />
    public string HandlerName { get; } = handlerName;

    /// <inheritdoc />
    public string Schema { get; } = schema;

    /// <summary>Set to make the handler throw, so failure paths can be exercised.</summary>
    public bool ThrowOnHandle { get; set; }

    /// <summary>Everything the handler was asked to deliver, in order.</summary>
    public IReadOnlyList<OutboxDelivery> Deliveries
    {
        get
        {
            lock (_gate)
            {
                return [.. _deliveries];
            }
        }
    }

    /// <inheritdoc />
    public Task HandleAsync(OutboxDelivery delivery, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            _deliveries.Add(delivery);
        }

        return ThrowOnHandle
            ? throw new InvalidOperationException("The handler was told to fail.")
            : Task.CompletedTask;
    }
}
