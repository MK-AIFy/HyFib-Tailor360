using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Tailor360.Modules.Orders.Application.Abstractions;
using Tailor360.Modules.Orders.Contracts.Orders;
using Tailor360.Modules.Orders.Infrastructure.Orders;
using Tailor360.Modules.Orders.Infrastructure.Persistence;
using Tailor360.Platform.Persistence;
using Tailor360.Platform.Persistence.Conventions;
using Tailor360.Platform.Persistence.Migrating;

namespace Tailor360.Modules.Orders.Infrastructure;

/// <summary>
/// Composes the Orders module: order drafts, estimates, orders, garment jobs and their frozen snapshots.
/// Hosts compose the application only through extensions like this one (architecture rule ARCH-006),
/// so a host never depends on a module's internal types.
/// </summary>
/// <remarks>
/// <para>
/// Registering the context here is what puts the <c>orders</c> schema into the migration runner: both hosts
/// already call this method, and <c>AddModuleContext</c> is how the command-line tool learns there is a schema to
/// migrate at all.
/// </para>
/// <para>
/// <strong>No <c>IResourceScopeResolver</c> and no <c>IPermissionSource</c> in this change</strong>, and neither
/// is an oversight. A resource scope is what ARCH-023 requires of a branch-scoped permissioned endpoint that
/// carries a route parameter, and the permission keys are what those endpoints declare — both belong with the
/// endpoints, which are not in this change. Adding either now would mean registering a resolver for routes that
/// do not exist and a catalogue nothing reads.
/// </para>
/// <para>
/// <strong><c>IAlterationRequests</c> stays unregistered</strong> for the same reason: it is issue #34, and a
/// contract bound to an implementation that does not exist would let a consumer resolve it and fail on the first
/// call rather than at start-up.
/// </para>
/// <para>
/// <strong>No <c>ITimelineSource</c> either, and that one <em>is</em> a debt rather than a deferral.</strong>
/// <c>docs/architecture/module-ownership.md</c> section 5.5 says Orders publishes one, and
/// <see cref="Persistence.OrdersDbContext"/> already builds two indexes to serve it — <c>ix_estimates_customer</c>
/// and <c>ix_orders_customer</c> both carry a comment naming the timeline as their only reader. Until a source is
/// registered those two indexes serve no query, which the same file calls a fault elsewhere: "an index with no
/// query is a guess". Customers registers its own with <c>AddScoped</c> rather than <c>TryAdd</c> precisely so a
/// second module's source joins the list instead of replacing it, so the shape is ready and only the
/// implementation is missing. It is a customer-timeline feature and not a persistence one, which is why it is not
/// in this change; the indexes are kept rather than dropped because dropping and re-adding them is two migrations
/// for a query that is already specified.
/// </para>
/// </remarks>
public static class OrdersModuleServiceCollectionExtensions
{
    /// <summary>The database schema this module owns. No other module may map a table in it.</summary>
    public const string SchemaName = OrdersDbContext.SchemaName;

    /// <summary>Registers the module's services, options and persistence.</summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configuration">Application configuration.</param>
    /// <returns>The service collection.</returns>
    public static IServiceCollection AddOrdersModule(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        AddPersistence(services);

        // This module's own publisher, over this module's context and therefore its own outbox. The binding is
        // per module because IEventPublisher is one interface and every module is composed into one container
        // (#77) — which module's outbox_messages a published event lands in is decided here and nowhere else.
        services.AddModuleOutbox<OrdersDbContext>();
        services.TryAddScoped<IOrdersEventPublisher, OrdersEventPublisher>();

        // Three stores over one context, and therefore one unit of work: a confirmation consumes a draft,
        // converts an estimate and creates an order, and all three have to commit or none of them may.
        services.TryAddScoped<IOrderDraftStore, OrderDraftStore>();
        services.TryAddScoped<IEstimateStore, EstimateStore>();
        services.TryAddScoped<IOrderStore, OrderStore>();

        // The module's published surface, registered here rather than in each consuming module so that the only
        // way to reach an order fact is the contract the boundary allows (ARCH-004), and so a consumer that
        // forgot to compose this module fails to start rather than failing on the first call.
        services.TryAddScoped<IOrderSnapshotQuery, OrderSnapshotQuery>();

        return services;
    }

    /// <summary>Registers the context and tells the migration runner the schema exists.</summary>
    /// <param name="services">The service collection.</param>
    private static void AddPersistence(IServiceCollection services)
    {
        services.AddDbContext<OrdersDbContext>((provider, builder) =>
        {
            var options = provider.GetRequiredService<IOptions<DatabaseOptions>>().Value;

            builder.UseNpgsql(options.BuildPooledConnectionString(), npgsql =>
            {
                npgsql.MigrationsHistoryTable(ModuleDbContext.MigrationsHistoryTable, OrdersDbContext.SchemaName);
                npgsql.CommandTimeout(options.CommandTimeoutSeconds);
                npgsql.EnableRetryOnFailure(3, TimeSpan.FromSeconds(2), null);
            })
            .UseSnakeCaseNamingConvention();
        });

        // Default order: platform migrates first, then Identity, then everything else alphabetically.
        services.AddModuleContext<OrdersDbContext>(OrdersDbContext.SchemaName);
    }
}
