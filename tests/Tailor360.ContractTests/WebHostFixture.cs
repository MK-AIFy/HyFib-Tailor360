using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Hosting;
using Tailor360.Web;

namespace Tailor360.ContractTests;

/// <summary>
/// Hosts the web application in process for contract tests. One instance is shared by the whole
/// collection because building the host is the expensive part and none of these tests mutate state.
/// </summary>
public sealed class WebHostFixture : WebApplicationFactory<WebEntryPoint>
{
    /// <inheritdoc />
    protected override IHost CreateHost(IHostBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.UseEnvironment(Environments.Development);
        return base.CreateHost(builder);
    }
}

/// <summary>The collection that shares one hosted application.</summary>
[CollectionDefinition(Name)]
public sealed class WebHostCollection : ICollectionFixture<WebHostFixture>
{
    /// <summary>The collection name.</summary>
    public const string Name = "web-host";
}
