using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Tailor360.Modules.Media.Application;
using Tailor360.Platform.Security.Permissions;

namespace Tailor360.Modules.Media.Infrastructure;

/// <summary>
/// Registers the Media module: the upload pipeline, object storage, authorised delivery and retention of images.
/// Hosts compose the application only through extensions like this one (architecture rule ARCH-006),
/// so a host never depends on a module's internal types.
/// </summary>
public static class MediaModuleServiceCollectionExtensions
{
    /// <summary>The database schema this module owns. No other module may map a table in it.</summary>
    public const string SchemaName = "media";

    /// <summary>Registers the module's services, options and persistence.</summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configuration">Application configuration.</param>
    public static IServiceCollection AddMediaModule(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddSingleton<IPermissionSource, MediaPermissions>();

        return services;
    }
}
