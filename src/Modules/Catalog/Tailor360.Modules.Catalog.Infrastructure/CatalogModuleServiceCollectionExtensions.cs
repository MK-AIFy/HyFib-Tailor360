using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Tailor360.Modules.Catalog.Application.Abstractions;
using Tailor360.Modules.Catalog.Application.Catalogue;
using Tailor360.Modules.Catalog.Application.Options;
using Tailor360.Modules.Catalog.Contracts.Catalogue;
using Tailor360.Modules.Catalog.Infrastructure.Catalogue;
using Tailor360.Modules.Catalog.Infrastructure.Persistence;
using Tailor360.Modules.Catalog.Infrastructure.Reconciliation;
using Tailor360.Platform.Persistence;
using Tailor360.Platform.Persistence.Conventions;
using Tailor360.Platform.Persistence.Migrating;
using Tailor360.Platform.Persistence.Outbox;
using Tailor360.Platform.Security.Authorisation;

namespace Tailor360.Modules.Catalog.Infrastructure;

/// <summary>
/// Composes the Catalog module: stitching categories, service types and the versions that hold them.
/// </summary>
/// <remarks>
/// <para>
/// The catalogue's own administration and picker routes declare
/// <see cref="Tailor360.Platform.Abstractions.Multitenancy.BranchScope.Organisation"/> or
/// <see cref="Tailor360.Platform.Abstractions.Multitenancy.BranchScope.CurrentBranch"/> with no
/// <c>IResourceScopeResolver</c> behind them, and that is a decision rather than an omission: a
/// catalogue version is organisation-wide configuration, and a category is offered at a <em>set</em> of
/// branches rather than owned by one, so branch availability is enforced where it means something — in
/// <c>ICatalogAvailabilityQuery</c>, which decides what a branch may order. A design selection draft
/// (issue #140) is the one row in this module that genuinely belongs to a single branch for its life, the
/// same way a measurement draft does in Customers, and it is the module's first and only registered
/// resolver.
/// </para>
/// <para>
/// The built-in validator is registered as one of the enumerable
/// <see cref="ICatalogDependencyValidator"/> and not as a special case, so that the module's own rules
/// run through exactly the mechanism it offers other modules. #27, #30, #33, #34 and #41 add theirs
/// beside it and none of them has to know this one exists.
/// </para>
/// </remarks>
public static class CatalogModuleServiceCollectionExtensions
{
    /// <summary>The database schema this module owns. No other module may map a table in it.</summary>
    public const string SchemaName = CatalogDbContext.SchemaName;

    /// <summary>Registers the module's services, options and persistence.</summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configuration">Application configuration.</param>
    /// <returns>The service collection.</returns>
    public static IServiceCollection AddCatalogModule(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        AddPersistence(services);

        services.AddModuleOutbox<CatalogDbContext>();
        services.TryAddScoped<ICatalogEventPublisher, CatalogEventPublisher>();

        services.TryAddScoped<ICatalogStore, CatalogStore>();
        services.TryAddScoped<CatalogPublicationCheck>();
        services.TryAddScoped<CatalogHandler>();
        services.TryAddScoped<ICatalogReferenceDataSeeder, CatalogReferenceDataSeeder>();

        // The reconciliation of INV-MTV-06 and every other cross-module reference (issue #91). Three
        // handlers because the race has two orderings and a third event heals it, and because the inbox
        // de-duplicates on a handler's name — one handler answering to three names would have no answer
        // to "did this delivery already run". Enumerable for the same reason the validators are.
        services.TryAddScoped<ICatalogReferenceBreachStore, CatalogReferenceBreachStore>();
        services.TryAddScoped<CatalogReconciler>();
        services.AddScoped<IOutboxMessageHandler, TemplateRetiredReconciliationHandler>();
        services.AddScoped<IOutboxMessageHandler, TemplatePublishedReconciliationHandler>();
        services.AddScoped<IOutboxMessageHandler, CatalogPublishedReconciliationHandler>();

        // Enumerable, not TryAdd: every module that owns something a service type links to adds its
        // own, and a second registration must join the list rather than replace this one.
        services.AddScoped<ICatalogDependencyValidator, BuiltInCatalogValidator>();
        services.AddScoped<ICatalogDependencyValidator, DesignRuleValidator>();
        services.TryAddScoped<IDesignSelectionValidator, DesignSelectionValidator>();

        // The design picker's own drafts (issue #140): a fourth aggregate beside the version, its own
        // store, and the module's first resource resolver.
        services.AddOptions<DesignSelectionDraftOptions>()
            .Bind(configuration.GetSection(DesignSelectionDraftOptions.SectionName))
            .ValidateDataAnnotations()
            .Validate(
                designOptions => designOptions.IsLifetimeUsable,
                "Catalog:DesignSelectionDraft:DraftLifetime must be between one hour and thirty days.")
            .ValidateOnStart();

        services.TryAddScoped<IDesignSelectionDraftStore, DesignSelectionDraftStore>();
        services.TryAddScoped<DesignSelectionDraftHandler>();
        services.TryAddScoped<IDesignSelectionQuery, DesignSelectionQuery>();
        services.AddScoped<IResourceScopeResolver, DesignSelectionDraftScopeResolver>();

        // A singleton, because the point of it is to survive the request that filled it. The
        // implementation is registered as itself as well as through the port, because the query reads
        // it and the handler only clears it.
        services.TryAddSingleton<CatalogSnapshotCache>();
        services.TryAddSingleton<ICatalogCache>(
            provider => provider.GetRequiredService<CatalogSnapshotCache>());

        // The module's published surface. Registered here rather than in each consuming module so that
        // the only way to reach a catalogue fact is through the contract the boundary allows
        // (ARCH-004), and so a consumer that forgot to compose this module fails to start rather than
        // failing on the first call.
        services.TryAddScoped<ICatalogAvailabilityQuery, CatalogAvailabilityQuery>();

        return services;
    }

    private static void AddPersistence(IServiceCollection services)
    {
        services.AddDbContext<CatalogDbContext>((provider, builder) =>
        {
            var options = provider.GetRequiredService<IOptions<DatabaseOptions>>().Value;

            builder.UseNpgsql(options.BuildPooledConnectionString(), npgsql =>
            {
                npgsql.MigrationsHistoryTable(
                    ModuleDbContext.MigrationsHistoryTable, CatalogDbContext.SchemaName);
                npgsql.CommandTimeout(options.CommandTimeoutSeconds);
                npgsql.EnableRetryOnFailure(3, TimeSpan.FromSeconds(2), null);
                // A version auto-includes four sibling collections — categories, service types, design
                // groups with their options, and rules. One query would join them into a cartesian
                // product whose row count is their sizes multiplied; one query per collection is the
                // shape the read actually has.
                npgsql.UseQuerySplittingBehavior(QuerySplittingBehavior.SplitQuery);
            })
            .UseSnakeCaseNamingConvention();
        });

        services.AddModuleContext<CatalogDbContext>(CatalogDbContext.SchemaName);
    }
}
