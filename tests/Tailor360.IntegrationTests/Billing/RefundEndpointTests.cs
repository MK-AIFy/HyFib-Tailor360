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
/// The compensating records over HTTP (#163): a reversal releases a payment's allocations and advance and
/// the balance is recomputed from rows, once per payment and never after a refund; a refund pays back an
/// advance or an invoice's surplus in the caller's open session through a mode allowed for it, never
/// beyond what the source holds; both under step-up with a reason, both immutable at the database, both on
/// the outbox with the paid status they move; and the session closes to totals by mode that equal the rows.
/// </summary>
[Trait("Category", "Integration")]
[Collection(WebApplicationCollection.Name)]
public sealed class RefundEndpointTests(WebApplicationFixture fixture)
{
    private static readonly string RunToken = AdministrationHarness.UniqueToken(6).ToUpperInvariant();

    [Fact]
    public async Task ReversesAPaymentOnceReleasingItsAllocationsAndRecomputingTheBalance()
    {
        Assert.SkipUnless(DatabaseAvailability.IsAvailable, DatabaseAvailability.SkipReason);

        await SeedPaymentModesAsync();
        var scene = await BuildAsync(fixture, "rev-flow", "203.0.113.221", "REV", RunToken);
        using var cashier = await CashierAsync(fixture, "rev-cashier", "203.0.113.222", scene.Branch,
            BillingPermissions.PostInvoice, BillingPermissions.RecordPayment, BillingPermissions.Session, BillingPermissions.Reverse, BillingPermissions.Refund);
        (await cashier.PostAsync("/api/v1/billing/cashier-sessions", new { openingFloat = 0m }, Key())).StatusCode.ShouldBe(HttpStatusCode.Created);
        var (invoiceId, tag) = await DraftAsync(cashier, scene.OrderId, scene.Reference);
        (await cashier.PostAsync($"/api/v1/billing/invoices/{invoiceId}/post", new { reason = (string?)null }, Tagged(tag))).StatusCode.ShouldBe(HttpStatusCode.OK);

        // 1500 against 1134 owed: paid, 366 held.
        var recorded = await cashier.PostAsync("/api/v1/billing/payments", new { orderId = scene.OrderId, modeCode = "CASH", amount = 1500m, reference = (string?)null }, Key());
        recorded.StatusCode.ShouldBe(HttpStatusCode.Created, await recorded.Content.ReadAsStringAsync(Token));
        var paymentId = CreatedId(recorded);
        (await BalanceAsync(cashier, scene.OrderId)).GetProperty("invoices")[0].GetProperty("status").GetString().ShouldBe("Paid");

        // Reversed, with a reason: the row stands, the allocation is released, the invoice is Unpaid again.
        await Refused(cashier.PostAsync($"/api/v1/billing/payments/{paymentId}/reversal", new { reason = (string?)null }, Key()), HttpStatusCode.BadRequest, "billing.reason-required");
        var reversed = await cashier.PostAsync($"/api/v1/billing/payments/{paymentId}/reversal", new { reason = "The note was counterfeit; the customer is paying again." }, Key());
        reversed.StatusCode.ShouldBe(HttpStatusCode.OK, await reversed.Content.ReadAsStringAsync(Token));
        var payment = JsonDocument.Parse(await reversed.Content.ReadAsStringAsync(Token)).RootElement;
        payment.GetProperty("reversal").GetProperty("reason").GetString().ShouldBe("The note was counterfeit; the customer is paying again.");
        payment.GetProperty("allocated").GetDecimal().ShouldBe(0m);
        payment.GetProperty("unappliedAdvance").GetDecimal().ShouldBe(0m);
        payment.GetProperty("allocations").GetArrayLength().ShouldBe(1, "the allocation row stays; it counts for nothing");
        payment.GetProperty("amount").GetDecimal().ShouldBe(1500m, "the original row is untouched");

        var balance = await BalanceAsync(cashier, scene.OrderId);
        balance.GetProperty("allocated").GetDecimal().ShouldBe(0m);
        balance.GetProperty("outstanding").GetDecimal().ShouldBe(1134m);
        balance.GetProperty("unappliedAdvances").GetDecimal().ShouldBe(0m);
        balance.GetProperty("invoices")[0].GetProperty("status").GetString().ShouldBe("Unpaid");

        // Once; and the released advance is nothing to pay back.
        await Refused(cashier.PostAsync($"/api/v1/billing/payments/{paymentId}/reversal", new { reason = "Again." }, Key()), HttpStatusCode.Conflict, "billing.payment-already-reversed");
        await Refused(cashier.PostAsync("/api/v1/billing/refunds", new { paymentId, modeCode = "CASH", amount = 1m, reason = "Back." }, Key()), HttpStatusCode.BadRequest, "billing.refund-exceeds-refundable");

        // The customer pays again in UPI: the invoice is paid from that payment, the reversed one stays out of every sum.
        var again = await cashier.PostAsync("/api/v1/billing/payments", new { orderId = scene.OrderId, modeCode = "UPI", amount = 1134m, reference = $"UPI-{RunToken}-RV01" }, Key());
        again.StatusCode.ShouldBe(HttpStatusCode.Created, await again.Content.ReadAsStringAsync(Token));
        (await BalanceAsync(cashier, scene.OrderId)).GetProperty("invoices")[0].GetProperty("status").GetString().ShouldBe("Paid");

        using var scope = fixture.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<BillingDbContext>();
        foreach (var sql in new[]
                 {
                     $"UPDATE billing.payment_reversals SET reason = 'edited' WHERE payment_id = '{paymentId}'",
                     $"DELETE FROM billing.payment_reversals WHERE payment_id = '{paymentId}'",
                 })
        {
            (await Should.ThrowAsync<PostgresException>(() => context.Database.ExecuteSqlRawAsync(sql, Token))).SqlState.ShouldBe(PostgresErrorCodes.RestrictViolation, sql);
        }

        (await context.OutboxMessages.AsNoTracking().Where(message => message.AggregateId == paymentId).Select(message => message.EventType).ToListAsync(Token))
            .ShouldContain(PaymentReversed.Type);
        var statuses = await context.OutboxMessages.AsNoTracking()
            .Where(message => message.EventType == InvoicePaidStatusChanged.Type && message.AggregateId == invoiceId)
            .Select(message => message.Payload).ToListAsync(Token);
        statuses.Select(Transition).Order().ToList().ShouldBe(["Paid>Unpaid", "Unpaid>Paid", "Unpaid>Paid"], customMessage: "paid, reversed to unpaid, paid again");
        var trail = await scope.ServiceProvider.GetRequiredService<PlatformDbContext>().AuditEvents.AsNoTracking()
            .Where(entry => entry.EntityId == paymentId).OrderBy(entry => entry.Sequence).ToListAsync(Token);
        trail.Select(entry => entry.Action).ShouldBe(["payments.record", "payments.reverse"]);
        trail[1].Reason.ShouldBe("The note was counterfeit; the customer is paying again.");
        trail[1].Summary.ShouldNotContain("1500");
    }

    [Fact]
    public async Task TwoReversalsAtOnceEndWithOneRecordAndOneConflict()
    {
        Assert.SkipUnless(DatabaseAvailability.IsAvailable, DatabaseAvailability.SkipReason);

        await SeedPaymentModesAsync();
        var scene = await BuildAsync(fixture, "rev-race", "203.0.113.223", "RVRC", RunToken);
        using var cashier = await CashierAsync(fixture, "rev-racer", "203.0.113.224", scene.Branch, BillingPermissions.RecordPayment, BillingPermissions.Session, BillingPermissions.Reverse);
        (await cashier.PostAsync("/api/v1/billing/cashier-sessions", new { openingFloat = 0m }, Key())).StatusCode.ShouldBe(HttpStatusCode.Created);
        var recorded = await cashier.PostAsync("/api/v1/billing/payments", new { orderId = scene.OrderId, modeCode = "CASH", amount = 200m, reference = (string?)null }, Key());
        var paymentId = CreatedId(recorded);

        var responses = await Task.WhenAll(
            cashier.PostAsync($"/api/v1/billing/payments/{paymentId}/reversal", new { reason = "Never cleared." }, Key()),
            cashier.PostAsync($"/api/v1/billing/payments/{paymentId}/reversal", new { reason = "Never cleared." }, Key()));
        responses.Select(response => response.StatusCode).OrderBy(status => status).ShouldBe([HttpStatusCode.OK, HttpStatusCode.Conflict]);

        using var scope = fixture.Services.CreateScope();
        (await scope.ServiceProvider.GetRequiredService<BillingDbContext>().PaymentReversals.AsNoTracking().CountAsync(reversal => reversal.PaymentId == paymentId, Token)).ShouldBe(1);
    }

    [Fact]
    public async Task RefundsAnAdvanceAndAnInvoicesSurplusWithinWhatEachHoldsAndTheSessionClosesToTheRows()
    {
        Assert.SkipUnless(DatabaseAvailability.IsAvailable, DatabaseAvailability.SkipReason);

        await SeedPaymentModesAsync();
        var scene = await BuildAsync(fixture, "ref-flow", "203.0.113.225", "REFD", RunToken);
        using var cashier = await CashierAsync(fixture, "ref-cashier", "203.0.113.226", scene.Branch,
            BillingPermissions.PostInvoice, BillingPermissions.RecordPayment, BillingPermissions.Session, BillingPermissions.Refund, BillingPermissions.Reverse, BillingPermissions.PostCreditNote);
        var opened = await cashier.PostAsync("/api/v1/billing/cashier-sessions", new { openingFloat = 500m }, Key());
        opened.StatusCode.ShouldBe(HttpStatusCode.Created, await opened.Content.ReadAsStringAsync(Token));
        var sessionId = CreatedId(opened);

        // No refund without a source, with both, or before anything was taken.
        await Refused(cashier.PostAsync("/api/v1/billing/refunds", new { modeCode = "CASH", amount = 10m, reason = "Back." }, Key()), HttpStatusCode.BadRequest, "billing.refund-source-not-well-formed");
        await Refused(cashier.PostAsync("/api/v1/billing/refunds", new { paymentId = Guid.CreateVersion7(), invoiceId = Guid.CreateVersion7(), modeCode = "CASH", amount = 10m, reason = "Back." }, Key()), HttpStatusCode.BadRequest, "billing.refund-source-not-well-formed");
        await Refused(cashier.PostAsync("/api/v1/billing/refunds", new { paymentId = Guid.CreateVersion7(), modeCode = "CASH", amount = 10m, reason = "Back." }, Key()), HttpStatusCode.NotFound, "billing.payment-not-found");

        // An 800 advance with no invoice; 300 of it paid back in cash, not in UPI (not allowed for refund), not 501.
        var advance = await cashier.PostAsync("/api/v1/billing/payments", new { orderId = scene.OrderId, modeCode = "CASH", amount = 800m, reference = (string?)null }, Key());
        advance.StatusCode.ShouldBe(HttpStatusCode.Created, await advance.Content.ReadAsStringAsync(Token));
        var advanceId = CreatedId(advance);
        await Refused(cashier.PostAsync("/api/v1/billing/refunds", new { paymentId = advanceId, modeCode = "UPI", amount = 300m, reference = "UPI-1", reason = "Back." }, Key()), HttpStatusCode.BadRequest, "billing.payment-mode-not-for-refund");
        await Refused(cashier.PostAsync("/api/v1/billing/refunds", new { paymentId = advanceId, modeCode = "CASH", amount = 800.01m, reason = "Back." }, Key()), HttpStatusCode.BadRequest, "billing.refund-exceeds-refundable");
        await Refused(cashier.PostAsync("/api/v1/billing/refunds", new { paymentId = advanceId, modeCode = "CASH", amount = 300m, reason = (string?)null }, Key()), HttpStatusCode.BadRequest, "billing.reason-required");
        var refunded = await cashier.PostAsync("/api/v1/billing/refunds", new { paymentId = advanceId, modeCode = "CASH", amount = 300m, reason = "One garment was dropped from the order." }, Key());
        refunded.StatusCode.ShouldBe(HttpStatusCode.Created, await refunded.Content.ReadAsStringAsync(Token));
        var refundId = CreatedId(refunded);
        var refund = JsonDocument.Parse(await refunded.Content.ReadAsStringAsync(Token)).RootElement;
        refund.GetProperty("source").GetString().ShouldBe("Advance");
        refund.GetProperty("paymentId").GetGuid().ShouldBe(advanceId);
        refund.GetProperty("cashierSessionId").GetGuid().ShouldBe(sessionId);
        (await cashier.GetAsync($"/api/v1/billing/refunds/{refundId}")).StatusCode.ShouldBe(HttpStatusCode.OK);

        // 500 is still held; a second refund is bounded by it; the reversal of a refunded payment is refused.
        var read = JsonDocument.Parse(await (await cashier.GetAsync($"/api/v1/billing/payments/{advanceId}")).Content.ReadAsStringAsync(Token)).RootElement;
        read.GetProperty("unappliedAdvance").GetDecimal().ShouldBe(500m);
        read.GetProperty("refundedFromAdvance").GetDecimal().ShouldBe(300m);
        (await BalanceAsync(cashier, scene.OrderId)).GetProperty("unappliedAdvances").GetDecimal().ShouldBe(500m);
        await Refused(cashier.PostAsync("/api/v1/billing/refunds", new { paymentId = advanceId, modeCode = "CASH", amount = 500.01m, reason = "Back." }, Key()), HttpStatusCode.BadRequest, "billing.refund-exceeds-refundable");
        await Refused(cashier.PostAsync($"/api/v1/billing/payments/{advanceId}/reversal", new { reason = "Never cleared." }, Key()), HttpStatusCode.Conflict, "billing.payment-refunded");

        // An invoice posts and takes the 500 by the rule; a full credit note leaves 500 on it beyond what it
        // charges, which is what the invoice source pays back — 500, not 501.
        var (invoiceId, tag) = await DraftAsync(cashier, scene.OrderId, scene.Reference);
        (await cashier.PostAsync($"/api/v1/billing/invoices/{invoiceId}/post", new { reason = (string?)null }, Tagged(tag))).StatusCode.ShouldBe(HttpStatusCode.OK);
        await DispatchAsync(fixture);
        await Refused(cashier.PostAsync("/api/v1/billing/refunds", new { invoiceId, modeCode = "CASH", amount = 1m, reason = "Back." }, Key()), HttpStatusCode.BadRequest, "billing.refund-exceeds-refundable");
        var credited = await cashier.PostAsync($"/api/v1/billing/invoices/{invoiceId}/credit-notes",
            new { lines = new[] { new { garmentJobId = scene.Jobs[0], taxableValue = 540m }, new { garmentJobId = scene.Jobs[1], taxableValue = 540m } }, reason = "The order was cancelled." }, Key());
        credited.IsSuccessStatusCode.ShouldBeTrue(await credited.Content.ReadAsStringAsync(Token));
        var surplus = await BalanceAsync(cashier, scene.OrderId);
        surplus.GetProperty("invoices")[0].GetProperty("allocated").GetDecimal().ShouldBe(500m);
        surplus.GetProperty("invoices")[0].GetProperty("outstanding").GetDecimal().ShouldBe(0m);
        await Refused(cashier.PostAsync("/api/v1/billing/refunds", new { invoiceId, modeCode = "CASH", amount = 500.01m, reason = "Back." }, Key()), HttpStatusCode.BadRequest, "billing.refund-exceeds-refundable");
        var refundedInvoice = await cashier.PostAsync("/api/v1/billing/refunds", new { invoiceId, modeCode = "CASH", amount = 500m, reason = "The order was cancelled; the advance is returned." }, Key());
        refundedInvoice.StatusCode.ShouldBe(HttpStatusCode.Created, await refundedInvoice.Content.ReadAsStringAsync(Token));
        var after = await BalanceAsync(cashier, scene.OrderId);
        after.GetProperty("invoices")[0].GetProperty("refunds").GetDecimal().ShouldBe(500m);
        after.GetProperty("outstanding").GetDecimal().ShouldBe(0m);
        after.GetProperty("unappliedAdvances").GetDecimal().ShouldBe(0m);

        // The close counts the rows: 500 float + 800 in - 300 out - 500 out = 500 cash.
        var closed = await cashier.PostAsync(
            $"/api/v1/billing/cashier-sessions/{sessionId}/close",
            new { denominations = new[] { new { denomination = 500m, quantity = 1 } }, modeTotals = Array.Empty<object>(), reason = (string?)null },
            Key());
        closed.StatusCode.ShouldBe(HttpStatusCode.OK, await closed.Content.ReadAsStringAsync(Token));
        var sheet = JsonDocument.Parse(await closed.Content.ReadAsStringAsync(Token)).RootElement;
        sheet.GetProperty("expectedTotal").GetDecimal().ShouldBe(500m);
        sheet.GetProperty("variance").GetDecimal().ShouldBe(0m);
        sheet.GetProperty("modeTotals").EnumerateArray().Single(total => total.GetProperty("modeCode").GetString() == "CASH").GetProperty("expected").GetDecimal().ShouldBe(500m);

        using var scope = fixture.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<BillingDbContext>();
        foreach (var sql in new[]
                 {
                     $"UPDATE billing.refunds SET amount_amount = 1 WHERE id = '{refundId}'",
                     $"DELETE FROM billing.refunds WHERE id = '{refundId}'",
                 })
        {
            (await Should.ThrowAsync<PostgresException>(() => context.Database.ExecuteSqlRawAsync(sql, Token))).SqlState.ShouldBe(PostgresErrorCodes.RestrictViolation, sql);
        }

        (await context.OutboxMessages.AsNoTracking().Where(message => message.AggregateId == refundId).Select(message => message.EventType).ToListAsync(Token))
            .ShouldBe([RefundRecorded.Type]);
        var trail = await scope.ServiceProvider.GetRequiredService<PlatformDbContext>().AuditEvents.AsNoTracking()
            .Where(entry => entry.EntityId == refundId).ToListAsync(Token);
        trail.Select(entry => entry.Action).ShouldBe(["payments.refund"]);
        trail[0].Summary.ShouldNotContain("300");
        trail[0].Reason.ShouldBe("One garment was dropped from the order.");
    }

    [Fact]
    public async Task AStrangerAClerkAndAClosedSessionAreEachRefused()
    {
        Assert.SkipUnless(DatabaseAvailability.IsAvailable, DatabaseAvailability.SkipReason);

        await SeedPaymentModesAsync();
        var scene = await BuildAsync(fixture, "ref-deny", "203.0.113.227", "RFDN", RunToken);
        using var cashier = await CashierAsync(fixture, "ref-denied", "203.0.113.228", scene.Branch, BillingPermissions.RecordPayment, BillingPermissions.Session, BillingPermissions.Refund);
        using var clerk = await AdministrationHarness.AdministratorAtBranchAsync(fixture, "ref-clerk", "203.0.113.229", scene.Branch, BillingPermissions.RecordPayment);
        var elsewhere = await BillingHarness.OpenBranchAsync(scene.Owner);
        using var stranger = await AdministrationHarness.AdministratorAtBranchAsync(fixture, "ref-stranger", "203.0.113.230", elsewhere, BillingPermissions.Refund, [BillingPermissions.Reverse, BillingPermissions.Session]);
        (await cashier.PostAsync("/api/v1/billing/cashier-sessions", new { openingFloat = 0m }, Key())).StatusCode.ShouldBe(HttpStatusCode.Created);
        (await stranger.PostAsync("/api/v1/billing/cashier-sessions", new { openingFloat = 0m }, Key())).StatusCode.ShouldBe(HttpStatusCode.Created);
        var recorded = await cashier.PostAsync("/api/v1/billing/payments", new { orderId = scene.OrderId, modeCode = "CASH", amount = 100m, reference = (string?)null }, Key());
        var paymentId = CreatedId(recorded);

        (await clerk.PostAsync("/api/v1/billing/refunds", new { paymentId, modeCode = "CASH", amount = 10m, reason = "Back." }, Key())).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await clerk.PostAsync($"/api/v1/billing/payments/{paymentId}/reversal", new { reason = "Never." }, Key())).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await stranger.PostAsync($"/api/v1/billing/payments/{paymentId}/reversal", new { reason = "Never." }, Key())).StatusCode.ShouldBe(HttpStatusCode.NotFound, "another branch's payment");
        await Refused(stranger.PostAsync("/api/v1/billing/refunds", new { paymentId, modeCode = "CASH", amount = 10m, reason = "Back." }, Key()), HttpStatusCode.Forbidden, "billing.order-at-another-branch");

        // No open session, no refund: the money out has to reconcile to a drawer.
        var sessions = JsonDocument.Parse(await (await cashier.GetAsync("/api/v1/billing/cashier-sessions?status=open")).Content.ReadAsStringAsync(Token)).RootElement;
        var sessionId = sessions.EnumerateArray().First().GetProperty("id").GetGuid();
        (await cashier.PostAsync($"/api/v1/billing/cashier-sessions/{sessionId}/close", new { denominations = new[] { new { denomination = 100m, quantity = 1 } }, modeTotals = Array.Empty<object>(), reason = (string?)null }, Key())).StatusCode.ShouldBe(HttpStatusCode.OK);
        await Refused(cashier.PostAsync("/api/v1/billing/refunds", new { paymentId, modeCode = "CASH", amount = 10m, reason = "Back." }, Key()), HttpStatusCode.Conflict, "billing.cashier-session-required");
    }

    private static string Transition(string payload)
    {
        var root = JsonDocument.Parse(payload).RootElement;
        return $"{root.GetProperty("previousStatus").GetString()}>{root.GetProperty("status").GetString()}";
    }

    private static async Task<JsonElement> BalanceAsync(AdministrationHarness.AdministratorClient client, Guid orderId)
    {
        var response = await client.GetAsync($"/api/v1/billing/orders/{orderId}/balance");
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(Token));
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync(Token)).RootElement;
    }

    private async Task SeedPaymentModesAsync()
    {
        using var scope = fixture.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<IBillingReferenceDataSeeder>().SeedPaymentModesAsync(SessionTestData.OrganisationId, Token);
    }
}
