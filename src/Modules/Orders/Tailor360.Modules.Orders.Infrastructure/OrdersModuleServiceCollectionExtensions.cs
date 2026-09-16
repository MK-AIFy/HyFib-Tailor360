using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Tailor360.Modules.Orders.Application.Abstractions;
using Tailor360.Modules.Orders.Application.Drafts;
using Tailor360.Modules.Orders.Application.Options;
using Tailor360.Modules.Orders.Application.Workflows;
using Tailor360.Modules.Orders.Contracts.Orders;
using Tailor360.Modules.Orders.Infrastructure.Drafts;
using Tailor360.Modules.Orders.Infrastructure.Orders;
using Tailor360.Modules.Orders.Infrastructure.Persistence;
using Tailor360.Platform.Persistence;
using Tailor360.Platform.Persistence.Conventions;
using Tailor360.Platform.Persistence.Migrating;
using Tailor360.Platform.Security.Authorisation;

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
/// <strong>The module's first <c>IResourceScopeResolver</c> arrives here</strong>, for the order draft
/// (#199): a resource scope is what ARCH-023 requires of a branch-scoped permissioned endpoint that
/// carries a route parameter, and the draft is the first such route this module publishes. There is no
/// <c>IPermissionSource</c> here and there never will be one for this module specifically — permission
/// keys are not module-owned in this repository. <c>ApplicationPermissions</c> composes the whole
/// catalogue centrally from every module's constants at once, and <c>OrdersPermissions.All</c> has been
/// part of that composition, and readable, since the permission model shipped (#24); registering a
/// second <c>IPermissionSource</c> here would not fill a gap — it would throw at start-up, because
/// <c>PermissionCatalogue</c>'s constructor rejects a key two sources both declare, and
/// <c>orders.intake</c> and <c>orders.read</c> are declared by <c>ApplicationPermissions</c> already.
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

        // Workflow definitions (#232): a fourth store over the same context and therefore the same unit of
        // work, though nothing here yet asks it to commit alongside a draft, an estimate or an order — this
        // slice has no command that touches more than one of the four in a single save.
        services.TryAddScoped<IWorkflowDefinitionStore, WorkflowDefinitionStore>();

        // The drafting surface over it (#243): list and create a definition, read a version, replace a
        // draft version's whole graph, and start a new draft by cloning one.
        services.TryAddScoped<WorkflowDefinitionHandler>();

        // The order draft lifecycle (#199): the module's first resource-scoped route. The window is
        // documented as branch configuration (glossary.md section 4); no branch-configuration surface
        // exists yet, so this is the module-level binding the application uses until one does, with the
        // documented default bound from OrderDraft.DefaultLifetime rather than restated as a literal.
        services.AddOptions<OrdersDraftOptions>()
            .Bind(configuration.GetSection(OrdersDraftOptions.SectionName))
            .ValidateDataAnnotations()
            .Validate(
                draftOptions => draftOptions.IsLifetimeUsable,
                "Orders:Draft:DraftLifetime must be between one hour and thirty days.")
            .ValidateOnStart();

        services.TryAddScoped<OrderDraftHandler>();

        // Enumerable, not TryAdd: a second module's resolver, for a resource kind of its own, must join
        // this list rather than replace the draft's.
        services.AddScoped<IResourceScopeResolver, OrderDraftScopeResolver>();

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
