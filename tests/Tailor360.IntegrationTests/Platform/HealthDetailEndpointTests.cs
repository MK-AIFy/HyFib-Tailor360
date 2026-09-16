using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Shouldly;
using Tailor360.IntegrationTests.Identity;
using Tailor360.Platform.Abstractions.Health;
using Tailor360.Platform.Security.Permissions;

namespace Tailor360.IntegrationTests.Platform;

/// <summary>
/// <c>GET /health/detail</c> (#447): the report <c>container.md</c> line 172 reserves for
/// <c>admin.health.read</c>, never for a public caller — every registered check named, with its status,
/// duration and description, including the two non-essential checks that no orchestrator probe has ever
/// surfaced before this issue.
/// </summary>
[Collection(WebApplicationCollection.Name)]
[Trait("Category", "Integration")]
public sealed class HealthDetailEndpointTests(WebApplicationFixture fixture)
{
    private const string Path = "/health/detail";
    private const string ReadyPath = "/health/ready";

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task AnAnonymousCallerIsRefused()
    {
        using var client = fixture.CreateClient();
        client.DefaultRequestHeaders.Add(TestClientAddressStartupFilter.HeaderName, "203.0.113.60");

        var response = await client.GetAsync(Path, Token);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task ASignedInCallerWithoutThePermissionIsRefused()
    {
        using var admin = await AdministrationHarness.AdministratorAsync(
            fixture, "healthnoperm", "203.0.113.61", grantPermission: null);

        var response = await admin.GetAsync(Path);

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task AnAdminWithHealthReadSeesEveryRegisteredCheckAndTheBuildVersion()
    {
        using var admin = await AdministrationHarness.AdministratorAsync(
            fixture, "healthread", "203.0.113.62", grantPermission: PlatformPermissions.HealthRead);

        var response = await admin.GetAsync(Path);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        response.Headers.CacheControl?.NoStore.ShouldBeTrue();

        var payload = await response.Content.ReadFromJsonAsync<JsonElement>(Token);
        payload.GetProperty("Version").GetString().ShouldNotBeNullOrWhiteSpace();

        var names = payload.GetProperty("Components").EnumerateArray()
            .Select(component => component.GetProperty("Name").GetString())
            .ToHashSet(StringComparer.Ordinal);

        // The essential and startup checks every host registers, and — the point of this issue — the
        // two non-essential checks that ran on every anonymous probe before this route existed and
        // whose result nothing ever read.
        names.ShouldContain("database");
        names.ShouldContain("migrations");
        names.ShouldContain("outbox");
        names.ShouldContain("audit-partitions");
        names.ShouldContain("self");
    }

    /// <summary>
    /// The acceptance criterion this whole issue exists to prove: a dependency tagged
    /// <see cref="HealthCheckTags.NonEssential"/> degrades the detail report without taking readiness
    /// down with it. A test-only check stands in for a real one (rather than forcing
    /// <c>OutboxBacklogHealthCheck</c> or <c>AuditPartitionHealthCheck</c> to fail, which would mean
    /// mutating shared tables other concurrent tests read) — <c>HealthCheckTags.NonEssential</c> is
    /// exactly the contract every non-essential check promises to honour, so a synthetic one registered
    /// under the same tag proves the routing rule the same way a real one failing would.
    /// </summary>
    [Fact]
    public async Task ANonEssentialFailureLeavesReadyGreenWhileDetailReportsDegraded()
    {
        var (user, _) = await AdministrationHarness.AccountAsync(
            fixture, "healthdegraded", PlatformPermissions.HealthRead);

        var factory = fixture.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
            services.AddHealthChecks().AddCheck(
                "forced-non-essential",
                () => HealthCheckResult.Degraded("Forced degraded for the test."),
                tags: [HealthCheckTags.NonEssential])));

        var httpClient = factory.CreateClient(AuthenticationClient.ClientOptions);
        httpClient.DefaultRequestHeaders.Add(TestClientAddressStartupFilter.HeaderName, "203.0.113.64");

        using var client = AuthenticationClient.Open(httpClient, "203.0.113.64");
        await AdministrationHarness.SignInAsync(client, user);

        var ready = await client.GetAsync(ReadyPath);
        ready.StatusCode.ShouldBe(HttpStatusCode.OK, "a non-essential dependency being down must not take readiness with it");

        var detail = await client.GetAsync(Path);
        detail.StatusCode.ShouldBe(HttpStatusCode.OK, "Degraded still serves traffic, so this is 200 and not 503");

        var payload = await AuthenticationClient.ReadAsync<JsonElement>(detail);
        payload.GetProperty("Status").GetString().ShouldBe("Degraded");

        var forced = payload.GetProperty("Components").EnumerateArray()
            .Single(component => component.GetProperty("Name").GetString() == "forced-non-essential");
        forced.GetProperty("Status").GetString().ShouldBe("Degraded");
        forced.GetProperty("Description").GetString().ShouldBe("Forced degraded for the test.");
    }

    /// <summary>
    /// The other half of the same rule, at the other status: an essential dependency down answers 503
    /// and still renders every other check, rather than failing to render at all. A test-only check
    /// tagged <see cref="HealthCheckTags.Ready"/> stands in for the database for the same reason the
    /// non-essential test above does not sever the real connection — a shared instance other concurrent
    /// tests are using is not a lever this suite pulls.
    /// </summary>
    [Fact]
    public async Task AnEssentialFailureAnswers503AndStillRendersEveryOtherCheck()
    {
        var (user, _) = await AdministrationHarness.AccountAsync(
            fixture, "healthunhealthy", PlatformPermissions.HealthRead);

        var factory = fixture.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
            services.AddHealthChecks().AddCheck(
                "forced-essential",
                () => HealthCheckResult.Unhealthy("Forced unhealthy for the test."),
                tags: [HealthCheckTags.Ready])));

        var httpClient = factory.CreateClient(AuthenticationClient.ClientOptions);
        httpClient.DefaultRequestHeaders.Add(TestClientAddressStartupFilter.HeaderName, "203.0.113.65");

        using var client = AuthenticationClient.Open(httpClient, "203.0.113.65");
        await AdministrationHarness.SignInAsync(client, user);

        var detail = await client.GetAsync(Path);
        detail.StatusCode.ShouldBe(HttpStatusCode.ServiceUnavailable);

        var payload = await AuthenticationClient.ReadAsync<JsonElement>(detail);
        payload.GetProperty("Status").GetString().ShouldBe("Unhealthy");

        var names = payload.GetProperty("Components").EnumerateArray()
            .Select(component => component.GetProperty("Name").GetString())
            .ToHashSet(StringComparer.Ordinal);

        // Rendered alongside the forced failure, not instead of it — the whole point of the acceptance
        // criterion this test proves.
        names.ShouldContain("forced-essential");
        names.ShouldContain("database");
        names.ShouldContain("self");
    }
}
