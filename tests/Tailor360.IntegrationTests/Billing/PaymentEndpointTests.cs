using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Shouldly;
using Tailor360.IntegrationTests.Identity;
using Tailor360.Modules.Billing.Application.Abstractions;
using Tailor360.Modules.Billing.Contracts.Events;
using Tailor360.Modules.Billing.Contracts.Payments;
using Tailor360.Modules.Billing.Infrastructure.Persistence;
using Tailor360.Platform.Persistence.Contexts;
using Tailor360.Platform.Security.Permissions;
using static Tailor360.IntegrationTests.Billing.InvoiceScenes;

namespace Tailor360.IntegrationTests.Billing;

/// <summary>
/// Payments over HTTP (#162): recorded in an open session and allocated at once oldest invoice first, the
/// rest held and applied when the order posts its next invoice; refused without a session, in a mode the
/// branch does not take, without the reference the mode requires, or with a card number for one; the same
/// key replayed, the same reference refused; twenty at once never over-allocate; immutable at the database;
/// the events on the outbox, the actions on the trail, and the balance and the close counting all of it.
/// </summary>
[Trait("Category", "Integration")]
[Collection(WebApplicationCollection.Name)]
public sealed class PaymentEndpointTests(WebApplicationFixture fixture)
{
    private static readonly string RunToken = AdministrationHarness.UniqueToken(6).ToUpperInvariant();

    [Fact]
    public async Task RecordsAllocatesOldestFirstHoldsTheRestAndAppliesItWhenTheNextInvoicePosts()
    {
        Assert.SkipUnless(DatabaseAvailability.IsAvailable, DatabaseAvailability.SkipReason);

        await SeedPaymentModesAsync();
        var scene = await BuildAsync(fixture, "pay-flow", "203.0.113.200", "PAY", RunToken);
        using var cashier = await CashierAsync(fixture, "pay-cashier", "203.0.113.201", scene.Branch,
            BillingPermissions.PostInvoice, BillingPermissions.RecordPayment, BillingPermissions.Session, BillingPermissions.AllocateManual);

        // The modes the counter may take money in: cash needs no reference, UPI does.
        var available = await cashier.GetAsync("/api/v1/billing/payment-modes/available");
        available.StatusCode.ShouldBe(HttpStatusCode.OK, await available.Content.ReadAsStringAsync(Token));
        var modes = JsonDocument.Parse(await available.Content.ReadAsStringAsync(Token)).RootElement.EnumerateArray()
            .ToDictionary(mode => mode.GetProperty("code").GetString()!, mode => mode.GetProperty("requiresReference").GetBoolean());
        modes["CASH"].ShouldBeFalse();
        modes["UPI"].ShouldBeTrue();

        // No session: refused, whatever else is right. Then the session opens and the order's first invoice posts.
        await Refused(cashier.PostAsync("/api/v1/billing/payments", new { orderId = scene.OrderId, modeCode = "CASH", amount = 100m, reference = (string?)null }, Key()),
            HttpStatusCode.Conflict, "billing.cashier-session-required");
        var opened = await cashier.PostAsync("/api/v1/billing/cashier-sessions", new { openingFloat = 500m }, Key());
        opened.StatusCode.ShouldBe(HttpStatusCode.Created, await opened.Content.ReadAsStringAsync(Token));
        var sessionId = CreatedId(opened);

        // One invoice for the first garment: 567. The order's balance says so before any money is taken.
        var (firstInvoice, firstTag) = await DraftOneAsync(scene, cashier, 0);
        (await cashier.PostAsync($"/api/v1/billing/invoices/{firstInvoice}/post", new { reason = (string?)null }, Tagged(firstTag))).StatusCode.ShouldBe(HttpStatusCode.OK);
        var before = await BalanceAsync(cashier, scene.OrderId);
        before.GetProperty("charges").GetDecimal().ShouldBe(567m);
        before.GetProperty("outstanding").GetDecimal().ShouldBe(567m);
        before.GetProperty("unappliedAdvances").GetDecimal().ShouldBe(0m);
        before.GetProperty("invoices")[0].GetProperty("status").GetString().ShouldBe("Unpaid");

        // 1000 in cash: 567 to the invoice, 433 held against the order.
        var key = Key();
        var recorded = await cashier.PostAsync("/api/v1/billing/payments", new { orderId = scene.OrderId, modeCode = "CASH", amount = 1000m, reference = (string?)null }, key);
        recorded.StatusCode.ShouldBe(HttpStatusCode.Created, await recorded.Content.ReadAsStringAsync(Token));
        var paymentId = CreatedId(recorded);
        var payment = JsonDocument.Parse(await recorded.Content.ReadAsStringAsync(Token)).RootElement;
        payment.GetProperty("cashierSessionId").GetGuid().ShouldBe(sessionId);
        payment.GetProperty("customerId").GetGuid().ShouldBe(scene.CustomerId);
        payment.GetProperty("status").GetString().ShouldBe("Recorded");
        payment.GetProperty("allocated").GetDecimal().ShouldBe(567m);
        payment.GetProperty("unappliedAdvance").GetDecimal().ShouldBe(433m);
        var allocation = payment.GetProperty("allocations").EnumerateArray().Single();
        allocation.GetProperty("invoiceId").GetGuid().ShouldBe(firstInvoice);
        allocation.GetProperty("kind").GetString().ShouldBe("Automatic");
        payment.GetProperty("advance").GetProperty("amount").GetDecimal().ShouldBe(433m);

        // The same key again is the same payment, not a second one; the row's own key guard is behind it.
        var replayed = await cashier.PostAsync("/api/v1/billing/payments", new { orderId = scene.OrderId, modeCode = "CASH", amount = 1000m, reference = (string?)null }, key);
        replayed.StatusCode.ShouldBe(HttpStatusCode.Created);
        JsonDocument.Parse(await replayed.Content.ReadAsStringAsync(Token)).RootElement.GetProperty("id").GetGuid().ShouldBe(paymentId);

        var afterFirst = await BalanceAsync(cashier, scene.OrderId);
        afterFirst.GetProperty("allocated").GetDecimal().ShouldBe(567m);
        afterFirst.GetProperty("outstanding").GetDecimal().ShouldBe(0m);
        afterFirst.GetProperty("unappliedAdvances").GetDecimal().ShouldBe(433m);
        afterFirst.GetProperty("invoices")[0].GetProperty("status").GetString().ShouldBe("Paid");

        // The second garment's invoice posts: the rule applies the held 433 to it, and the balance moves.
        var (secondInvoice, secondTag) = await DraftOneAsync(scene, cashier, 1);
        (await cashier.PostAsync($"/api/v1/billing/invoices/{secondInvoice}/post", new { reason = (string?)null }, Tagged(secondTag))).StatusCode.ShouldBe(HttpStatusCode.OK);
        await DispatchAsync(fixture);

        var read = await cashier.GetAsync($"/api/v1/billing/payments/{paymentId}");
        read.StatusCode.ShouldBe(HttpStatusCode.OK, await read.Content.ReadAsStringAsync(Token));
        var applied = JsonDocument.Parse(await read.Content.ReadAsStringAsync(Token)).RootElement;
        applied.GetProperty("unappliedAdvance").GetDecimal().ShouldBe(0m);
        var byRule = applied.GetProperty("allocations").EnumerateArray().Single(line => line.GetProperty("invoiceId").GetGuid() == secondInvoice);
        byRule.GetProperty("kind").GetString().ShouldBe("AdvanceApplied");
        byRule.GetProperty("amount").GetDecimal().ShouldBe(433m);
        byRule.GetProperty("allocatedBy").ValueKind.ShouldBe(JsonValueKind.Null);

        var afterSecond = await BalanceAsync(cashier, scene.OrderId);
        afterSecond.GetProperty("charges").GetDecimal().ShouldBe(1134m);
        afterSecond.GetProperty("allocated").GetDecimal().ShouldBe(1000m);
        afterSecond.GetProperty("outstanding").GetDecimal().ShouldBe(134m);
        afterSecond.GetProperty("unappliedAdvances").GetDecimal().ShouldBe(0m);
        afterSecond.GetProperty("invoices")[1].GetProperty("status").GetString().ShouldBe("PartlyPaid");

        // Nothing is held any more, so a hand allocation has nothing to apply; a UPI payment with its
        // reference settles the rest, and the same reference again is refused.
        await Refused(cashier.PostAsync($"/api/v1/billing/payments/{paymentId}/allocations", new { invoiceId = secondInvoice, amount = 1m, reason = "Try." }, Key()),
            HttpStatusCode.BadRequest, "billing.no-advance-held");
        var reference = $"UPI-{RunToken}-8QX2";
        var settled = await cashier.PostAsync("/api/v1/billing/payments", new { orderId = scene.OrderId, modeCode = "UPI", amount = 134m, reference }, Key());
        settled.StatusCode.ShouldBe(HttpStatusCode.Created, await settled.Content.ReadAsStringAsync(Token));
        await Refused(cashier.PostAsync("/api/v1/billing/payments", new { orderId = scene.OrderId, modeCode = "UPI", amount = 1m, reference }, Key()),
            HttpStatusCode.Conflict, "billing.payment-reference-duplicated");
        (await BalanceAsync(cashier, scene.OrderId)).GetProperty("outstanding").GetDecimal().ShouldBe(0m);

        // The contract other modules read answers the same figures.
        using (var scope = fixture.Services.CreateScope())
        {
            var totals = scope.ServiceProvider.GetRequiredService<IFinancialTotalsQuery>();
            var order = await totals.GetOrderBalanceAsync(scene.OrderId, SessionTestData.OrganisationId, Token);
            order.ShouldNotBeNull();
            order.Outstanding.ShouldBe(0m);
            order.Allocated.ShouldBe(1134m);
            (await totals.GetInvoiceBalanceAsync(secondInvoice, SessionTestData.OrganisationId, Token))!.Status.ShouldBe("Paid");
            (await totals.GetOrderBalanceAsync(Guid.CreateVersion7(), SessionTestData.OrganisationId, Token)).ShouldBeNull();
        }

        // Frozen at the database: no update and no delete gets through, on the payment, its allocations or its advance.
        using (var scope = fixture.Services.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<BillingDbContext>();
            foreach (var sql in new[]
                     {
                         $"UPDATE billing.payments SET amount_amount = 1 WHERE id = '{paymentId}'",
                         $"UPDATE billing.payments SET mode_code = 'CARD' WHERE id = '{paymentId}'",
                         $"DELETE FROM billing.payments WHERE id = '{paymentId}'",
                         $"UPDATE billing.payment_allocations SET amount_amount = 1 WHERE payment_id = '{paymentId}'",
                         $"DELETE FROM billing.payment_allocations WHERE payment_id = '{paymentId}'",
                         $"UPDATE billing.advances SET amount_amount = 1 WHERE payment_id = '{paymentId}'",
                         $"DELETE FROM billing.advances WHERE payment_id = '{paymentId}'",
                     })
            {
                (await Should.ThrowAsync<PostgresException>(() => context.Database.ExecuteSqlRawAsync(sql, Token))).SqlState.ShouldBe(PostgresErrorCodes.RestrictViolation, sql);
            }

            // The events, identifiers and figures only; the trail, the actions the matrix names.
            // As a set: the rows of one save share a millisecond, and a UUIDv7 orders nothing inside one.
            var events = await context.OutboxMessages.AsNoTracking()
                .Where(message => message.AggregateId == paymentId).Select(message => message.EventType).ToListAsync(Token);
            events.ShouldBe([PaymentRecorded.Type, PaymentAllocated.Type, PaymentAllocated.Type], ignoreOrder: true);
            var advanceId = applied.GetProperty("advance").GetProperty("id").GetGuid();
            (await context.OutboxMessages.AsNoTracking().Where(message => message.AggregateId == advanceId).Select(message => message.EventType).ToListAsync(Token))
                .ShouldBe([AdvanceReceived.Type, AdvanceApplied.Type], ignoreOrder: true);
            var statuses = await context.OutboxMessages.AsNoTracking()
                .Where(message => message.EventType == InvoicePaidStatusChanged.Type && (message.AggregateId == firstInvoice || message.AggregateId == secondInvoice))
                .ToListAsync(Token);
            statuses.Count.ShouldBe(3, "Unpaid to Paid on the first; Unpaid to PartlyPaid then PartlyPaid to Paid on the second");
            var trail = await scope.ServiceProvider.GetRequiredService<PlatformDbContext>().AuditEvents.AsNoTracking()
                .Where(entry => entry.EntityId == paymentId).OrderBy(entry => entry.Sequence).ToListAsync(Token);
            trail.Select(entry => entry.Action).ShouldBe(["payments.record"]);
            trail[0].Summary.ShouldNotContain("1000");
            trail[0].After!.ShouldNotContain("1000");
            (await scope.ServiceProvider.GetRequiredService<PlatformDbContext>().AuditEvents.AsNoTracking()
                .Where(entry => entry.EntityId == secondInvoice && entry.Action == "payments.allocate").CountAsync(Token)).ShouldBe(1, "the rule audits its application without a user");
        }

        // The close counts the session's own takings per mode: 1000 cash on top of the 500 float, 134 in UPI.
        var closed = await cashier.PostAsync(
            $"/api/v1/billing/cashier-sessions/{sessionId}/close",
            new { denominations = new[] { new { denomination = 500m, quantity = 3 } }, modeTotals = new[] { new { modeCode = "UPI", counted = 134m } }, reason = (string?)null },
            Key());
        closed.StatusCode.ShouldBe(HttpStatusCode.OK, await closed.Content.ReadAsStringAsync(Token));
        var sheet = JsonDocument.Parse(await closed.Content.ReadAsStringAsync(Token)).RootElement;
        sheet.GetProperty("expectedTotal").GetDecimal().ShouldBe(1634m);
        sheet.GetProperty("variance").GetDecimal().ShouldBe(0m);
        var byMode = sheet.GetProperty("modeTotals").EnumerateArray().ToDictionary(total => total.GetProperty("modeCode").GetString()!, total => total.GetProperty("expected").GetDecimal());
        byMode["CASH"].ShouldBe(1500m);
        byMode["UPI"].ShouldBe(134m);
        byMode["CARD"].ShouldBe(0m);

        // Closed: the next payment is refused until a session opens again.
        await Refused(cashier.PostAsync("/api/v1/billing/payments", new { orderId = scene.OrderId, modeCode = "CASH", amount = 1m, reference = (string?)null }, Key()),
            HttpStatusCode.Conflict, "billing.cashier-session-required");
    }

    [Fact]
    public async Task TwentyPaymentsAtOnceAgainstOneInvoiceNeverOverAllocateIt()
    {
        Assert.SkipUnless(DatabaseAvailability.IsAvailable, DatabaseAvailability.SkipReason);

        await SeedPaymentModesAsync();
        var scene = await BuildAsync(fixture, "pay-race", "203.0.113.202", "RACE", RunToken);
        using var cashier = await CashierAsync(fixture, "pay-racer", "203.0.113.203", scene.Branch, BillingPermissions.PostInvoice, BillingPermissions.RecordPayment, BillingPermissions.Session);
        (await cashier.PostAsync("/api/v1/billing/cashier-sessions", new { openingFloat = 0m }, Key())).StatusCode.ShouldBe(HttpStatusCode.Created);
        var (invoiceId, tag) = await DraftAsync(cashier, scene.OrderId, scene.Reference);
        (await cashier.PostAsync($"/api/v1/billing/invoices/{invoiceId}/post", new { reason = (string?)null }, Tagged(tag))).StatusCode.ShouldBe(HttpStatusCode.OK);

        // Twenty of 100 against 1134 owed: every one is recorded, 1134 is allocated in all and 866 is held.
        var responses = await Task.WhenAll(Enumerable.Range(0, 20).Select(_ =>
            cashier.PostAsync("/api/v1/billing/payments", new { orderId = scene.OrderId, modeCode = "CASH", amount = 100m, reference = (string?)null }, Key())));
        foreach (var response in responses)
        {
            response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync(Token));
        }

        using var scope = fixture.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<BillingDbContext>();
        var allocated = await context.PaymentAllocations.AsNoTracking().Where(allocation => allocation.InvoiceId == invoiceId).SumAsync(allocation => allocation.Amount.Amount, Token);
        allocated.ShouldBe(1134m);
        var held = await context.Advances.AsNoTracking()
            .Join(context.Payments.IgnoreAutoIncludes().Where(payment => payment.OrderId == scene.OrderId), advance => advance.PaymentId, payment => payment.Id, (advance, _) => advance.Amount.Amount)
            .SumAsync(Token);
        held.ShouldBe(866m);

        var balance = await BalanceAsync(cashier, scene.OrderId);
        balance.GetProperty("outstanding").GetDecimal().ShouldBe(0m);
        balance.GetProperty("unappliedAdvances").GetDecimal().ShouldBe(866m);
        balance.GetProperty("invoices")[0].GetProperty("status").GetString().ShouldBe("Paid");
    }

    [Fact]
    public async Task AppliesAnAdvanceByHandUnderStepUpWithAReasonAndWithinWhatIsHeldAndOwed()
    {
        Assert.SkipUnless(DatabaseAvailability.IsAvailable, DatabaseAvailability.SkipReason);

        await SeedPaymentModesAsync();
        var scene = await BuildAsync(fixture, "pay-hand", "203.0.113.204", "HAND", RunToken);
        using var cashier = await CashierAsync(fixture, "pay-hander", "203.0.113.205", scene.Branch, BillingPermissions.PostInvoice, BillingPermissions.RecordPayment, BillingPermissions.Session);
        using var allocator = await AdministrationHarness.AdministratorAtBranchAsync(fixture, "pay-allocator", "203.0.113.206", scene.Branch, BillingPermissions.AllocateManual);
        (await cashier.PostAsync("/api/v1/billing/cashier-sessions", new { openingFloat = 0m }, Key())).StatusCode.ShouldBe(HttpStatusCode.Created);

        // An advance with no invoice posted: the whole 800 is held.
        var recorded = await cashier.PostAsync("/api/v1/billing/payments", new { orderId = scene.OrderId, modeCode = "CASH", amount = 800m, reference = (string?)null }, Key());
        recorded.StatusCode.ShouldBe(HttpStatusCode.Created, await recorded.Content.ReadAsStringAsync(Token));
        var paymentId = CreatedId(recorded);
        JsonDocument.Parse(await recorded.Content.ReadAsStringAsync(Token)).RootElement.GetProperty("unappliedAdvance").GetDecimal().ShouldBe(800m);
        (await BalanceAsync(cashier, scene.OrderId)).GetProperty("unappliedAdvances").GetDecimal().ShouldBe(800m);

        // A draft is not a posted invoice; by hand, the rule is bypassed only for a posted one of the order.
        var (invoiceId, tag) = await DraftOneAsync(scene, cashier, 0);
        await Refused(allocator.PostAsync($"/api/v1/billing/payments/{paymentId}/allocations", new { invoiceId, amount = 100m, reason = "Early." }, Key()),
            HttpStatusCode.BadRequest, "billing.allocation-invoice-not-of-order");

        // Posting applies the rule first: 567 of the 800, leaving 233.
        (await cashier.PostAsync($"/api/v1/billing/invoices/{invoiceId}/post", new { reason = (string?)null }, Tagged(tag))).StatusCode.ShouldBe(HttpStatusCode.OK);
        await DispatchAsync(fixture);
        var (second, secondTag) = await DraftOneAsync(scene, cashier, 1);

        // The rule has not run for the second invoice yet (no dispatch): by hand, 233 goes to it, bounded.
        (await cashier.PostAsync($"/api/v1/billing/invoices/{second}/post", new { reason = (string?)null }, Tagged(secondTag))).StatusCode.ShouldBe(HttpStatusCode.OK);
        await Refused(allocator.PostAsync($"/api/v1/billing/payments/{paymentId}/allocations", new { invoiceId = second, amount = 233.01m, reason = "Too much." }, Key()),
            HttpStatusCode.BadRequest, "billing.advance-exceeded");
        await Refused(allocator.PostAsync($"/api/v1/billing/payments/{paymentId}/allocations", new { invoiceId = second, amount = 100m, reason = (string?)null }, Key()),
            HttpStatusCode.BadRequest, "reason");
        (await cashier.PostAsync($"/api/v1/billing/payments/{paymentId}/allocations", new { invoiceId = second, amount = 100m, reason = "Not mine to do." }, Key()))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden, "the cashier holds no payments.allocate_manual");
        var byHand = await allocator.PostAsync($"/api/v1/billing/payments/{paymentId}/allocations", new { invoiceId = second, amount = 233m, reason = "The customer asked for it to go against the second garment." }, Key());
        byHand.StatusCode.ShouldBe(HttpStatusCode.OK, await byHand.Content.ReadAsStringAsync(Token));
        var payment = JsonDocument.Parse(await byHand.Content.ReadAsStringAsync(Token)).RootElement;
        payment.GetProperty("unappliedAdvance").GetDecimal().ShouldBe(0m);
        payment.GetProperty("allocations").EnumerateArray().Select(line => line.GetProperty("kind").GetString()).ShouldBe(["AdvanceApplied", "Manual"]);

        // The rule then finds nothing left to apply for the second invoice, and applies nothing twice.
        await DispatchAsync(fixture);
        var balance = await BalanceAsync(cashier, scene.OrderId);
        balance.GetProperty("allocated").GetDecimal().ShouldBe(800m);
        balance.GetProperty("outstanding").GetDecimal().ShouldBe(334m);

        using var scope = fixture.Services.CreateScope();
        var trail = await scope.ServiceProvider.GetRequiredService<PlatformDbContext>().AuditEvents.AsNoTracking()
            .Where(entry => entry.EntityId == paymentId).OrderBy(entry => entry.Sequence).ToListAsync(Token);
        trail.Select(entry => entry.Action).ShouldBe(["payments.record", "payments.allocate_manual"]);
        trail[1].Reason.ShouldBe("The customer asked for it to go against the second garment.");
        trail[1].ActorId.ShouldBe(allocator.UserId);
    }

    [Fact]
    public async Task AppliesHeldAdvancesInPostingOrderAndMovesThePaidStatusOnNotesAndCancellations()
    {
        Assert.SkipUnless(DatabaseAvailability.IsAvailable, DatabaseAvailability.SkipReason);

        await SeedPaymentModesAsync();
        var scene = await BuildAsync(fixture, "pay-order", "203.0.113.219", "ORDR", RunToken);
        using var cashier = await CashierAsync(fixture, "pay-orderer", "203.0.113.220", scene.Branch,
            BillingPermissions.PostInvoice, BillingPermissions.RecordPayment, BillingPermissions.Session, BillingPermissions.PostCreditNote, BillingPermissions.CancelInvoice);
        (await cashier.PostAsync("/api/v1/billing/cashier-sessions", new { openingFloat = 0m }, Key())).StatusCode.ShouldBe(HttpStatusCode.Created);

        // 800 held with nothing posted; then two invoices post before either posting event is handled.
        var recorded = await cashier.PostAsync("/api/v1/billing/payments", new { orderId = scene.OrderId, modeCode = "CASH", amount = 800m, reference = (string?)null }, Key());
        recorded.StatusCode.ShouldBe(HttpStatusCode.Created, await recorded.Content.ReadAsStringAsync(Token));
        var paymentId = CreatedId(recorded);
        var (first, firstTag) = await DraftOneAsync(scene, cashier, 0);
        (await cashier.PostAsync($"/api/v1/billing/invoices/{first}/post", new { reason = (string?)null }, Tagged(firstTag))).StatusCode.ShouldBe(HttpStatusCode.OK);
        var (second, secondTag) = await DraftOneAsync(scene, cashier, 1);
        (await cashier.PostAsync($"/api/v1/billing/invoices/{second}/post", new { reason = (string?)null }, Tagged(secondTag))).StatusCode.ShouldBe(HttpStatusCode.OK);
        await DispatchAsync(fixture);

        // The rule is the order's, not the event's: the older invoice takes 567, the newer the 233 left,
        // whichever event the dispatcher handled first.
        var payment = JsonDocument.Parse(await (await cashier.GetAsync($"/api/v1/billing/payments/{paymentId}")).Content.ReadAsStringAsync(Token)).RootElement;
        payment.GetProperty("unappliedAdvance").GetDecimal().ShouldBe(0m);
        payment.GetProperty("allocations").EnumerateArray().Select(line => (line.GetProperty("invoiceId").GetGuid(), line.GetProperty("amount").GetDecimal()))
            .ShouldBe([(first, 567m), (second, 233m)]);
        var balance = await BalanceAsync(cashier, scene.OrderId);
        balance.GetProperty("invoices")[0].GetProperty("status").GetString().ShouldBe("Paid");
        balance.GetProperty("invoices")[1].GetProperty("status").GetString().ShouldBe("PartlyPaid");
        balance.GetProperty("outstanding").GetDecimal().ShouldBe(334m);

        // A credit note relieving the second's whole line pays it (567 credited against 334 owed floors at
        // zero); the cancellation of the first, paid in full, leaves it Paid with the money on it until
        // E09-F03-3 refunds it. Each move goes on the outbox as a status change.
        var credited = await cashier.PostAsync($"/api/v1/billing/invoices/{second}/credit-notes", new { lines = new[] { new { garmentJobId = scene.Jobs[1], taxableValue = 540m } }, reason = "Garment not made." }, Key());
        credited.IsSuccessStatusCode.ShouldBeTrue(await credited.Content.ReadAsStringAsync(Token));
        (await BalanceAsync(cashier, scene.OrderId)).GetProperty("invoices")[1].GetProperty("status").GetString().ShouldBe("Paid");
        var cancelled = await cashier.PostAsync($"/api/v1/billing/invoices/{first}/cancel", new { reason = "Issued to the wrong customer." }, Key());
        cancelled.StatusCode.ShouldBe(HttpStatusCode.OK, await cancelled.Content.ReadAsStringAsync(Token));

        using var scope = fixture.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<BillingDbContext>();
        var statuses = await context.OutboxMessages.AsNoTracking()
            .Where(message => message.EventType == InvoicePaidStatusChanged.Type && (message.AggregateId == first || message.AggregateId == second))
            .OrderBy(message => message.Id)
            .Select(message => new { message.AggregateId, message.Payload })
            .ToListAsync(Token);
        statuses.Where(status => status.AggregateId == first).Select(status => Transition(status.Payload)).ShouldBe(["Unpaid>Paid"], "posted, paid by the advance, cancelled with money on it: Paid stays Paid");
        statuses.Where(status => status.AggregateId == second).Select(status => Transition(status.Payload)).ShouldBe(["Unpaid>PartlyPaid", "PartlyPaid>Paid"], "the advance, then the credit note");

        static string Transition(string payload)
        {
            var root = JsonDocument.Parse(payload).RootElement;
            return $"{root.GetProperty("previousStatus").GetString()}>{root.GetProperty("status").GetString()}";
        }
    }

    [Fact]
    public async Task RefusesTheWrongModeTheMissingReferenceTheCardNumberTheStrangerAndTheOtherBranch()
    {
        Assert.SkipUnless(DatabaseAvailability.IsAvailable, DatabaseAvailability.SkipReason);

        await SeedPaymentModesAsync();
        var scene = await BuildAsync(fixture, "pay-deny", "203.0.113.207", "DENY", RunToken);
        using var cashier = await CashierAsync(fixture, "pay-denied", "203.0.113.208", scene.Branch, BillingPermissions.RecordPayment, BillingPermissions.Session);
        using var clerk = await AdministrationHarness.AdministratorAtBranchAsync(fixture, "pay-clerk", "203.0.113.209", scene.Branch, BillingPermissions.CreateInvoice);
        var elsewhere = await BillingHarness.OpenBranchAsync(scene.Owner);
        using var stranger = await AdministrationHarness.AdministratorAtBranchAsync(fixture, "pay-stranger", "203.0.113.210", elsewhere, BillingPermissions.RecordPayment, [BillingPermissions.Session]);
        (await cashier.PostAsync("/api/v1/billing/cashier-sessions", new { openingFloat = 0m }, Key())).StatusCode.ShouldBe(HttpStatusCode.Created);
        (await stranger.PostAsync("/api/v1/billing/cashier-sessions", new { openingFloat = 0m }, Key())).StatusCode.ShouldBe(HttpStatusCode.Created);

        string Path() => "/api/v1/billing/payments";
        await Refused(cashier.PostAsync(Path(), new { orderId = scene.OrderId, modeCode = "CASH", amount = 0m, reference = (string?)null }, Key()), HttpStatusCode.BadRequest, "billing.amount-not-well-formed");
        await Refused(cashier.PostAsync(Path(), new { orderId = scene.OrderId, modeCode = "CASH", amount = 10.001m, reference = (string?)null }, Key()), HttpStatusCode.BadRequest, "billing.amount-not-well-formed");
        await Refused(cashier.PostAsync(Path(), new { orderId = scene.OrderId, modeCode = "CHEQUE", amount = 10m, reference = "x" }, Key()), HttpStatusCode.BadRequest, "billing.payment-mode-not-known");
        await Refused(cashier.PostAsync(Path(), new { orderId = scene.OrderId, modeCode = "UPI", amount = 10m, reference = (string?)null }, Key()), HttpStatusCode.BadRequest, "billing.reference-required");
        await Refused(cashier.PostAsync(Path(), new { orderId = scene.OrderId, modeCode = "CARD", amount = 10m, reference = "4111 1111 1111 1111" }, Key()), HttpStatusCode.BadRequest, "billing.reference-looks-like-a-card");
        await Refused(cashier.PostAsync(Path(), new { orderId = Guid.CreateVersion7(), modeCode = "CASH", amount = 10m, reference = (string?)null }, Key()), HttpStatusCode.NotFound, "billing.order-not-known");
        await Refused(cashier.PostAsync(Path(), new { orderId = scene.OrderId, modeCode = "CASH", amount = 10m, reference = (string?)null }), HttpStatusCode.BadRequest, "Idempotency-Key");
        (await clerk.PostAsync(Path(), new { orderId = scene.OrderId, modeCode = "CASH", amount = 10m, reference = (string?)null }, Key())).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await clerk.GetAsync("/api/v1/billing/payment-modes/available")).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await clerk.GetAsync($"/api/v1/billing/orders/{scene.OrderId}/balance")).StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        // The stranger's branch is not the order's: refused; and the order's balance is not theirs to read.
        await Refused(stranger.PostAsync(Path(), new { orderId = scene.OrderId, modeCode = "CASH", amount = 10m, reference = (string?)null }, Key()), HttpStatusCode.Forbidden, "billing.order-at-another-branch");
        (await stranger.GetAsync($"/api/v1/billing/orders/{scene.OrderId}/balance")).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        // A mode restricted to another branch is not available here, and the available list leaves it out.
        var card = JsonDocument.Parse(await (await scene.Owner.GetAsync("/api/v1/billing/payment-modes")).Content.ReadAsStringAsync(Token)).RootElement
            .EnumerateArray().Single(mode => mode.GetProperty("code").GetString() == "CARD").GetProperty("id").GetGuid();
        var read = await scene.Owner.GetAsync($"/api/v1/billing/payment-modes/{card}");
        var restricted = await scene.Owner.PutAsync($"/api/v1/billing/payment-modes/{card}",
            new { name = "Card", requiresReference = true, requiresProvider = false, allowedForRefund = false, isActive = true, branchIds = new[] { elsewhere } },
            Tagged(read.Headers.ETag!.ToString()));
        restricted.StatusCode.ShouldBe(HttpStatusCode.OK, await restricted.Content.ReadAsStringAsync(Token));
        try
        {
            await Refused(cashier.PostAsync(Path(), new { orderId = scene.OrderId, modeCode = "CARD", amount = 10m, reference = "A1B2C3" }, Key()), HttpStatusCode.BadRequest, "billing.payment-mode-not-available");
            JsonDocument.Parse(await (await cashier.GetAsync("/api/v1/billing/payment-modes/available")).Content.ReadAsStringAsync(Token)).RootElement
                .EnumerateArray().Select(mode => mode.GetProperty("code").GetString()).ShouldNotContain("CARD");
        }
        finally
        {
            var again = await scene.Owner.GetAsync($"/api/v1/billing/payment-modes/{card}");
            (await scene.Owner.PutAsync($"/api/v1/billing/payment-modes/{card}",
                new { name = "Card", requiresReference = true, requiresProvider = false, allowedForRefund = false, isActive = true, branchIds = Array.Empty<Guid>() },
                Tagged(again.Headers.ETag!.ToString()))).StatusCode.ShouldBe(HttpStatusCode.OK);
        }

        // A payment at the other branch reads as 404 here; nothing was recorded at this branch.
        var theirs = await stranger.PostAsync(Path(), new { orderId = scene.OrderId, modeCode = "CASH", amount = 10m, reference = (string?)null }, Key());
        theirs.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await cashier.GetAsync($"/api/v1/billing/payments/{Guid.CreateVersion7()}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    private static async Task<JsonElement> BalanceAsync(AdministrationHarness.AdministratorClient client, Guid orderId)
    {
        var response = await client.GetAsync($"/api/v1/billing/orders/{orderId}/balance");
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(Token));
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync(Token)).RootElement;
    }

    /// <summary>One garment of the scene priced and drafted on its own, so the order posts two invoices of 567 in turn.</summary>
    private async Task<(Guid InvoiceId, string Tag)> DraftOneAsync(InvoiceScene scene, AdministrationHarness.AdministratorClient cashier, int job)
    {
        var reference = $"order:{scene.OrderId:N}:job{job + 1}";
        await PriceAsync(fixture, scene.Branch, reference, [scene.Jobs[job].ToString()], scene.ItemCode, scene.SurchargeCode);
        return await DraftAsync(cashier, scene.OrderId, reference);
    }

    private async Task SeedPaymentModesAsync()
    {
        using var scope = fixture.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<IBillingReferenceDataSeeder>().SeedPaymentModesAsync(SessionTestData.OrganisationId, Token);
    }
}
