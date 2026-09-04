using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
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
            builder.ConfigureHostConfiguration(configuration => configuration.AddInMemoryCollection(
                new Dictionary<string, string?> { ["Database:ConnectionString"] = connectionString }));
        }

        return base.CreateHost(builder);
    }
}

/// <summary>The collection that shares one hosted application.</summary>
[CollectionDefinition(Name)]
public sealed class WebApplicationCollection : ICollectionFixture<WebApplicationFixture>
{
    /// <summary>The collection name.</summary>
    public const string Name = "web-application";
}
