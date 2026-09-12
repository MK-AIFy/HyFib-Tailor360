using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Tailor360.Modules.Billing.Application.Abstractions;
using Tailor360.Modules.Billing.Application.Pricing;
using Tailor360.Modules.Billing.Application.Registrations;
using Tailor360.Modules.Billing.Application.Tax;
using Tailor360.Modules.Billing.Contracts.Pricing;
using Tailor360.Modules.Billing.Infrastructure.Persistence;
using Tailor360.Modules.Catalog.Contracts.Catalogue;
using Tailor360.Platform.Persistence;
using Tailor360.Platform.Persistence.Conventions;
using Tailor360.Platform.Persistence.Migrating;
using Tailor360.Platform.Persistence.Outbox;

namespace Tailor360.Modules.Billing.Infrastructure;

/// <summary>
/// Registers the Billing module: the pricing and tax engine, invoices, payments, receipts and cashier reconciliation.
/// Hosts compose the application only through extensions like this one (architecture rule ARCH-006),
/// so a host never depends on a module's internal types.
/// </summary>
public static class BillingModuleServiceCollectionExtensions
{
    /// <summary>The database schema this module owns. No other module may map a table in it.</summary>
    public const string SchemaName = BillingDbContext.SchemaName;

    /// <summary>Registers the module's services, options and persistence.</summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configuration">Application configuration.</param>
    public static IServiceCollection AddBillingModule(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        AddPersistence(services);

        // The module's own outbox pair, mapped by ModuleDbContext, so that the events the later
        // slices publish (#42, #43) commit with the rows that cause them.
        services.AddModuleOutbox<BillingDbContext>();

        services.TryAddScoped<ITaxConfigurationStore, TaxConfigurationStore>();
        services.TryAddScoped<IGstRegistrationStore, GstRegistrationStore>();
        services.TryAddScoped<TaxConfigurationHandler>();
        services.TryAddScoped<GstRegistrationHandler>();
        services.TryAddScoped<IPriceListStore, PriceListStore>();
        services.TryAddScoped<PriceListHandler>();
        services.TryAddScoped<ICalculationSnapshotStore, CalculationSnapshotStore>();
        services.TryAddScoped<PricingService>();
        // The contract other modules price through is the same instance as the service the preview route uses.
        services.TryAddScoped<IPricingService>(provider => provider.GetRequiredService<PricingService>());

        // Link 4 of the catalogue's service types and design options — the price-list item code — is
        // answered here. Enumerable, not TryAdd: every module that owns something a service type links
        // to adds its own validator, and a second registration must join the list rather than replace it.
        services.AddScoped<ICatalogDependencyValidator, PriceListCatalogValidator>();

        return services;
    }

    private static void AddPersistence(IServiceCollection services)
    {
        services.AddDbContext<BillingDbContext>((provider, builder) =>
        {
            var options = provider.GetRequiredService<IOptions<DatabaseOptions>>().Value;

            builder.UseNpgsql(options.BuildPooledConnectionString(), npgsql =>
            {
                npgsql.MigrationsHistoryTable(ModuleDbContext.MigrationsHistoryTable, BillingDbContext.SchemaName);
                npgsql.CommandTimeout(options.CommandTimeoutSeconds);
                npgsql.EnableRetryOnFailure(3, TimeSpan.FromSeconds(2), null);
                npgsql.UseQuerySplittingBehavior(QuerySplittingBehavior.SplitQuery);
            })
            .UseSnakeCaseNamingConvention();
        });

        services.AddModuleContext<BillingDbContext>(BillingDbContext.SchemaName);
    }
}
