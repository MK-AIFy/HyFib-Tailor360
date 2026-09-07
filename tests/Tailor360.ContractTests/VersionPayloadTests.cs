using Microsoft.Extensions.Hosting;
using Shouldly;
using Tailor360.Platform.Persistence.Contexts;
using Tailor360.Platform.Persistence.Migrating;
using Tailor360.Web.Configuration;
using Tailor360.Web.Endpoints;
using Tailor360.Web.OpenApi;

namespace Tailor360.ContractTests;

/// <summary>
/// What <c>GET /api/version</c> discloses, per environment.
/// </summary>
/// <remarks>
/// It is the one anonymous endpoint that describes the server rather than answering a question about
/// the caller, so what it says is a contract in both directions: the client depends on every member
/// being there, and an unauthenticated caller must learn nothing from it that helps them attack the
/// deployment.
/// </remarks>
[Trait("Category", "Contract")]
public sealed class VersionPayloadTests
{
    /// <summary>
    /// The revision is a Development-only member, and it is withheld by being <em>absent</em> rather
    /// than present and empty — a client that rendered an empty build hash would show a blank where a
    /// support engineer expects a value.
    /// </summary>
    [Theory]
    [InlineData("Staging")]
    [InlineData("Production")]
    [InlineData("Training")]
    public void TheRevisionIsWithheldOutsideDevelopment(string environmentName)
        => Describe(environmentName).Commit.ShouldBeNull(
            "A precise revision tells an unauthenticated caller which published advisories apply to "
            + "what is running.");

    [Fact]
    public void TheRevisionIsDisclosedInDevelopment()
        => Describe(Environments.Development).Commit.ShouldNotBeNullOrWhiteSpace();

    /// <summary>
    /// The environment name decides whether the shell shows the "TRAINING — not real data" banner, and
    /// the client compares it with <c>production</c> exactly. Anything but lower case shows the banner
    /// in production, or hides it everywhere else; both are safety problems.
    /// </summary>
    [Theory]
    [InlineData("Production", "production")]
    [InlineData("Staging", "staging")]
    [InlineData("Training", "training")]
    public void TheEnvironmentNameIsLowerCase(string environmentName, string expected)
        => Describe(environmentName).Environment.ShouldBe(expected);

    [Fact]
    public void TheApiMemberNamesTheMajorSurfaceTheServerServes()
    {
        Describe(Environments.Production).Api.ShouldBe(ApiDocument.Major);
        ApiDocument.SurfacePrefix.ShouldBe($"/api/{ApiDocument.Major}");
    }

    /// <summary>
    /// The minimum is reported exactly as configured, including when nothing is configured. An empty
    /// value is the honest answer before a release — nothing has shipped that a client could be older
    /// than — and the client reads it as "no floor" rather than as a missing field.
    /// </summary>
    [Fact]
    public void TheConfiguredMinimumIsReportedVerbatim()
    {
        Describe(Environments.Production).MinimumClient.ShouldBe(string.Empty);
        Describe(Environments.Production, minimum: "2.4.0").MinimumClient.ShouldBe("2.4.0");
    }

    /// <summary>
    /// The schema version is the newest migration timestamp the build carries: fourteen digits, and
    /// never the migration's name, which is a feature the team is working on and this endpoint is
    /// anonymous.
    /// </summary>
    [Fact]
    public void TheSchemaVersionIsATimestampAndNotAMigrationName()
    {
        var schemaVersion = Describe(Environments.Production).SchemaVersion;

        schemaVersion.ShouldNotBe(SchemaVersion.None);
        schemaVersion.Length.ShouldBe(14);
        schemaVersion.ShouldAllBe(character => char.IsAsciiDigit(character));
    }

    /// <summary>A registry with no contexts reports no schema rather than throwing or guessing.</summary>
    [Fact]
    public void ABuildWithNoMigrationsReportsNoSchemaVersion()
        => SchemaVersion.Of(new ModuleContextRegistry()).ShouldBe(SchemaVersion.None);

    private static VersionResponse Describe(string environmentName, string minimum = "")
    {
        var registry = new ModuleContextRegistry()
            .Add<PlatformDbContext>(PlatformDbContext.SchemaName, ModuleContextRegistry.PlatformOrder);

        return VersionEndpoints.Describe(
            new StubEnvironment(environmentName),
            new ClientCompatibilityOptions { MinimumClientVersion = minimum },
            registry);
    }

    /// <summary>The smallest thing that answers "which environment is this".</summary>
    private sealed class StubEnvironment(string environmentName) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = environmentName;

        public string ApplicationName { get; set; } = "Tailor360.Web";

        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;

        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } =
            new Microsoft.Extensions.FileProviders.NullFileProvider();
    }
}
