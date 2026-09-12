using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Shouldly;
using Tailor360.Modules.Integration.Infrastructure.Storage;

namespace Tailor360.ContractTests.Adapters;

/// <summary>
/// The guard ADR-0014 section 2 point 5 promises: outside Development a host does not start on an object-storage
/// configuration that would leave every rendered document in one process's memory, and Development is let through
/// so a developer without the secrets written still has a working loop.
/// </summary>
[Trait("Category", "Contract")]
public sealed class ObjectStorageOptionsValidatorTests
{
    [Theory]
    [InlineData("Production")]
    [InlineData("Staging")]
    public void RefusesAStartOutsideDevelopmentWithoutAnEndpointOrWithoutBothKeys(string environment)
    {
        var validator = new ObjectStorageOptionsValidator(new StubEnvironment(environment));

        var unconfigured = validator.Validate(null, new ObjectStorageOptions());
        unconfigured.Failed.ShouldBeTrue();
        unconfigured.FailureMessage.ShouldContain("ObjectStorage:Endpoint");

        var halfSecret = validator.Validate(null, new ObjectStorageOptions { Endpoint = "http://minio:9000", AccessKey = "tailor360" });
        halfSecret.Failed.ShouldBeTrue();
        halfSecret.FailureMessage.ShouldContain("SecretKey");

        validator.Validate(null, new ObjectStorageOptions { Endpoint = "http://minio:9000", AccessKey = "tailor360", SecretKey = "not-a-real-secret" })
            .Succeeded.ShouldBeTrue();
    }

    [Fact]
    public void LetsDevelopmentThroughWhateverIsSet()
    {
        var validator = new ObjectStorageOptionsValidator(new StubEnvironment(Environments.Development));

        validator.Validate(null, new ObjectStorageOptions()).Succeeded.ShouldBeTrue();
        validator.Validate(null, new ObjectStorageOptions { Endpoint = "http://127.0.0.1:9000" }).Succeeded.ShouldBeTrue();
    }

    /// <summary>The smallest thing that answers "which environment is this".</summary>
    private sealed class StubEnvironment(string environmentName) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = environmentName;

        public string ApplicationName { get; set; } = "Tailor360.Worker";

        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;

        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
