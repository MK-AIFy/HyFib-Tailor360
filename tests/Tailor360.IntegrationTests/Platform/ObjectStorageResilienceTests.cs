using System.Net;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Shouldly;
using Tailor360.IntegrationTests.Identity;
using Tailor360.Modules.Billing.Application.Abstractions;
using Tailor360.Modules.Integration.Infrastructure.Storage;
using Tailor360.Platform.Abstractions.Ports;
using Tailor360.Platform.Abstractions.Time;
using Tailor360.Platform.Security.Permissions;
using static Tailor360.IntegrationTests.Billing.InvoiceScenes;

namespace Tailor360.IntegrationTests.Platform;

/// <summary>
/// #453's central acceptance criterion: with object storage unreachable, the circuit breaker opens,
/// <c>/health/detail</c> reports it Degraded, <c>/health/ready</c> stays green, and posting an invoice and
/// recording a payment both still succeed — <c>docs/architecture/failure-modes.md</c> section 5's promise
/// that the financial record is independent of the file. Once storage answers again, the breaker recovers
/// with no operator action.
/// </summary>
/// <remarks>
/// The outage is injected by replacing the module's <c>ResilientObjectStorage</c> registration with one
/// wrapping a fake <c>IObjectStorage</c> that fails on command — never by pointing at a closed port or a
/// real MinIO instance, so this test needs nothing beyond the database every other integration test
/// already needs. The breaker's own failure threshold and break duration are set to fast, deterministic
/// test values; the real defaults (5 failures, 30 seconds) are <c>docs/architecture/resilience-policies.md</c>
/// section 2.2's concern, not this test's.
/// </remarks>
[Trait("Category", "Integration")]
[Collection(WebApplicationCollection.Name)]
public sealed class ObjectStorageResilienceTests(WebApplicationFixture fixture)
{
    private static readonly string RunToken = AdministrationHarness.UniqueToken(6).ToUpperInvariant();

    [Fact]
    public async Task StorageOutageDegradesDetailKeepsReadinessGreenAndBillingStillPostsThenRecovers()
    {
        Assert.SkipUnless(DatabaseAvailability.IsAvailable, DatabaseAvailability.SkipReason);

        await SeedPaymentModesAsync();

        var (user, _) = await AdministrationHarness.AccountAsync(fixture, "storageout", PlatformPermissions.HealthRead);

        var innerStorage = new SwitchableObjectStorage();
        var factory = fixture.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
            services.Replace(ServiceDescriptor.Singleton(provider => new ResilientObjectStorage(
                innerStorage,
                Options.Create(new ObjectStorageOptions
                {
                    Breaker = new ObjectStorageBreakerOptions { FailureThreshold = 1, BreakDuration = TimeSpan.FromMilliseconds(200) },
                    CallTimeout = TimeSpan.FromSeconds(2),
                }),
                provider.GetRequiredService<IClock>(),
                provider.GetRequiredService<ILogger<ResilientObjectStorage>>())))));

        var storage = factory.Services.GetRequiredService<ResilientObjectStorage>();

        // Drive the breaker open — the fake fails until told otherwise.
        await Should.ThrowAsync<ObjectStorageUnavailableException>(
            () => storage.ExistsAsync("probe/one", Token));
        storage.Breaker.State.ShouldBe(ObjectStorageCircuitState.Open);

        var httpClient = factory.CreateClient(AuthenticationClient.ClientOptions);
        httpClient.DefaultRequestHeaders.Add(TestClientAddressStartupFilter.HeaderName, "203.0.113.180");
        using var client = AuthenticationClient.Open(httpClient, "203.0.113.180");
        await AdministrationHarness.SignInAsync(client, user);

        var ready = await client.GetAsync("/health/ready");
        ready.StatusCode.ShouldBe(HttpStatusCode.OK, "object storage is non-essential; readiness must stay green while it is down");

        var detail = await client.GetAsync("/health/detail");
        detail.StatusCode.ShouldBe(HttpStatusCode.OK, "Degraded still serves traffic, so this is 200 and not 503");
        var payload = await AuthenticationClient.ReadAsync<JsonElement>(detail);
        payload.GetProperty("Status").GetString().ShouldBe("Degraded");
        var storageComponent = payload.GetProperty("Components").EnumerateArray()
            .Single(component => component.GetProperty("Name").GetString() == "object-storage");
        storageComponent.GetProperty("Status").GetString().ShouldBe("Degraded");

        // The financial record does not depend on the file: an invoice still posts and a payment still
        // records while object storage is unreachable.
        var scene = await BuildAsync(fixture, "storageout-owner", "203.0.113.181", "SOUT", RunToken);
        using var cashier = await CashierAsync(
            fixture, "storageout-cashier", "203.0.113.182", scene.Branch,
            BillingPermissions.PostInvoice, BillingPermissions.RecordPayment, BillingPermissions.Session);

        (await cashier.PostAsync("/api/v1/billing/cashier-sessions", new { openingFloat = 0m }, Key()))
            .StatusCode.ShouldBe(HttpStatusCode.Created);

        var (invoiceId, tag) = await DraftAsync(cashier, scene.OrderId, scene.Reference);
        var posted = await cashier.PostAsync($"/api/v1/billing/invoices/{invoiceId}/post", new { reason = (string?)null }, Tagged(tag));
        posted.StatusCode.ShouldBe(HttpStatusCode.OK, await posted.Content.ReadAsStringAsync(Token));

        var payment = await cashier.PostAsync(
            "/api/v1/billing/payments", new { orderId = scene.OrderId, modeCode = "CASH", amount = 100m, reference = (string?)null }, Key());
        payment.StatusCode.ShouldBe(HttpStatusCode.Created, await payment.Content.ReadAsStringAsync(Token));

        // Recovery: once storage answers again, the breaker half-opens and closes with no operator action.
        innerStorage.Restore();
        await Task.Delay(TimeSpan.FromMilliseconds(250), Token);
        await storage.ExistsAsync("probe/two", Token);
        storage.Breaker.State.ShouldBe(ObjectStorageCircuitState.Closed);
    }

    private async Task SeedPaymentModesAsync()
    {
        using var scope = fixture.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<IBillingReferenceDataSeeder>().SeedPaymentModesAsync(SessionTestData.OrganisationId, Token);
    }

    /// <summary>Fails every call until <see cref="Restore"/> is called — the "storage is down, then it recovers" fake.</summary>
    private sealed class SwitchableObjectStorage : IObjectStorage
    {
        private volatile bool _failing = true;

        public void Restore() => _failing = false;

        public Task PutAsync(string key, Stream content, string contentType, CancellationToken cancellationToken = default)
            => _failing ? throw new InvalidOperationException("Object storage is unreachable.") : Task.CompletedTask;

        public Task<Stream?> OpenReadAsync(string key, CancellationToken cancellationToken = default)
            => _failing ? throw new InvalidOperationException("Object storage is unreachable.") : Task.FromResult<Stream?>(null);

        public Task<bool> ExistsAsync(string key, CancellationToken cancellationToken = default)
            => _failing ? throw new InvalidOperationException("Object storage is unreachable.") : Task.FromResult(true);
    }
}
