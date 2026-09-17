using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Tailor360.Modules.Integration.Infrastructure.Documents;
using Tailor360.Modules.Integration.Infrastructure.Storage;
using Tailor360.Platform.Abstractions.Health;
using Tailor360.Platform.Abstractions.Ports;
using Tailor360.Platform.Abstractions.Time;

namespace Tailor360.Modules.Integration.Infrastructure;

/// <summary>
/// Registers the Integration module: the integration event relay, webhooks, provider adapters and the accounting export.
/// Hosts compose the application only through extensions like this one (architecture rule ARCH-006),
/// so a host never depends on a module's internal types.
/// </summary>
public static class IntegrationModuleServiceCollectionExtensions
{
    /// <summary>The database schema this module owns. No other module may map a table in it.</summary>
    public const string SchemaName = "integration";

    /// <summary>Registers the module's services, options and persistence.</summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configuration">Application configuration.</param>
    public static IServiceCollection AddIntegrationModule(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        // The adapters behind the platform ports (ADR-0012, ADR-0014): the SDKs they wrap are referenced by
        // this project alone (ARCH-009). Object storage is the real adapter when an endpoint is configured
        // and the collecting one otherwise — the test host and a developer without the compose stack — and
        // the environment guard refuses a production start without an endpoint.
        services.AddOptions<ObjectStorageOptions>()
            .Bind(configuration.GetSection(ObjectStorageOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();
        services.AddSingleton<IValidateOptions<ObjectStorageOptions>, ObjectStorageOptionsValidator>();
        services.TryAddSingleton<InMemoryObjectStorage>();

        // ResilientObjectStorage wraps whichever adapter was selected with the bulkhead, the circuit
        // breaker and the per-call timeout of docs/architecture/resilience-policies.md. It is registered
        // as itself, with IObjectStorage resolving to that same instance, the same double registration
        // HeartbeatService uses for IHeartbeatMonitor — so that ObjectStorageHealthCheck reads the one
        // breaker every caller's calls actually go through, not a second instance of its own.
        services.TryAddSingleton(provider =>
        {
            var options = provider.GetRequiredService<IOptions<ObjectStorageOptions>>();
            IObjectStorage inner;
            if (options.Value.IsConfigured && options.Value.HasCredentials)
            {
                inner = new MinioObjectStorage(options);
            }
            else
            {
                // Development only: the validator has refused every other environment by now.
                provider.GetRequiredService<ILogger<InMemoryObjectStorage>>().LogWarning(
                    "Object storage is not configured with an endpoint and both keys; the in-memory adapter serves this process alone, and a document rendered by the worker cannot be downloaded from the web host.");
                inner = provider.GetRequiredService<InMemoryObjectStorage>();
            }

            return new ResilientObjectStorage(
                inner,
                options,
                provider.GetRequiredService<IClock>(),
                provider.GetRequiredService<ILogger<ResilientObjectStorage>>());
        });
        services.TryAddSingleton<IObjectStorage>(provider => provider.GetRequiredService<ResilientObjectStorage>());

        services.AddHealthChecks()
            .AddCheck<ObjectStorageHealthCheck>("object-storage", tags: [HealthCheckTags.NonEssential]);

        services.TryAddSingleton<IPdfRenderer, QuestPdfRenderer>();
        services.TryAddSingleton<IBarcodeRenderer, Code128BarcodeRenderer>();

        // IPrintQueue is registered by Platform (DatabasePrintQueue, E07-F01-5): platform.print_jobs is
        // Platform's own table (ARCH-005), and the interim LoggingPrintQueue this module used to bind is
        // gone.

        return services;
    }
}
