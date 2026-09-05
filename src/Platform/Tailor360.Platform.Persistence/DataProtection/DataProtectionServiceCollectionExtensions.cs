using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Tailor360.Platform.Abstractions.Health;
using Tailor360.Platform.Persistence.Contexts;

namespace Tailor360.Platform.Persistence.DataProtection;

/// <summary>Persists the data-protection key ring in the database rather than on a container's disk.</summary>
public static class DataProtectionServiceCollectionExtensions
{
    /// <summary>
    /// Registers data protection against <c>platform.data_protection_keys</c>, names the application so
    /// that every host in the deployment shares one ring, and adds the startup check that refuses to
    /// serve when the ring cannot be read.
    /// </summary>
    /// <remarks>
    /// Call this from any host that issues anti-forgery tokens or protects a payload. Without it the
    /// framework falls back to a per-container key ring, which produces the worst kind of fault: the
    /// application works perfectly until it is restarted or scaled, and then rejects tokens it issued
    /// itself.
    /// <para>
    /// The values are read from configuration directly as well as being bound as validated options.
    /// The data-protection builder wants them at registration time, before any provider exists; the
    /// bound options are what make a mistyped value fail on start rather than at the first protected
    /// write.
    /// </para>
    /// </remarks>
    /// <param name="services">The service collection.</param>
    /// <param name="configuration">Application configuration.</param>
    public static IServiceCollection AddTailor360DataProtection(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddOptions<DataProtectionOptions>()
            .BindConfiguration(DataProtectionOptions.SectionName)
            .ValidateDataAnnotations()
            .ValidateOnStart();

        var options = new DataProtectionOptions();
        configuration.GetSection(DataProtectionOptions.SectionName).Bind(options);

        services.AddDataProtection()
            .SetApplicationName(options.ApplicationName)
            .SetDefaultKeyLifetime(options.KeyLifetime)
            .PersistKeysToDbContext<PlatformDbContext>();

        services.AddHealthChecks()
            .AddCheck<DataProtectionKeyRingHealthCheck>(
                "data-protection", tags: [HealthCheckTags.Startup]);

        return services;
    }
}
