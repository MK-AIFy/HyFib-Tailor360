using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Tailor360.IntegrationTests.Identity;
using Tailor360.Modules.Billing.Contracts.Events;
using Tailor360.Modules.Billing.Infrastructure.Persistence;
using Tailor360.Platform.Persistence.Contexts;
using Tailor360.Platform.Security.Permissions;
using static Tailor360.IntegrationTests.Billing.InvoiceScenes;

namespace Tailor360.IntegrationTests.Billing;

/// <summary>
/// The single-use dispatch exception's approval over HTTP (#164): bound to an order, exactly its named
/// jobs, a maximum outstanding amount and an expiry of at most 72 hours, refused where the order is not
/// Billing's, is at another branch, or names a job that is not a live job of it. No amount appears in
/// the audit summary; the event carries the maximum and the expiry.
/// </summary>
[Trait("Category", "Integration")]
[Collection(WebApplicationCollection.Name)]
public sealed class DispatchExceptionEndpointTests(WebApplicationFixture fixture)
{
    private static readonly string RunToken = AdministrationHarness.UniqueToken(6).ToUpperInvariant();

    [Fact]
    public async Task ApprovesAnExceptionForNamedJobsOfAnOrder()
    {
        Assert.SkipUnless(DatabaseAvailability.IsAvailable, DatabaseAvailability.SkipReason);

        var scene = await BuildAsync(fixture, "dxc-approve", "203.0.113.60", "DXCA", RunToken);
        using var approver = await AdministrationHarness.AdministratorAtBranchAsync(fixture, "dxc-appr", "203.0.113.61", scene.Branch, BillingPermissions.ApproveDispatchException);

        var expiresAt = Now(fixture).AddHours(48);
        var response = await approver.PostAsync(
            "/api/v1/billing/dispatch-exceptions",
            new { orderId = scene.OrderId, jobIds = scene.Jobs, maxOutstandingAmount = 500m, reasonCode = "CUSTOMER_TRAVELLING", reasonText = "Travelling tonight; settling on return.", expiresAt },
            Key());
        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync(Token));

        var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Token)).RootElement;
        body.GetProperty("orderId").GetGuid().ShouldBe(scene.OrderId);
        body.GetProperty("jobIds").EnumerateArray().Select(job => job.GetGuid()).Order().ShouldBe(scene.Jobs.Order());
        body.GetProperty("maxOutstandingAmount").GetDecimal().ShouldBe(500m);
        body.GetProperty("policyVersion").GetString().ShouldBe("1");
        body.GetProperty("reasonCode").GetString().ShouldBe("CUSTOMER_TRAVELLING");
        body.GetProperty("status").GetString().ShouldBe("Approved");
        var exceptionId = body.GetProperty("id").GetGuid();

        using var scope = fixture.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<BillingDbContext>();
        (await context.OutboxMessages.AsNoTracking().Where(message => message.AggregateId == exceptionId).Select(message => message.EventType).ToListAsync(Token))
            .ShouldContain(DispatchExceptionApproved.Type);

        var trail = await scope.ServiceProvider.GetRequiredService<PlatformDbContext>().AuditEvents.AsNoTracking()
            .Where(entry => entry.EntityId == exceptionId).ToListAsync(Token);
        trail.Select(entry => entry.Action).ShouldBe(["billing.approve_dispatch_exception"]);
        trail[0].Reason.ShouldBe("Travelling tonight; settling on return.");
        trail[0].Summary.ShouldNotContain("500");
    }

    [Fact]
    public async Task RefusesAnEmptyReason()
    {
        Assert.SkipUnless(DatabaseAvailability.IsAvailable, DatabaseAvailability.SkipReason);

        var scene = await BuildAsync(fixture, "dxc-reason", "203.0.113.62", "DXCR", RunToken);
        using var approver = await AdministrationHarness.AdministratorAtBranchAsync(fixture, "dxc-rappr", "203.0.113.63", scene.Branch, BillingPermissions.ApproveDispatchException);

        await Refused(
            approver.PostAsync(
                "/api/v1/billing/dispatch-exceptions",
                new { orderId = scene.OrderId, jobIds = scene.Jobs, maxOutstandingAmount = 500m, reasonCode = "CODE", reasonText = (string?)null, expiresAt = Now(fixture).AddHours(1) },
                Key()),
            HttpStatusCode.BadRequest, "billing.reason-required");
    }

    [Fact]
    public async Task RefusesAJobThatIsNotOfTheOrder()
    {
        Assert.SkipUnless(DatabaseAvailability.IsAvailable, DatabaseAvailability.SkipReason);

        var scene = await BuildAsync(fixture, "dxc-job", "203.0.113.64", "DXCJ", RunToken);
        using var approver = await AdministrationHarness.AdministratorAtBranchAsync(fixture, "dxc-jappr", "203.0.113.65", scene.Branch, BillingPermissions.ApproveDispatchException);

        await Refused(
            approver.PostAsync(
                "/api/v1/billing/dispatch-exceptions",
                new { orderId = scene.OrderId, jobIds = new[] { Guid.NewGuid() }, maxOutstandingAmount = 500m, reasonCode = "CODE", reasonText = "Reason.", expiresAt = Now(fixture).AddHours(1) },
                Key()),
            HttpStatusCode.BadRequest, "billing.dispatch-exception-job-not-of-order");
    }

    [Fact]
    public async Task RefusesAMaximumThatIsNotPositive()
    {
        Assert.SkipUnless(DatabaseAvailability.IsAvailable, DatabaseAvailability.SkipReason);

        var scene = await BuildAsync(fixture, "dxc-amt", "203.0.113.66", "DXCM", RunToken);
        using var approver = await AdministrationHarness.AdministratorAtBranchAsync(fixture, "dxc-mappr", "203.0.113.67", scene.Branch, BillingPermissions.ApproveDispatchException);

        await Refused(
            approver.PostAsync(
                "/api/v1/billing/dispatch-exceptions",
                new { orderId = scene.OrderId, jobIds = scene.Jobs, maxOutstandingAmount = 0m, reasonCode = "CODE", reasonText = "Reason.", expiresAt = Now(fixture).AddHours(1) },
                Key()),
            HttpStatusCode.BadRequest, "billing.dispatch-exception-amount-not-positive");
    }

    [Fact]
    public async Task RefusesAnExpiryMoreThanSeventyTwoHoursAhead()
    {
        Assert.SkipUnless(DatabaseAvailability.IsAvailable, DatabaseAvailability.SkipReason);

        var scene = await BuildAsync(fixture, "dxc-exp", "203.0.113.68", "DXCE", RunToken);
        using var approver = await AdministrationHarness.AdministratorAtBranchAsync(fixture, "dxc-eappr", "203.0.113.69", scene.Branch, BillingPermissions.ApproveDispatchException);

        await Refused(
            approver.PostAsync(
                "/api/v1/billing/dispatch-exceptions",
                new { orderId = scene.OrderId, jobIds = scene.Jobs, maxOutstandingAmount = 500m, reasonCode = "CODE", reasonText = "Reason.", expiresAt = Now(fixture).AddHours(73) },
                Key()),
            HttpStatusCode.BadRequest, "billing.dispatch-exception-expiry-not-well-formed");
    }

    [Fact]
    public async Task RefusesAnOrderBillingHasNotHeardOf()
    {
        Assert.SkipUnless(DatabaseAvailability.IsAvailable, DatabaseAvailability.SkipReason);

        var scene = await BuildAsync(fixture, "dxc-unk", "203.0.113.70", "DXCU", RunToken);
        using var approver = await AdministrationHarness.AdministratorAtBranchAsync(fixture, "dxc-uappr", "203.0.113.71", scene.Branch, BillingPermissions.ApproveDispatchException);

        await Refused(
            approver.PostAsync(
                "/api/v1/billing/dispatch-exceptions",
                new { orderId = Guid.NewGuid(), jobIds = scene.Jobs, maxOutstandingAmount = 500m, reasonCode = "CODE", reasonText = "Reason.", expiresAt = Now(fixture).AddHours(1) },
                Key()),
            HttpStatusCode.NotFound, "billing.order-not-known");
    }

    [Fact]
    public async Task RefusesAnOrderAtAnotherBranch()
    {
        Assert.SkipUnless(DatabaseAvailability.IsAvailable, DatabaseAvailability.SkipReason);

        var scene = await BuildAsync(fixture, "dxc-else", "203.0.113.72", "DXCX", RunToken);
        var elsewhere = await BillingHarness.OpenBranchAsync(scene.Owner);
        using var stranger = await AdministrationHarness.AdministratorAtBranchAsync(fixture, "dxc-stranger", "203.0.113.73", elsewhere, BillingPermissions.ApproveDispatchException);

        await Refused(
            stranger.PostAsync(
                "/api/v1/billing/dispatch-exceptions",
                new { orderId = scene.OrderId, jobIds = scene.Jobs, maxOutstandingAmount = 500m, reasonCode = "CODE", reasonText = "Reason.", expiresAt = Now(fixture).AddHours(1) },
                Key()),
            HttpStatusCode.Forbidden, "billing.order-at-another-branch");
    }

    [Fact]
    public async Task RefusesACallerWithoutThePermission()
    {
        Assert.SkipUnless(DatabaseAvailability.IsAvailable, DatabaseAvailability.SkipReason);

        var scene = await BuildAsync(fixture, "dxc-noperm", "203.0.113.74", "DXCN", RunToken);
        using var clerk = await AdministrationHarness.AdministratorAtBranchAsync(fixture, "dxc-clerk", "203.0.113.75", scene.Branch, BillingPermissions.RecordPayment);

        (await clerk.PostAsync(
                "/api/v1/billing/dispatch-exceptions",
                new { orderId = scene.OrderId, jobIds = scene.Jobs, maxOutstandingAmount = 500m, reasonCode = "CODE", reasonText = "Reason.", expiresAt = Now(fixture).AddHours(1) },
                Key()))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    /// <summary>
    /// #220: an Owner who holds only <c>billing.approve_dispatch_exception</c> — not <c>payments.record</c>
    /// — can now read the order balance the approval screen needs to set the maximum allowance. The
    /// permission grants nothing wider: the general-purpose balance route, another branch's order under
    /// the same route, and a caller holding a different billing permission are all still refused.
    /// </summary>
    [Fact]
    public async Task AnOwnerHoldingOnlyTheApprovalPermissionReadsTheBalanceButGainsNothingElseOfPaymentsRecord()
    {
        Assert.SkipUnless(DatabaseAvailability.IsAvailable, DatabaseAvailability.SkipReason);

        var scene = await BuildAsync(fixture, "dxc-bal", "203.0.113.76", "DXCB", RunToken);
        // Holds exactly billing.approve_dispatch_exception and nothing else: the permission this issue is about.
        using var approverOnly = await AdministrationHarness.AdministratorAtBranchAsync(fixture, "dxc-bal-a", "203.0.113.77", scene.Branch, BillingPermissions.ApproveDispatchException);

        // The gap this issue closes: reading the order's balance now answers 200, not 403.
        var read = await approverOnly.GetAsync($"/api/v1/billing/orders/{scene.OrderId}/dispatch-exception-balance");
        read.StatusCode.ShouldBe(HttpStatusCode.OK, await read.Content.ReadAsStringAsync(Token));
        JsonDocument.Parse(await read.Content.ReadAsStringAsync(Token)).RootElement.GetProperty("outstanding").GetDecimal().ShouldBe(0m);

        // Narrow scope: the same permission grants none of payments.record's other reach.
        (await approverOnly.GetAsync($"/api/v1/billing/orders/{scene.OrderId}/balance")).StatusCode.ShouldBe(HttpStatusCode.Forbidden, "GetOrderBalance still needs payments.record");
        (await approverOnly.GetAsync("/api/v1/billing/payment-modes/available")).StatusCode.ShouldBe(HttpStatusCode.Forbidden, "still needs payments.record");

        // Narrow scope on the other axis: the new route does not travel to an order at another branch.
        var elsewhere = await BillingHarness.OpenBranchAsync(scene.Owner);
        using var stranger = await AdministrationHarness.AdministratorAtBranchAsync(fixture, "dxc-bal-s", "203.0.113.78", elsewhere, BillingPermissions.ApproveDispatchException);
        (await stranger.GetAsync($"/api/v1/billing/orders/{scene.OrderId}/dispatch-exception-balance")).StatusCode.ShouldBe(HttpStatusCode.NotFound, "another branch's order");

        // And a different billing permission does not substitute for the approving one.
        using var clerk = await AdministrationHarness.AdministratorAtBranchAsync(fixture, "dxc-bal-k", "203.0.113.79", scene.Branch, BillingPermissions.RecordPayment);
        (await clerk.GetAsync($"/api/v1/billing/orders/{scene.OrderId}/dispatch-exception-balance")).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }
}
