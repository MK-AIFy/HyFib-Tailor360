using System.Net;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Tailor360.IntegrationTests.Identity;
using Tailor360.Modules.Billing.Application.Abstractions;
using Tailor360.Platform.Security.Permissions;
using static Tailor360.IntegrationTests.Billing.InvoiceScenes;

namespace Tailor360.IntegrationTests.Billing;

/// <summary>
/// The payment modes (#161): seeded idempotently, listed under the Billing configuration permission, and
/// changed against their ETag with a rename, a branch restriction and a refund flag; a branch that is not
/// the organisation's is refused, a stale token is refused, and a later seed run touches nothing the Owner set.
/// </summary>
[Trait("Category", "Integration")]
[Collection(WebApplicationCollection.Name)]
public sealed class PaymentModeEndpointTests(WebApplicationFixture fixture)
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task SeedsOnceListsAndChangesAModeAgainstItsTag()
    {
        Assert.SkipUnless(DatabaseAvailability.IsAvailable, DatabaseAvailability.SkipReason);

        using (var scope = fixture.Services.CreateScope())
        {
            var seeder = scope.ServiceProvider.GetRequiredService<IBillingReferenceDataSeeder>();
            var first = await seeder.SeedPaymentModesAsync(SessionTestData.OrganisationId, Token);
            (first.ModesCreated + first.ModesUnchanged + first.ModesUpdated).ShouldBe(5);
            var second = await seeder.SeedPaymentModesAsync(SessionTestData.OrganisationId, Token);
            second.ShouldBe(new PaymentModeSeedOutcome(0, 0, 5), "a second run changes nothing");
        }

        using var administrator = await AdministrationHarness.AdministratorAsync(fixture, "pm-admin", "203.0.113.199", BillingPermissions.ManagePriceLists, IdentityPermissions.Branches);
        var branch = await BillingHarness.OpenBranchAsync(administrator);

        var listed = await administrator.GetAsync("/api/v1/billing/payment-modes");
        listed.StatusCode.ShouldBe(HttpStatusCode.OK, await listed.Content.ReadAsStringAsync(Token));
        var modes = JsonDocument.Parse(await listed.Content.ReadAsStringAsync(Token)).RootElement.EnumerateArray().ToList();
        modes.Select(mode => mode.GetProperty("code").GetString()).ShouldBe(["BANK_TRANSFER", "CARD", "CASH", "OTHER", "UPI"]);
        var cardId = modes.Single(mode => mode.GetProperty("code").GetString() == "CARD").GetProperty("id").GetGuid();

        // A change needs the token as read: none is 428, a wrong one is 412.
        var request = new { name = "Card (terminal)", requiresReference = true, requiresProvider = false, allowedForRefund = true, isActive = true, branchIds = new[] { branch } };
        (await administrator.PutAsync($"/api/v1/billing/payment-modes/{cardId}", request, Key())).StatusCode.ShouldBe(HttpStatusCode.PreconditionRequired);
        await Refused(administrator.PutAsync($"/api/v1/billing/payment-modes/{cardId}", request, Tagged("\"stale\"")), HttpStatusCode.PreconditionFailed, "billing.payment-mode-changed");

        // The token comes from reading the one mode; the list carries none.
        var read = await administrator.GetAsync($"/api/v1/billing/payment-modes/{cardId}");
        read.StatusCode.ShouldBe(HttpStatusCode.OK, await read.Content.ReadAsStringAsync(Token));
        var tag = read.Headers.ETag!.ToString();
        (await administrator.GetAsync($"/api/v1/billing/payment-modes/{Guid.CreateVersion7()}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        var changed = await administrator.PutAsync($"/api/v1/billing/payment-modes/{cardId}", request, Tagged(tag));
        changed.StatusCode.ShouldBe(HttpStatusCode.OK, await changed.Content.ReadAsStringAsync(Token));
        var payload = JsonDocument.Parse(await changed.Content.ReadAsStringAsync(Token)).RootElement;
        payload.GetProperty("name").GetString().ShouldBe("Card (terminal)");
        payload.GetProperty("allowedForRefund").GetBoolean().ShouldBeTrue();
        payload.GetProperty("branchIds").EnumerateArray().Select(id => id.GetGuid()).ShouldBe([branch]);
        changed.Headers.ETag.ShouldNotBeNull();
        var fresh = changed.Headers.ETag!.ToString();

        // A branch that is not the organisation's, the token as it was, and a mode that does not exist.
        await Refused(
            administrator.PutAsync($"/api/v1/billing/payment-modes/{cardId}", new { request.name, request.requiresReference, request.requiresProvider, request.allowedForRefund, request.isActive, branchIds = new[] { Guid.CreateVersion7() } }, Tagged(fresh)),
            HttpStatusCode.BadRequest, "billing.branch-not-found");
        await Refused(administrator.PutAsync($"/api/v1/billing/payment-modes/{cardId}", request, Tagged(tag)), HttpStatusCode.PreconditionFailed, "billing.payment-mode-changed");
        await Refused(administrator.PutAsync($"/api/v1/billing/payment-modes/{Guid.CreateVersion7()}", request, Tagged(fresh)), HttpStatusCode.NotFound, "billing.payment-mode-not-found");

        // A later seed run leaves the Owner's name, flag and branches exactly as set.
        using (var scope = fixture.Services.CreateScope())
        {
            var outcome = await scope.ServiceProvider.GetRequiredService<IBillingReferenceDataSeeder>().SeedPaymentModesAsync(SessionTestData.OrganisationId, Token);
            outcome.ShouldBe(new PaymentModeSeedOutcome(0, 0, 5));
            var reseeded = await scope.ServiceProvider.GetRequiredService<IPaymentModeStore>().FindAsync(cardId, SessionTestData.OrganisationId, Token);
            reseeded!.Name.ShouldBe("Card (terminal)", "the Owner's name stands");
            reseeded.AllowedForRefund.ShouldBeTrue("the Owner's flag stands");
            reseeded.Branches.Select(restriction => restriction.BranchId).ShouldBe([branch]);
        }

        // Left as found: the restriction is lifted again, so a cashier elsewhere in this run can still count a card.
        var current = (await administrator.GetAsync($"/api/v1/billing/payment-modes/{cardId}")).Headers.ETag!.ToString();
        var restored = await administrator.PutAsync(
            $"/api/v1/billing/payment-modes/{cardId}",
            new { name = "Card", requiresReference = true, requiresProvider = false, allowedForRefund = false, isActive = true, branchIds = Array.Empty<Guid>() },
            Tagged(current));
        restored.StatusCode.ShouldBe(HttpStatusCode.OK, await restored.Content.ReadAsStringAsync(Token));
    }
}
