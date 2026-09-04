using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
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
