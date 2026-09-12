using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Tailor360.Modules.Billing.Infrastructure.Persistence;
using Tailor360.Modules.Catalog.Infrastructure.Persistence;
using Tailor360.Modules.Customers.Infrastructure.Persistence;
using Tailor360.Modules.Identity.Infrastructure.Persistence;
using Tailor360.Modules.Orders.Infrastructure.Persistence;
using Tailor360.Platform.Persistence.Contexts;
using Tailor360.Platform.Persistence.Conventions;
using Tailor360.Web;

namespace Tailor360.IntegrationTests;

/// <summary>Hosts the web application in process, shared by the integration collection.</summary>
public sealed class WebApplicationFixture : WebApplicationFactory<WebEntryPoint>
{
    /// <inheritdoc />
    protected override IHost CreateHost(IHostBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        // Fail the whole run rather than quietly skipping when continuous integration has no database.
        DatabaseAvailability.EnsureAvailableInContinuousIntegration();

        builder.UseEnvironment(Environments.Development);

        // The hosted application must reach the same database the tests use. Its development settings
        // name the compose instance, which is the right default for a developer but is not necessarily
        // what is running here — a cloud session supplies its own instance instead.
        if (DatabaseAvailability.ConnectionString is { } connectionString)
        {
            // Migrate before hosting. A deployment runs `migrate` and only then lets the application
            // serve, and the startup probe is built to refuse traffic until that has happened; hosting
            // against an unmigrated database would therefore be testing a state the system is designed
            // never to serve in.
            MigrateAsync(connectionString).GetAwaiter().GetResult();

            builder.ConfigureHostConfiguration(configuration => configuration.AddInMemoryCollection(
                new Dictionary<string, string?> { ["Database:ConnectionString"] = connectionString }));
        }

        // Every rate-limit policy and every abuse throttle partitions on the client address, and the
        // test server leaves it unset — so without this, one shared partition would count every test in
        // the assembly together and the tenth sign-in anywhere would start answering 429. The filter
        // runs ahead of the application's own pipeline and sets the address from a header only tests
        // send; the forwarded-headers middleware that follows does not touch it, because no
        // X-Forwarded-For accompanies it.
        builder.ConfigureServices(services =>
            services.AddSingleton<IStartupFilter, TestClientAddressStartupFilter>());

        return base.CreateHost(builder);
    }

    // Every module that owns a schema is migrated here, in the order the migration runner uses. A
    // module missing from this list would leave the startup probe reporting pending migrations, so the
    // hosted application would refuse to serve and every test in the collection would fail on a
    // health check rather than on what it was testing.
    private static async Task MigrateAsync(string connectionString)
    {
        var platformOptions = new DbContextOptionsBuilder<PlatformDbContext>()
            .UseNpgsql(connectionString, npgsql => npgsql.MigrationsHistoryTable(
                ModuleDbContext.MigrationsHistoryTable, PlatformDbContext.SchemaName))
            .UseSnakeCaseNamingConvention()
            .Options;

        await using (var context = new PlatformDbContext(platformOptions))
        {
            await context.Database.MigrateAsync();
        }

        var identityOptions = new DbContextOptionsBuilder<IdentityDbContext>()
            .UseNpgsql(connectionString, npgsql => npgsql.MigrationsHistoryTable(
                ModuleDbContext.MigrationsHistoryTable, IdentityDbContext.SchemaName))
            .UseSnakeCaseNamingConvention()
            .Options;

        await using (var identity = new IdentityDbContext(identityOptions))
        {
            await identity.Database.MigrateAsync();
        }

        var customersOptions = new DbContextOptionsBuilder<CustomersDbContext>()
            .UseNpgsql(connectionString, npgsql => npgsql.MigrationsHistoryTable(
                ModuleDbContext.MigrationsHistoryTable, CustomersDbContext.SchemaName))
            .UseSnakeCaseNamingConvention()
            .Options;

        await using (var customers = new CustomersDbContext(customersOptions))
        {
            await customers.Database.MigrateAsync();
        }

        var catalogOptions = new DbContextOptionsBuilder<CatalogDbContext>()
            .UseNpgsql(connectionString, npgsql => npgsql.MigrationsHistoryTable(
                ModuleDbContext.MigrationsHistoryTable, CatalogDbContext.SchemaName))
            .UseSnakeCaseNamingConvention()
            .Options;

        await using (var catalog = new CatalogDbContext(catalogOptions))
        {
            await catalog.Database.MigrateAsync();
        }

        var ordersOptions = new DbContextOptionsBuilder<OrdersDbContext>()
            .UseNpgsql(connectionString, npgsql => npgsql.MigrationsHistoryTable(
                ModuleDbContext.MigrationsHistoryTable, OrdersDbContext.SchemaName))
            .UseSnakeCaseNamingConvention()
            .Options;

        await using (var orders = new OrdersDbContext(ordersOptions))
        {
            await orders.Database.MigrateAsync();
        }

        var billingOptions = new DbContextOptionsBuilder<BillingDbContext>()
            .UseNpgsql(connectionString, npgsql => npgsql.MigrationsHistoryTable(
                ModuleDbContext.MigrationsHistoryTable, BillingDbContext.SchemaName))
            .UseSnakeCaseNamingConvention()
            .Options;

        await using var billing = new BillingDbContext(billingOptions);
        await billing.Database.MigrateAsync();
    }
}

/// <summary>
/// Lets a test choose the client address its requests appear to come from, so that one test's attempts
/// cannot exhaust another's rate-limit or throttle budget.
/// </summary>
internal sealed class TestClientAddressStartupFilter : IStartupFilter
{
    /// <summary>The header a test sets to choose its own client address.</summary>
    public const string HeaderName = "X-Test-Client-Address";

    /// <inheritdoc />
    public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next)
    {
        ArgumentNullException.ThrowIfNull(next);

        return app =>
        {
            app.Use(async (context, continuation) =>
            {
                if (context.Request.Headers.TryGetValue(HeaderName, out var address)
                    && IPAddress.TryParse(address.ToString(), out var parsed))
                {
                    context.Connection.RemoteIpAddress = parsed;
                }

                await continuation();
            });

            next(app);
        };
    }
}

/// <summary>The collection that shares one hosted application.</summary>
[CollectionDefinition(Name)]
public sealed class WebApplicationCollection : ICollectionFixture<WebApplicationFixture>
{
    /// <summary>The collection name.</summary>
    public const string Name = "web-application";
}
