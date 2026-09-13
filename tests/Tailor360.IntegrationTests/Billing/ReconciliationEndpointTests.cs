using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Shouldly;
using Tailor360.IntegrationTests.Identity;
using Tailor360.Modules.Billing.Application.Abstractions;
using Tailor360.Modules.Billing.Contracts.Events;
using Tailor360.Modules.Billing.Domain.Payments;
using Tailor360.Modules.Billing.Infrastructure.Persistence;
using Tailor360.Platform.Persistence.Contexts;
using Tailor360.Platform.Security.Permissions;
using static Tailor360.IntegrationTests.Billing.InvoiceScenes;

namespace Tailor360.IntegrationTests.Billing;

/// <summary>
/// The reconciliation batch a close opens over HTTP (#172): a variance beyond the threshold leaves a
/// pending batch that the closing cashier cannot approve and a different holder of
/// <c>payments.approve_reconciliation</c> can, once, with a reason and a fresh step-up; a count that
/// agreed leaves a batch that needs no approval and refuses one; both cases are immutable at the
/// database and the approval carries its own event and trail entry.
/// </summary>
[Trait("Category", "Integration")]
[Collection(WebApplicationCollection.Name)]
public sealed class ReconciliationEndpointTests(WebApplicationFixture fixture)
{
    [Fact]
    public async Task AVarianceBeyondTheThresholdIsApprovedOnceByADifferentUserAndRefusedTheRestOfTheWays()
    {
        Assert.SkipUnless(DatabaseAvailability.IsAvailable, DatabaseAvailability.SkipReason);

        await SeedPaymentModesAsync();
        using var owner = await AdministrationHarness.AdministratorAsync(fixture, "rec-owner", "203.0.113.240", IdentityPermissions.Branches);
        var branch = await BillingHarness.OpenBranchAsync(owner);
        // The cashier also holds the approval permission, so their own refusal below is the domain's
        // "not this person" check (INV-CSH-04) and not merely a permission they lack.
        using var cashier = await AdministrationHarness.AdministratorAtBranchAsync(fixture, "rec-cashier", "203.0.113.241", branch, BillingPermissions.Session, BillingPermissions.ApproveReconciliation);
        using var manager = await AdministrationHarness.AdministratorAtBranchAsync(fixture, "rec-manager", "203.0.113.242", branch, BillingPermissions.ApproveReconciliation);
        using var clerk = await AdministrationHarness.AdministratorAtBranchAsync(fixture, "rec-clerk", "203.0.113.243", branch, BillingPermissions.RecordPayment);

        var opened = await cashier.PostAsync("/api/v1/billing/cashier-sessions", new { openingFloat = 2000m }, Key());
        opened.StatusCode.ShouldBe(HttpStatusCode.Created, await opened.Content.ReadAsStringAsync(Token));
        var sessionId = CreatedId(opened);

        // 100 short, with a reason: the close leaves a batch pending approval.
        var shortSheet = new[] { new { denomination = 500m, quantity = 3 }, new { denomination = 200m, quantity = 2 } };
        var closed = await cashier.PostAsync(
            $"/api/v1/billing/cashier-sessions/{sessionId}/close",
            new { denominations = shortSheet, modeTotals = Array.Empty<object>(), reason = "A hundred-rupee note was given as change from the float." },
            Key());
        closed.StatusCode.ShouldBe(HttpStatusCode.OK, await closed.Content.ReadAsStringAsync(Token));
        var closedPayload = JsonDocument.Parse(await closed.Content.ReadAsStringAsync(Token)).RootElement;
        var batch = closedPayload.GetProperty("reconciliationBatch");
        batch.GetProperty("status").GetString().ShouldBe("Pending");
        batch.GetProperty("expectedTotal").GetDecimal().ShouldBe(2000m);
        batch.GetProperty("recordedTotal").GetDecimal().ShouldBe(1900m);
        batch.GetProperty("variance").GetDecimal().ShouldBe(-100m);
        batch.GetProperty("approvedBy").ValueKind.ShouldBe(JsonValueKind.Null);

        // Refused: no reason, a holder without the permission, and the cashier who closed it.
        await Refused(manager.PostAsync($"/api/v1/billing/cashier-sessions/{sessionId}/reconciliation/approve", new { reason = (string?)null }, Key()), HttpStatusCode.BadRequest, "billing.reason-required");
        (await clerk.PostAsync($"/api/v1/billing/cashier-sessions/{sessionId}/reconciliation/approve", new { reason = "Fine." }, Key())).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        await Refused(
            cashier.PostAsync($"/api/v1/billing/cashier-sessions/{sessionId}/reconciliation/approve", new { reason = "I closed it; I'll allow it." }, Key()),
            HttpStatusCode.Forbidden, "billing.reconciliation-approval-by-same-user");

        // Approved, by the manager, with a reason.
        var approved = await manager.PostAsync($"/api/v1/billing/cashier-sessions/{sessionId}/reconciliation/approve", new { reason = "Counted twice with the cashier present; the float was short." }, Key());
        approved.StatusCode.ShouldBe(HttpStatusCode.OK, await approved.Content.ReadAsStringAsync(Token));
        var approvedPayload = JsonDocument.Parse(await approved.Content.ReadAsStringAsync(Token)).RootElement;
        approvedPayload.GetProperty("status").GetString().ShouldBe("Approved");
        approvedPayload.GetProperty("approvedBy").GetGuid().ShouldBe(manager.UserId);
        approvedPayload.GetProperty("variance").GetDecimal().ShouldBe(-100m);
        var batchId = approvedPayload.GetProperty("id").GetGuid();

        // Once.
        await Refused(
            manager.PostAsync($"/api/v1/billing/cashier-sessions/{sessionId}/reconciliation/approve", new { reason = "Again." }, Key()),
            HttpStatusCode.Conflict, "billing.reconciliation-batch-already-approved");

        // Immutable by trigger: neither the approval columns nor anything else may move again.
        using var scope = fixture.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<BillingDbContext>();
        foreach (var sql in new[]
                 {
                     $"UPDATE billing.reconciliation_batches SET close_reason = 'edited' WHERE id = '{batchId}'",
                     $"DELETE FROM billing.reconciliation_batches WHERE id = '{batchId}'",
                 })
        {
            (await Should.ThrowAsync<PostgresException>(() => context.Database.ExecuteSqlRawAsync(sql, Token))).SqlState.ShouldBe(PostgresErrorCodes.RestrictViolation, sql);
        }

        (await context.OutboxMessages.AsNoTracking().Where(message => message.AggregateId == batchId).Select(message => message.EventType).ToListAsync(Token))
            .ShouldBe([ReconciliationApproved.Type]);
        var trail = await scope.ServiceProvider.GetRequiredService<PlatformDbContext>().AuditEvents.AsNoTracking()
            .Where(entry => entry.EntityId == batchId).ToListAsync(Token);
        trail.Select(entry => entry.Action).ShouldBe(["payments.approve_reconciliation"]);
        trail[0].Reason.ShouldBe("Counted twice with the cashier present; the float was short.");
        trail[0].Summary.ShouldNotContain("100");
    }

    [Fact]
    public async Task ACountThatAgreedNeedsNoApprovalAndRefusesOne()
    {
        Assert.SkipUnless(DatabaseAvailability.IsAvailable, DatabaseAvailability.SkipReason);

        await SeedPaymentModesAsync();
        using var owner = await AdministrationHarness.AdministratorAsync(fixture, "rec-agree", "203.0.113.244", IdentityPermissions.Branches);
        var branch = await BillingHarness.OpenBranchAsync(owner);
        using var cashier = await AdministrationHarness.AdministratorAtBranchAsync(fixture, "rec-agree-c", "203.0.113.245", branch, BillingPermissions.Session);
        using var manager = await AdministrationHarness.AdministratorAtBranchAsync(fixture, "rec-agree-m", "203.0.113.246", branch, BillingPermissions.ApproveReconciliation);

        var opened = await cashier.PostAsync("/api/v1/billing/cashier-sessions", new { openingFloat = 500m }, Key());
        var sessionId = CreatedId(opened);
        var closed = await cashier.PostAsync(
            $"/api/v1/billing/cashier-sessions/{sessionId}/close",
            new { denominations = new[] { new { denomination = 500m, quantity = 1 } }, modeTotals = Array.Empty<object>(), reason = (string?)null },
            Key());
        closed.StatusCode.ShouldBe(HttpStatusCode.OK, await closed.Content.ReadAsStringAsync(Token));
        var batch = JsonDocument.Parse(await closed.Content.ReadAsStringAsync(Token)).RootElement.GetProperty("reconciliationBatch");
        batch.GetProperty("status").GetString().ShouldBe("NotRequired");
        batch.GetProperty("variance").GetDecimal().ShouldBe(0m);

        await Refused(
            manager.PostAsync($"/api/v1/billing/cashier-sessions/{sessionId}/reconciliation/approve", new { reason = "Fine." }, Key()),
            HttpStatusCode.Conflict, "billing.reconciliation-approval-not-required");
    }

    [Fact]
    public async Task TwoApprovalsAtOnceEndWithOneApprovalAndOneConflict()
    {
        Assert.SkipUnless(DatabaseAvailability.IsAvailable, DatabaseAvailability.SkipReason);

        await SeedPaymentModesAsync();
        using var owner = await AdministrationHarness.AdministratorAsync(fixture, "rec-race", "203.0.113.247", IdentityPermissions.Branches);
        var branch = await BillingHarness.OpenBranchAsync(owner);
        using var cashier = await AdministrationHarness.AdministratorAtBranchAsync(fixture, "rec-race-c", "203.0.113.248", branch, BillingPermissions.Session);
        using var manager = await AdministrationHarness.AdministratorAtBranchAsync(fixture, "rec-race-m", "203.0.113.249", branch, BillingPermissions.ApproveReconciliation);

        var opened = await cashier.PostAsync("/api/v1/billing/cashier-sessions", new { openingFloat = 2000m }, Key());
        var sessionId = CreatedId(opened);
        var shortSheet = new[] { new { denomination = 500m, quantity = 3 }, new { denomination = 200m, quantity = 2 } };
        (await cashier.PostAsync(
            $"/api/v1/billing/cashier-sessions/{sessionId}/close",
            new { denominations = shortSheet, modeTotals = Array.Empty<object>(), reason = "Short at the count." },
            Key())).StatusCode.ShouldBe(HttpStatusCode.OK);

        var responses = await Task.WhenAll(
            manager.PostAsync($"/api/v1/billing/cashier-sessions/{sessionId}/reconciliation/approve", new { reason = "Approving the variance." }, Key()),
            manager.PostAsync($"/api/v1/billing/cashier-sessions/{sessionId}/reconciliation/approve", new { reason = "Approving the variance." }, Key()));
        responses.Select(response => response.StatusCode).OrderBy(status => status).ShouldBe([HttpStatusCode.OK, HttpStatusCode.Conflict]);

        using var scope = fixture.Services.CreateScope();
        (await scope.ServiceProvider.GetRequiredService<BillingDbContext>().ReconciliationBatches.AsNoTracking()
            .CountAsync(record => record.CashierSessionId == sessionId && record.Status == ReconciliationBatchStatus.Approved, Token)).ShouldBe(1);
    }

    [Fact]
    public async Task AnotherBranchsSessionReadsAsNotFound()
    {
        Assert.SkipUnless(DatabaseAvailability.IsAvailable, DatabaseAvailability.SkipReason);

        await SeedPaymentModesAsync();
        using var owner = await AdministrationHarness.AdministratorAsync(fixture, "rec-cross", "203.0.113.250", IdentityPermissions.Branches);
        var branch = await BillingHarness.OpenBranchAsync(owner);
        var elsewhere = await BillingHarness.OpenBranchAsync(owner);
        using var cashier = await AdministrationHarness.AdministratorAtBranchAsync(fixture, "rec-cross-c", "203.0.113.251", branch, BillingPermissions.Session);
        using var stranger = await AdministrationHarness.AdministratorAtBranchAsync(fixture, "rec-cross-m", "203.0.113.252", elsewhere, BillingPermissions.ApproveReconciliation);

        var opened = await cashier.PostAsync("/api/v1/billing/cashier-sessions", new { openingFloat = 500m }, Key());
        var sessionId = CreatedId(opened);
        (await cashier.PostAsync(
            $"/api/v1/billing/cashier-sessions/{sessionId}/close",
            new { denominations = new[] { new { denomination = 500m, quantity = 1 } }, modeTotals = Array.Empty<object>(), reason = (string?)null },
            Key())).StatusCode.ShouldBe(HttpStatusCode.OK);

        (await stranger.PostAsync($"/api/v1/billing/cashier-sessions/{sessionId}/reconciliation/approve", new { reason = "Fine." }, Key()))
            .StatusCode.ShouldBe(HttpStatusCode.NotFound, "another branch's session");
    }

    /// <summary>
    /// #220: an Owner who holds only <c>payments.approve_reconciliation</c> — not <c>payments.session</c> —
    /// can now read the very session they are authorised to approve, and can go on to approve it. The
    /// permission grants nothing wider: every other cashier-session route, and the same route for a
    /// session at another branch, still refuse them.
    /// </summary>
    [Fact]
    public async Task AnOwnerHoldingOnlyTheApprovalPermissionReadsAndApprovesButGainsNothingElseOfPaymentsSession()
    {
        Assert.SkipUnless(DatabaseAvailability.IsAvailable, DatabaseAvailability.SkipReason);

        await SeedPaymentModesAsync();
        using var owner = await AdministrationHarness.AdministratorAsync(fixture, "rec-nar-o", "203.0.113.253", IdentityPermissions.Branches);
        var branch = await BillingHarness.OpenBranchAsync(owner);
        var elsewhere = await BillingHarness.OpenBranchAsync(owner);
        using var cashier = await AdministrationHarness.AdministratorAtBranchAsync(fixture, "rec-nar-c", "203.0.113.254", branch, BillingPermissions.Session);
        // Holds exactly billing.approve_reconciliation and nothing else: the permission this issue is about.
        using var approverOnly = await AdministrationHarness.AdministratorAtBranchAsync(fixture, "rec-nar-a", "203.0.113.255", branch, BillingPermissions.ApproveReconciliation);

        var opened = await cashier.PostAsync("/api/v1/billing/cashier-sessions", new { openingFloat = 2000m }, Key());
        var sessionId = CreatedId(opened);
        var shortSheet = new[] { new { denomination = 500m, quantity = 3 }, new { denomination = 200m, quantity = 2 } };
        var closed = await cashier.PostAsync(
            $"/api/v1/billing/cashier-sessions/{sessionId}/close",
            new { denominations = shortSheet, modeTotals = Array.Empty<object>(), reason = "Short at the count." },
            Key());
        closed.StatusCode.ShouldBe(HttpStatusCode.OK, await closed.Content.ReadAsStringAsync(Token));

        // The gap this issue closes: reading the session they are about to approve now answers 200, not 403.
        var read = await approverOnly.GetAsync($"/api/v1/billing/cashier-sessions/{sessionId}/reconciliation");
        read.StatusCode.ShouldBe(HttpStatusCode.OK, await read.Content.ReadAsStringAsync(Token));
        var readBody = JsonDocument.Parse(await read.Content.ReadAsStringAsync(Token)).RootElement;
        readBody.GetProperty("id").GetGuid().ShouldBe(sessionId);
        readBody.GetProperty("reconciliationBatch").GetProperty("variance").GetDecimal().ShouldBe(-100m);

        // And they can go on to approve, exactly as before.
        (await approverOnly.PostAsync($"/api/v1/billing/cashier-sessions/{sessionId}/reconciliation/approve", new { reason = "Counted with the cashier; the float was short." }, Key()))
            .StatusCode.ShouldBe(HttpStatusCode.OK);

        // Narrow scope: the same permission grants none of payments.session's other reach.
        (await approverOnly.GetAsync($"/api/v1/billing/cashier-sessions/{sessionId}")).StatusCode.ShouldBe(HttpStatusCode.Forbidden, "GetCashierSession still needs payments.session");
        (await approverOnly.GetAsync("/api/v1/billing/cashier-sessions?status=open")).StatusCode.ShouldBe(HttpStatusCode.Forbidden, "listing still needs payments.session");
        (await approverOnly.PostAsync("/api/v1/billing/cashier-sessions", new { openingFloat = 0m }, Key())).StatusCode.ShouldBe(HttpStatusCode.Forbidden, "opening still needs payments.session");

        // Narrow scope on the other axis: the new route does not travel to a session at another branch.
        using var stranger = await AdministrationHarness.AdministratorAtBranchAsync(fixture, "rec-nar-s", "203.0.113.256", elsewhere, BillingPermissions.ApproveReconciliation);
        (await stranger.GetAsync($"/api/v1/billing/cashier-sessions/{sessionId}/reconciliation")).StatusCode.ShouldBe(HttpStatusCode.NotFound, "another branch's session");

        // And holding neither permission at all still gets nowhere near it.
        using var clerk = await AdministrationHarness.AdministratorAtBranchAsync(fixture, "rec-nar-k", "203.0.113.257", branch, BillingPermissions.RecordPayment);
        (await clerk.GetAsync($"/api/v1/billing/cashier-sessions/{sessionId}/reconciliation")).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    /// <summary>
    /// Codex review, PR #222: an open session has no reconciliation to approve, so this read must not
    /// hand an approver-only caller a live drawer's cashier identity and opening float just because the
    /// session ID resolves. Closed is the only state <c>ApproveReconciliation</c> itself ever reaches.
    /// </summary>
    [Fact]
    public async Task AnOpenSessionReadsAsNotFoundToTheApprovalPermission()
    {
        Assert.SkipUnless(DatabaseAvailability.IsAvailable, DatabaseAvailability.SkipReason);

        await SeedPaymentModesAsync();
        using var owner = await AdministrationHarness.AdministratorAsync(fixture, "rec-open-o", "203.0.113.258", IdentityPermissions.Branches);
        var branch = await BillingHarness.OpenBranchAsync(owner);
        using var cashier = await AdministrationHarness.AdministratorAtBranchAsync(fixture, "rec-open-c", "203.0.113.259", branch, BillingPermissions.Session);
        using var approverOnly = await AdministrationHarness.AdministratorAtBranchAsync(fixture, "rec-open-a", "203.0.113.260", branch, BillingPermissions.ApproveReconciliation);

        var opened = await cashier.PostAsync("/api/v1/billing/cashier-sessions", new { openingFloat = 2000m }, Key());
        var sessionId = CreatedId(opened);

        (await approverOnly.GetAsync($"/api/v1/billing/cashier-sessions/{sessionId}/reconciliation"))
            .StatusCode.ShouldBe(HttpStatusCode.NotFound, "a session with nothing to approve yet, still open");
    }

    private async Task SeedPaymentModesAsync()
    {
        using var scope = fixture.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<IBillingReferenceDataSeeder>().SeedPaymentModesAsync(SessionTestData.OrganisationId, Token);
    }
}
