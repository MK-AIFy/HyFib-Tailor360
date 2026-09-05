using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Tailor360.Modules.Identity.Application;
using Tailor360.Platform.Security.Permissions;

namespace Tailor360.Modules.Identity.Infrastructure;

/// <summary>
/// Registers the Identity module: users, roles, permissions, branch assignments, sessions and multi-factor authentication.
/// Hosts compose the application only through extensions like this one (architecture rule ARCH-006),
/// so a host never depends on a module's internal types.
/// </summary>
public static class IdentityModuleServiceCollectionExtensions
{
    /// <summary>The database schema this module owns. No other module may map a table in it.</summary>
    public const string SchemaName = "identity";

    /// <summary>Registers the module's services, options and persistence.</summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configuration">Application configuration.</param>
    public static IServiceCollection AddIdentityModule(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddSingleton<IPermissionSource, IdentityPermissions>();

        return services;
    }
}
