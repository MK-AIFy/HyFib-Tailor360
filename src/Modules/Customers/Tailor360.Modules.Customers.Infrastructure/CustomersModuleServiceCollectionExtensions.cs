using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Tailor360.Modules.Customers.Application.Abstractions;
using Tailor360.Modules.Customers.Application.Consent;
using Tailor360.Modules.Customers.Application.Customers;
using Tailor360.Modules.Customers.Application.Options;
using Tailor360.Modules.Customers.Application.Preferences;
using Tailor360.Modules.Customers.Contracts.Consent;
using Tailor360.Modules.Customers.Contracts.Customers;
using Tailor360.Modules.Customers.Contracts.Preferences;
using Tailor360.Modules.Customers.Infrastructure.Consent;
using Tailor360.Modules.Customers.Infrastructure.Persistence;
using Tailor360.Platform.Persistence;
using Tailor360.Platform.Persistence.Conventions;
using Tailor360.Platform.Persistence.Migrating;

namespace Tailor360.Modules.Customers.Infrastructure;

/// <summary>
/// Composes the Customers module.
/// </summary>
/// <remarks>
/// <para>
/// The module registers <strong>no <c>IResourceScopeResolver</c></strong>, and that is a decision
/// rather than an omission. A resolver reports the one branch a row belongs to, and the platform then
/// refuses a caller who cannot reach that branch — which is the right shape for a garment job and the
/// wrong shape for a customer. <c>docs/prd/workflows/branch-scenarios.md</c> section 3.2 places the
/// customer record organisation-wide with branch-scoped <em>visibility</em>: a customer served at
/// Branch A who walks into Branch B is the same customer, and refusing Branch B is how a second
/// record gets created. So the record's routes declare
/// <c>TouchesNoBranchOwnedResource</c> citing that document, and visibility is enforced inside the
/// search query, where it belongs.
/// </para>
/// </remarks>
public static class CustomersModuleServiceCollectionExtensions
{
    /// <summary>The database schema this module owns. No other module may map a table in it.</summary>
    public const string SchemaName = CustomersDbContext.SchemaName;

    /// <summary>Registers the module's services, options and persistence.</summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configuration">Application configuration.</param>
    /// <returns>The service collection.</returns>
    public static IServiceCollection AddCustomersModule(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        AddPersistence(services);

        // This module's own publisher, over this module's context and therefore its own outbox. The
        // binding is per module because IEventPublisher is one interface and every module is composed
        // into one container (#77).
        services.AddModuleOutbox<CustomersDbContext>();
        services.TryAddScoped<ICustomersEventPublisher, CustomersEventPublisher>();

        services.TryAddScoped<ICustomerStore, CustomerStore>();
        services.TryAddScoped<ICustomerDirectory, CustomerDirectory>();
        services.TryAddScoped<IMergeStore, MergeStore>();
        services.TryAddScoped<CustomerHandler>();
        services.TryAddScoped<IConsentStore, ConsentStore>();
        services.TryAddScoped<IPreferenceStore, PreferenceStore>();
        services.TryAddScoped<ConsentHandler>();
        services.TryAddScoped<PreferenceHandler>();
        services.TryAddScoped<IConsentReferenceDataSeeder, ConsentReferenceDataSeeder>();
        services.TryAddScoped<IExportStore, ExportStore>();
        services.TryAddScoped<CustomerExportHandler>();

        // Validated at start-up rather than when somebody answers a subject-access request. A
        // misconfigured lifetime is clamped by the domain and would otherwise never be noticed, which
        // is exactly the kind of quiet wrong answer a privacy setting must not be allowed to give.
        services.AddOptions<CustomerExportOptions>()
            .Bind(configuration.GetSection(CustomerExportOptions.SectionName))
            .ValidateDataAnnotations()
            .Validate(
                options => options.IsLifetimeUsable,
                "Customers:Export:Lifetime must be between 15 minutes and 30 days, the bounds "
                + "CustomerExport enforces on a copy of somebody's personal data.")
            .ValidateOnStart();

        // The module's published surface. Registered here rather than in each consuming module so
        // that the only way to reach a customer fact is through the contract the boundary allows
        // (ARCH-004), and so a consumer that forgot to compose this module fails to start rather
        // than failing on the first send.
        services.TryAddScoped<IConsentQuery, ConsentQuery>();
        services.TryAddScoped<ICommunicationPreferenceQuery, CommunicationPreferenceQuery>();
        services.TryAddScoped<ICustomerSnapshotQuery, CustomerSnapshotQuery>();

        return services;
    }

    private static void AddPersistence(IServiceCollection services)
    {
        services.AddDbContext<CustomersDbContext>((provider, builder) =>
        {
            var options = provider.GetRequiredService<IOptions<DatabaseOptions>>().Value;

            builder.UseNpgsql(options.BuildPooledConnectionString(), npgsql =>
            {
                npgsql.MigrationsHistoryTable(
                    ModuleDbContext.MigrationsHistoryTable, CustomersDbContext.SchemaName);
                npgsql.CommandTimeout(options.CommandTimeoutSeconds);
                npgsql.EnableRetryOnFailure(3, TimeSpan.FromSeconds(2), null);
            })
            .UseSnakeCaseNamingConvention();
        });

        // Default order: platform migrates first, then Identity, then everything else alphabetically.
        services.AddModuleContext<CustomersDbContext>(CustomersDbContext.SchemaName);
    }
}
