using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Shouldly;
using Tailor360.IntegrationTests.Identity;
using Tailor360.Modules.Billing.Application.Abstractions;
using Tailor360.Modules.Billing.Contracts.Events;
using Tailor360.Modules.Billing.Infrastructure.Persistence;
using Tailor360.Platform.Persistence.Contexts;
using Tailor360.Platform.Security.Permissions;
using static Tailor360.IntegrationTests.Billing.InvoiceScenes;

namespace Tailor360.IntegrationTests.Billing;

/// <summary>
/// The cashier session over HTTP (#161): opened once per cashier per branch under a second factor, read
/// back, closed once against its sheet by its own cashier with a reason where the count is out, immutable
/// at the database once closed, its event on the outbox and its two actions on the trail.
/// </summary>
[Trait("Category", "Integration")]
[Collection(WebApplicationCollection.Name)]
public sealed class CashierSessionEndpointTests(WebApplicationFixture fixture)
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task OpensOncePerCashierPerBranchReadsBackAndClosesOnceAgainstTheSheet()
    {
        Assert.SkipUnless(DatabaseAvailability.IsAvailable, DatabaseAvailability.SkipReason);

        await SeedPaymentModesAsync();
        using var owner = await AdministrationHarness.AdministratorAsync(fixture, "csh-owner", "203.0.113.190", IdentityPermissions.Branches);
        var branch = await BillingHarness.OpenBranchAsync(owner);
        using var cashier = await AdministrationHarness.AdministratorAtBranchAsync(fixture, "csh-one", "203.0.113.191", branch, BillingPermissions.Session);

        // Open, with the float; the answer is the session and its token.
        var opened = await cashier.PostAsync("/api/v1/billing/cashier-sessions", new { openingFloat = 2000m }, Key());
        opened.StatusCode.ShouldBe(HttpStatusCode.Created, await opened.Content.ReadAsStringAsync(Token));
        var sessionId = CreatedId(opened);
        var payload = JsonDocument.Parse(await opened.Content.ReadAsStringAsync(Token)).RootElement;
        payload.GetProperty("status").GetString().ShouldBe("Open");
        payload.GetProperty("openingFloat").GetDecimal().ShouldBe(2000m);
        payload.GetProperty("cashierId").GetGuid().ShouldBe(cashier.UserId);
        payload.GetProperty("branchId").GetGuid().ShouldBe(branch);
        opened.Headers.ETag.ShouldNotBeNull();

        // A second one is refused: one open session per cashier per branch.
        await Refused(cashier.PostAsync("/api/v1/billing/cashier-sessions", new { openingFloat = 500m }, Key()), HttpStatusCode.Conflict, "billing.cashier-session-already-open");
        await Refused(cashier.PostAsync("/api/v1/billing/cashier-sessions", new { openingFloat = -5m }, Key()), HttpStatusCode.BadRequest, "billing.amount-not-well-formed");

        // Found by status, and by identifier.
        var listed = await cashier.GetAsync("/api/v1/billing/cashier-sessions?status=open");
        listed.StatusCode.ShouldBe(HttpStatusCode.OK, await listed.Content.ReadAsStringAsync(Token));
        JsonDocument.Parse(await listed.Content.ReadAsStringAsync(Token)).RootElement.EnumerateArray()
            .Select(session => session.GetProperty("id").GetGuid()).ShouldContain(sessionId);
        await Refused(cashier.GetAsync("/api/v1/billing/cashier-sessions?status=7"), HttpStatusCode.BadRequest, "billing.value-required");
        await Refused(cashier.GetAsync("/api/v1/billing/cashier-sessions?status=open,closed"), HttpStatusCode.BadRequest, "billing.value-required");
        (await cashier.GetAsync($"/api/v1/billing/cashier-sessions/{sessionId}")).StatusCode.ShouldBe(HttpStatusCode.OK);

        // Open, the row takes one write and it is the close: the float cannot be moved underneath the cashier.
        using (var probe = fixture.Services.CreateScope())
        {
            var sql = $"UPDATE billing.cashier_sessions SET opening_float_amount = 0 WHERE id = '{sessionId}'";
            (await Should.ThrowAsync<PostgresException>(() => probe.ServiceProvider.GetRequiredService<BillingDbContext>().Database.ExecuteSqlRawAsync(sql, Token)))
                .SqlState.ShouldBe(PostgresErrorCodes.RestrictViolation);
        }

        // The count is 100 short and nothing says why: refused, and the session stays open.
        var shortSheet = new[] { new { denomination = 500m, quantity = 3 }, new { denomination = 200m, quantity = 2 } };
        await Refused(
            cashier.PostAsync($"/api/v1/billing/cashier-sessions/{sessionId}/close", new { denominations = shortSheet, modeTotals = Array.Empty<object>(), reason = (string?)null }, Key()),
            HttpStatusCode.BadRequest, "billing.variance-reason-required");
        await Refused(
            cashier.PostAsync($"/api/v1/billing/cashier-sessions/{sessionId}/close", new { denominations = new[] { new { denomination = 1000m, quantity = 2 } }, modeTotals = Array.Empty<object>(), reason = (string?)null }, Key()),
            HttpStatusCode.BadRequest, "billing.denomination-not-known");
        await Refused(
            cashier.PostAsync($"/api/v1/billing/cashier-sessions/{sessionId}/close", new { denominations = shortSheet, modeTotals = new[] { new { modeCode = "CHEQUE", counted = 1m } }, reason = "x" }, Key()),
            HttpStatusCode.BadRequest, "billing.payment-mode-not-known");

        // With the reason it closes, the variance recorded per mode and in all.
        var closed = await cashier.PostAsync(
            $"/api/v1/billing/cashier-sessions/{sessionId}/close",
            new { denominations = shortSheet, modeTotals = new[] { new { modeCode = "CARD", counted = 0m } }, reason = "A hundred-rupee note was given as change from the float." },
            Key());
        closed.StatusCode.ShouldBe(HttpStatusCode.OK, await closed.Content.ReadAsStringAsync(Token));
        var closedPayload = JsonDocument.Parse(await closed.Content.ReadAsStringAsync(Token)).RootElement;
        closedPayload.GetProperty("status").GetString().ShouldBe("Closed");
        closedPayload.GetProperty("expectedTotal").GetDecimal().ShouldBe(2000m);
        closedPayload.GetProperty("countedTotal").GetDecimal().ShouldBe(1900m);
        closedPayload.GetProperty("variance").GetDecimal().ShouldBe(-100m);
        closedPayload.GetProperty("closedBy").GetGuid().ShouldBe(cashier.UserId);
        closedPayload.GetProperty("denominations").GetArrayLength().ShouldBe(2);
        var cash = closedPayload.GetProperty("modeTotals").EnumerateArray().Single(total => total.GetProperty("modeCode").GetString() == "CASH");
        cash.GetProperty("expected").GetDecimal().ShouldBe(2000m);
        cash.GetProperty("counted").GetDecimal().ShouldBe(1900m);
        closedPayload.GetProperty("modeTotals").EnumerateArray().Select(total => total.GetProperty("modeCode").GetString())
            .ShouldBe(["BANK_TRANSFER", "CARD", "CASH", "OTHER", "UPI"], "every mode available at the branch has a line");

        // Once. And then never again, through the API or through SQL.
        await Refused(
            cashier.PostAsync($"/api/v1/billing/cashier-sessions/{sessionId}/close", new { denominations = shortSheet, modeTotals = Array.Empty<object>(), reason = "Again." }, Key()),
            HttpStatusCode.Conflict, "billing.cashier-session-already-closed");

        using var scope = fixture.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<BillingDbContext>();
        foreach (var sql in new[]
                 {
                     $"UPDATE billing.cashier_sessions SET variance_reason = 'edited' WHERE id = '{sessionId}'",
                     $"UPDATE billing.cashier_sessions SET counted_total_amount = 2000 WHERE id = '{sessionId}'",
                     $"DELETE FROM billing.cashier_sessions WHERE id = '{sessionId}'",
                     $"UPDATE billing.cashier_session_counts SET quantity = 9 WHERE session_id = '{sessionId}'",
                     $"DELETE FROM billing.cashier_session_mode_totals WHERE session_id = '{sessionId}'",
                 })
        {
            (await Should.ThrowAsync<PostgresException>(() => context.Database.ExecuteSqlRawAsync(sql, Token))).SqlState.ShouldBe(PostgresErrorCodes.RestrictViolation, sql);
        }

        // The event, with the figures and no sheet; the trail, with the two actions the matrix names.
        var events = await context.OutboxMessages.AsNoTracking().Where(message => message.AggregateId == sessionId).Select(message => message.EventType).ToListAsync(Token);
        events.ShouldBe([CashierSessionClosed.Type]);
        var trail = await scope.ServiceProvider.GetRequiredService<PlatformDbContext>().AuditEvents.AsNoTracking()
            .Where(entry => entry.EntityId == sessionId).OrderBy(entry => entry.Sequence).Select(entry => entry.Action).ToListAsync(Token);
        trail.ShouldBe(["payments.open_session", "payments.close_session"]);

        // Closed, the cashier may open the next one.
        (await cashier.PostAsync("/api/v1/billing/cashier-sessions", new { openingFloat = 1500m }, Key())).StatusCode.ShouldBe(HttpStatusCode.Created);
    }

    [Fact]
    public async Task TwoOpensAndTwoClosesAtOnceEachEndWithOneSuccessAndOneConflict()
    {
        Assert.SkipUnless(DatabaseAvailability.IsAvailable, DatabaseAvailability.SkipReason);

        await SeedPaymentModesAsync();
        using var owner = await AdministrationHarness.AdministratorAsync(fixture, "csh-race-owner", "203.0.113.192", IdentityPermissions.Branches);
        var branch = await BillingHarness.OpenBranchAsync(owner);
        using var cashier = await AdministrationHarness.AdministratorAtBranchAsync(fixture, "csh-racer", "203.0.113.193", branch, BillingPermissions.Session);

        var opens = await Task.WhenAll(
            cashier.PostAsync("/api/v1/billing/cashier-sessions", new { openingFloat = 100m }, Key()),
            cashier.PostAsync("/api/v1/billing/cashier-sessions", new { openingFloat = 100m }, Key()));
        opens.Select(response => response.StatusCode).OrderBy(status => status).ShouldBe([HttpStatusCode.Created, HttpStatusCode.Conflict]);
        var sessionId = CreatedId(opens.Single(response => response.StatusCode == HttpStatusCode.Created));

        var sheet = new[] { new { denomination = 100m, quantity = 1 } };
        var closes = await Task.WhenAll(
            cashier.PostAsync($"/api/v1/billing/cashier-sessions/{sessionId}/close", new { denominations = sheet, modeTotals = Array.Empty<object>(), reason = (string?)null }, Key()),
            cashier.PostAsync($"/api/v1/billing/cashier-sessions/{sessionId}/close", new { denominations = sheet, modeTotals = Array.Empty<object>(), reason = (string?)null }, Key()));
        closes.Select(response => response.StatusCode).OrderBy(status => status).ShouldBe([HttpStatusCode.OK, HttpStatusCode.Conflict]);

        using var scope = fixture.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<BillingDbContext>();
        (await context.CashierSessions.AsNoTracking().CountAsync(session => session.Id == sessionId, Token)).ShouldBe(1);
        (await context.OutboxMessages.AsNoTracking().CountAsync(message => message.AggregateId == sessionId, Token)).ShouldBe(1, "one close, one event");
    }

    [Fact]
    public async Task AnotherCashierAnotherBranchAndAnotherPermissionAreEachRefused()
    {
        Assert.SkipUnless(DatabaseAvailability.IsAvailable, DatabaseAvailability.SkipReason);

        await SeedPaymentModesAsync();
        using var owner = await AdministrationHarness.AdministratorAsync(fixture, "csh-deny-owner", "203.0.113.194", IdentityPermissions.Branches);
        var branch = await BillingHarness.OpenBranchAsync(owner);
        var elsewhere = await BillingHarness.OpenBranchAsync(owner);
        using var cashier = await AdministrationHarness.AdministratorAtBranchAsync(fixture, "csh-deny-a", "203.0.113.195", branch, BillingPermissions.Session);
        using var colleague = await AdministrationHarness.AdministratorAtBranchAsync(fixture, "csh-deny-b", "203.0.113.196", branch, BillingPermissions.Session);
        using var stranger = await AdministrationHarness.AdministratorAtBranchAsync(fixture, "csh-deny-c", "203.0.113.197", elsewhere, BillingPermissions.Session);
        using var clerk = await AdministrationHarness.AdministratorAtBranchAsync(fixture, "csh-deny-d", "203.0.113.198", branch, BillingPermissions.CreateInvoice);

        var opened = await cashier.PostAsync("/api/v1/billing/cashier-sessions", new { openingFloat = 0m }, Key());
        opened.StatusCode.ShouldBe(HttpStatusCode.Created, await opened.Content.ReadAsStringAsync(Token));
        var sessionId = CreatedId(opened);
        var empty = new { denominations = Array.Empty<object>(), modeTotals = Array.Empty<object>(), reason = (string?)null };

        // A colleague at the same branch may see it and may open their own, but not close it.
        (await colleague.GetAsync($"/api/v1/billing/cashier-sessions/{sessionId}")).StatusCode.ShouldBe(HttpStatusCode.OK);
        await Refused(colleague.PostAsync($"/api/v1/billing/cashier-sessions/{sessionId}/close", empty, Key()), HttpStatusCode.Forbidden, "billing.cashier-session-not-yours");
        (await colleague.PostAsync("/api/v1/billing/cashier-sessions", new { openingFloat = 0m }, Key())).StatusCode.ShouldBe(HttpStatusCode.Created, "one per cashier, not one per branch");

        // Another branch's session reads as nothing; the permission without the session grant is refused outright.
        (await stranger.GetAsync($"/api/v1/billing/cashier-sessions/{sessionId}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await stranger.PostAsync($"/api/v1/billing/cashier-sessions/{sessionId}/close", empty, Key())).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await clerk.PostAsync("/api/v1/billing/cashier-sessions", new { openingFloat = 0m }, Key())).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await clerk.GetAsync("/api/v1/billing/cashier-sessions")).StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        // The cashier closes an empty drawer with nothing to say.
        (await cashier.PostAsync($"/api/v1/billing/cashier-sessions/{sessionId}/close", empty, Key())).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    private async Task SeedPaymentModesAsync()
    {
        using var scope = fixture.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<IBillingReferenceDataSeeder>().SeedPaymentModesAsync(SessionTestData.OrganisationId, Token);
    }
}
