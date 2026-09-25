using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Tailor360.Modules.Orders.Application.Drafts;
using Tailor360.Modules.Orders.Infrastructure.Persistence;
using Tailor360.Platform.Persistence;
using Tailor360.Platform.Persistence.Conventions;
using Tailor360.Platform.Persistence.Migrating;
using Tailor360.Platform.Security.Authorisation;

namespace Tailor360.Modules.Orders.Infrastructure;

/// <summary>
/// Registers the Orders module: estimates, orders, garment jobs, snapshots, the production workflow, QC and alterations.
/// Hosts compose the application only through extensions like this one (architecture rule ARCH-006),
/// so a host never depends on a module's internal types.
/// </summary>
public static class OrdersModuleServiceCollectionExtensions
{
    /// <summary>The database schema this module owns. No other module may map a table in it.</summary>
    public const string SchemaName = OrdersDbContext.SchemaName;

    /// <summary>Registers the module's services, options and persistence.</summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configuration">Application configuration.</param>
    public static IServiceCollection AddOrdersModule(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddDbContext<OrdersDbContext>((provider, builder) =>
        {
            var options = provider.GetRequiredService<IOptions<DatabaseOptions>>().Value;
            builder.UseNpgsql(options.BuildPooledConnectionString(), npgsql =>
            {
                npgsql.MigrationsHistoryTable(ModuleDbContext.MigrationsHistoryTable, SchemaName);
                npgsql.CommandTimeout(options.CommandTimeoutSeconds);
                npgsql.EnableRetryOnFailure(3, TimeSpan.FromSeconds(2), null);
            }).UseSnakeCaseNamingConvention();
        });

        services.AddModuleContext<OrdersDbContext>(SchemaName);
        services.AddModuleOutbox<OrdersDbContext>();
        services.TryAddScoped<IOrderDraftStore, OrderDraftStore>();
        services.TryAddScoped<OrderDraftHandler>();
        services.AddScoped<IResourceScopeResolver, OrderDraftScopeResolver>();
        return services;
    }
}
