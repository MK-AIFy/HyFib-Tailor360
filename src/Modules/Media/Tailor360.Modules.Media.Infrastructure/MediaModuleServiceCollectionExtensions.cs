using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Tailor360.Modules.Media.Application.Abstractions;
using Tailor360.Modules.Media.Application.Upload;
using Tailor360.Modules.Media.Infrastructure.Persistence;
using Tailor360.Platform.Persistence;
using Tailor360.Platform.Persistence.Conventions;
using Tailor360.Platform.Persistence.Migrating;
using Tailor360.Platform.Persistence.Outbox;

namespace Tailor360.Modules.Media.Infrastructure;

/// <summary>
/// Registers the Media module: the upload pipeline, object storage, authorised delivery and retention of images.
/// Hosts compose the application only through extensions like this one (architecture rule ARCH-006),
/// so a host never depends on a module's internal types.
/// </summary>
public static class MediaModuleServiceCollectionExtensions
{
    /// <summary>The database schema this module owns. No other module may map a table in it.</summary>
    public const string SchemaName = MediaDbContext.SchemaName;

    /// <summary>Registers the module's services, options and persistence.</summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configuration">Application configuration.</param>
    public static IServiceCollection AddMediaModule(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        AddPersistence(services);

        // The outbox/inbox tables exist from this issue's own migration (module-ownership.md §3's
        // "first issue" convention); registering the dispatcher wiring now means #593 only has to add
        // IMediaEventPublisher itself, not this line too.
        services.AddModuleOutbox<MediaDbContext>();

        services.AddOptions<MediaUploadOptions>()
            .Bind(configuration.GetSection(MediaUploadOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.TryAddScoped<IMediaStore, MediaStore>();
        services.TryAddScoped<UploadMediaHandler>();

        return services;
    }

    private static void AddPersistence(IServiceCollection services)
    {
        services.AddDbContext<MediaDbContext>((provider, builder) =>
        {
            var options = provider.GetRequiredService<IOptions<DatabaseOptions>>().Value;

            builder.UseNpgsql(options.BuildPooledConnectionString(), npgsql =>
            {
                npgsql.MigrationsHistoryTable(
                    ModuleDbContext.MigrationsHistoryTable, MediaDbContext.SchemaName);
                npgsql.CommandTimeout(options.CommandTimeoutSeconds);
                npgsql.EnableRetryOnFailure(3, TimeSpan.FromSeconds(2), null);
            })
            .UseSnakeCaseNamingConvention();
        });

        services.AddModuleContext<MediaDbContext>(MediaDbContext.SchemaName);
    }
}
