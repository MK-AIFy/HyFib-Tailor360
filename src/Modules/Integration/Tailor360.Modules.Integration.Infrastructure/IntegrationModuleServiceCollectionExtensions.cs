using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Tailor360.Modules.Integration.Infrastructure.Documents;
using Tailor360.Modules.Integration.Infrastructure.Printing;
using Tailor360.Modules.Integration.Infrastructure.Storage;
using Tailor360.Platform.Abstractions.Ports;

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
        services.TryAddSingleton<IObjectStorage>(provider =>
        {
            var options = provider.GetRequiredService<IOptions<ObjectStorageOptions>>();
            if (options.Value.IsConfigured && options.Value.HasCredentials)
            {
                return new MinioObjectStorage(options);
            }

            // Development only: the validator has refused every other environment by now.
            provider.GetRequiredService<ILogger<InMemoryObjectStorage>>().LogWarning(
                "Object storage is not configured with an endpoint and both keys; the in-memory adapter serves this process alone, and a document rendered by the worker cannot be downloaded from the web host.");
            return provider.GetRequiredService<InMemoryObjectStorage>();
        });
        services.TryAddSingleton<IPdfRenderer, QuestPdfRenderer>();
        services.TryAddSingleton<IBarcodeRenderer, Code128BarcodeRenderer>();
        services.TryAddScoped<IPrintQueue, LoggingPrintQueue>();

        return services;
    }
}
