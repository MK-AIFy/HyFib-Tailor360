using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Tailor360.Modules.Integration.Application;
using Tailor360.Platform.Security.Permissions;

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

        services.AddSingleton<IPermissionSource, IntegrationPermissions>();

        return services;
    }
}
