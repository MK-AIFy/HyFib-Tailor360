using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Tailor360.Platform.Observability.Health;

/// <summary>
/// Binds the liveness watchdog's options. Both hosts call this; neither host's own hosted-service
/// registration is done here, because the worker must register its own <c>[WorkerJob]</c>-carrying
/// subclass rather than this project's own <see cref="LivenessWatchdogService"/> — see that class's remarks.
/// </summary>
public static class LivenessWatchdogServiceCollectionExtensions
{
    /// <summary>Binds <see cref="LivenessWatchdogOptions"/>. The caller still adds its own hosted-service registration.</summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configuration">Application configuration.</param>
    public static IServiceCollection AddTailor360LivenessWatchdogOptions(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddOptions<LivenessWatchdogOptions>()
            .Bind(configuration.GetSection(LivenessWatchdogOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        return services;
    }
}
