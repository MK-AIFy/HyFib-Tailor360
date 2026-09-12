using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Shouldly;
using Tailor360.IntegrationTests.Identity;
using Tailor360.Modules.Billing.Application.Abstractions;
using Tailor360.Modules.Billing.Application.Payments;
using Tailor360.Modules.Billing.Contracts.Events;
using Tailor360.Modules.Billing.Contracts.Payments;
using Tailor360.Modules.Billing.Domain.Payments;
using Tailor360.Modules.Billing.Infrastructure.Persistence;
using Tailor360.Platform.Abstractions.Results;
using Tailor360.Platform.Persistence.Contexts;
using Tailor360.Platform.Security.Permissions;
using static Tailor360.IntegrationTests.Billing.InvoiceScenes;

namespace Tailor360.IntegrationTests.Billing;

/// <summary>
/// The dispatch eligibility answer and the consumption of the exception behind it (#164), exercised
/// through <see cref="IDispatchEligibilityQuery"/> and <see cref="DispatchExceptionHandler"/> directly —
/// the same way #37's dispatch authorisation will call them, since it does not exist yet. Fails closed
/// with no posted invoice; a live exception wins and is consumed exactly once; every re-validation named
/// in the acceptance criteria is exercised against a real database.
/// </summary>
[Trait("Category", "Integration")]
[Collection(WebApplicationCollection.Name)]
public sealed class DispatchEligibilityQueryTests(WebApplicationFixture fixture)
{
    private static readonly string RunToken = AdministrationHarness.UniqueToken(6).ToUpperInvariant();

    [Fact]
    public async Task NoPostedInvoiceAnswersNotEvaluated()
    {
        Assert.SkipUnless(DatabaseAvailability.IsAvailable, DatabaseAvailability.SkipReason);

        var scene = await BuildAsync(fixture, "dxe-none", "203.0.113.80", "DXEN", RunToken);

        var answer = await EligibilityAsync(scene.OrderId, scene.Jobs);
        answer.Reason.ShouldBe("NotEvaluated");
    }

    [Fact]
    public async Task APostedInvoiceWithNothingPaidAnswersUnpaid()
    {
        Assert.SkipUnless(DatabaseAvailability.IsAvailable, DatabaseAvailability.SkipReason);

        var scene = await BuildAsync(fixture, "dxe-unpaid", "203.0.113.81", "DXEU", RunToken);
        using var cashier = await CashierAsync(fixture, "dxe-u-cashier", "203.0.113.82", scene.Branch, BillingPermissions.PostInvoice);
        var (invoiceId, tag) = await DraftAsync(cashier, scene.OrderId, scene.Reference);
        (await cashier.PostAsync($"/api/v1/billing/invoices/{invoiceId}/post", new { reason = (string?)null }, Tagged(tag))).StatusCode.ShouldBe(HttpStatusCode.OK);

        var answer = await EligibilityAsync(scene.OrderId, scene.Jobs);
        answer.Reason.ShouldBe("Unpaid");
    }

    [Fact]
    public async Task APostedInvoicePaidInFullAnswersPaid()
    {
        Assert.SkipUnless(DatabaseAvailability.IsAvailable, DatabaseAvailability.SkipReason);

        await SeedPaymentModesAsync();
        var scene = await BuildAsync(fixture, "dxe-paid", "203.0.113.83", "DXEP", RunToken);
        using var cashier = await CashierAsync(fixture, "dxe-p-cashier", "203.0.113.84", scene.Branch, BillingPermissions.PostInvoice, BillingPermissions.RecordPayment, BillingPermissions.Session);
        var (invoiceId, tag) = await DraftAsync(cashier, scene.OrderId, scene.Reference);
        (await cashier.PostAsync($"/api/v1/billing/invoices/{invoiceId}/post", new { reason = (string?)null }, Tagged(tag))).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await cashier.PostAsync("/api/v1/billing/cashier-sessions", new { openingFloat = 0m }, Key())).StatusCode.ShouldBe(HttpStatusCode.Created);
        (await cashier.PostAsync("/api/v1/billing/payments", new { orderId = scene.OrderId, modeCode = "CASH", amount = 1134m, reference = (string?)null }, Key()))
            .StatusCode.ShouldBe(HttpStatusCode.Created);

        var answer = await EligibilityAsync(scene.OrderId, scene.Jobs);
        answer.Reason.ShouldBe("Paid");
    }

    [Fact]
    public async Task NotEvaluatedWhenOneRequestedJobHasNoPostedInvoiceEvenThoughAnotherJobsInvoiceIsPaidInFull()
    {
        Assert.SkipUnless(DatabaseAvailability.IsAvailable, DatabaseAvailability.SkipReason);

        // Two garment jobs of one order; only the first is ever invoiced, and that invoice is paid in
        // full. A dispatch of both jobs must not read as Paid off the strength of the order's other,
        // fully-settled invoice — the second job was never charged for at all.
        await SeedPaymentModesAsync();
        var scene = await BuildAsync(fixture, "dxe-partial", "203.0.113.93", "DXEZ", RunToken);
        using var cashier = await CashierAsync(fixture, "dxe-z-cashier", "203.0.113.94", scene.Branch, BillingPermissions.PostInvoice, BillingPermissions.RecordPayment, BillingPermissions.Session);

        var jobAOnlyReference = $"{scene.Reference}-job-a-only";
        await PriceAsync(fixture, scene.Branch, jobAOnlyReference, [scene.Jobs[0].ToString()], scene.ItemCode, scene.SurchargeCode);
        var (invoiceId, tag) = await DraftAsync(cashier, scene.OrderId, jobAOnlyReference);
        (await cashier.PostAsync($"/api/v1/billing/invoices/{invoiceId}/post", new { reason = (string?)null }, Tagged(tag))).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await cashier.PostAsync("/api/v1/billing/cashier-sessions", new { openingFloat = 0m }, Key())).StatusCode.ShouldBe(HttpStatusCode.Created);
        (await cashier.PostAsync("/api/v1/billing/payments", new { orderId = scene.OrderId, modeCode = "CASH", amount = 567m, reference = (string?)null }, Key()))
            .StatusCode.ShouldBe(HttpStatusCode.Created);

        (await EligibilityAsync(scene.OrderId, [scene.Jobs[0]])).Reason.ShouldBe("Paid", "the invoiced, paid job alone");
        (await EligibilityAsync(scene.OrderId, scene.Jobs)).Reason.ShouldBe("NotEvaluated", "the second job has no posted invoice covering it");
    }

    [Fact]
    public async Task ALiveApprovedExceptionAnswersApprovedExceptionAndIsConsumedExactlyOnceUnderConcurrentAttempts()
    {
        Assert.SkipUnless(DatabaseAvailability.IsAvailable, DatabaseAvailability.SkipReason);

        var scene = await BuildAsync(fixture, "dxe-race", "203.0.113.85", "DXER", RunToken);
        var (exceptionId, _) = await ApproveAsync(scene, "dxe-r-appr", "203.0.113.86");

        var answer = await EligibilityAsync(scene.OrderId, scene.Jobs);
        answer.Reason.ShouldBe("ApprovedException");
        answer.DispatchExceptionId.ShouldBe(exceptionId);

        var dispatcher = Guid.NewGuid();
        var results = await Task.WhenAll(
            ConsumeAsync(exceptionId, scene.OrderId, scene.Jobs, dispatcher),
            ConsumeAsync(exceptionId, scene.OrderId, scene.Jobs, dispatcher));
        results.Count(result => result.IsSuccess).ShouldBe(1);
        results.Count(result => result.IsFailure && result.Error.Code == "billing.dispatch-exception-already-consumed").ShouldBe(1);

        using var scope = fixture.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<BillingDbContext>();
        (await context.DispatchExceptions.AsNoTracking().CountAsync(exception => exception.Id == exceptionId && exception.Status == DispatchExceptionStatus.Consumed, Token))
            .ShouldBe(1);
        (await context.OutboxMessages.AsNoTracking().Where(message => message.AggregateId == exceptionId).Select(message => message.EventType).ToListAsync(Token))
            .ShouldContain(DispatchExceptionConsumed.Type);
    }

    [Fact]
    public async Task RefusesConsumptionByThePersonWhoApprovedIt()
    {
        Assert.SkipUnless(DatabaseAvailability.IsAvailable, DatabaseAvailability.SkipReason);

        var scene = await BuildAsync(fixture, "dxe-self", "203.0.113.87", "DXES", RunToken);
        var (exceptionId, approvedBy) = await ApproveAsync(scene, "dxe-s-appr", "203.0.113.88");

        var result = await ConsumeAsync(exceptionId, scene.OrderId, scene.Jobs, approvedBy);
        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("billing.dispatch-exception-approver-is-dispatcher");
    }

    [Fact]
    public async Task RefusesConsumptionWhenTheJobSetNoLongerMatchesExactly()
    {
        Assert.SkipUnless(DatabaseAvailability.IsAvailable, DatabaseAvailability.SkipReason);

        var scene = await BuildAsync(fixture, "dxe-jobs", "203.0.113.89", "DXEJ", RunToken);
        var (exceptionId, _) = await ApproveAsync(scene, "dxe-j-appr", "203.0.113.90");

        var result = await ConsumeAsync(exceptionId, scene.OrderId, [scene.Jobs[0]], Guid.NewGuid());
        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("billing.dispatch-exception-job-set-changed");
    }

    [Fact]
    public async Task RefusesConsumptionOnceItIsPastItsExpiry()
    {
        Assert.SkipUnless(DatabaseAvailability.IsAvailable, DatabaseAvailability.SkipReason);

        var scene = await BuildAsync(fixture, "dxe-expired", "203.0.113.91", "DXEX", RunToken);
        var exceptionId = await SeedPastDueExceptionAsync(scene.Branch, scene.OrderId, scene.Jobs);

        var result = await ConsumeAsync(exceptionId, scene.OrderId, scene.Jobs, Guid.NewGuid());
        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("billing.dispatch-exception-expired");
    }

    [Fact]
    public async Task TheWorkerExpiresAPastDueExceptionAndPublishesTheEvent()
    {
        Assert.SkipUnless(DatabaseAvailability.IsAvailable, DatabaseAvailability.SkipReason);

        var scene = await BuildAsync(fixture, "dxe-worker", "203.0.113.92", "DXEW", RunToken);
        var exceptionId = await SeedPastDueExceptionAsync(scene.Branch, scene.OrderId, scene.Jobs);

        using (var scope = fixture.Services.CreateScope())
        {
            var handler = scope.ServiceProvider.GetRequiredService<DispatchExceptionHandler>();
            var expired = await handler.ExpireDueAsync(200, Token);
            expired.IsSuccess.ShouldBeTrue(expired.IsFailure ? expired.Error.Code : string.Empty);
            expired.Value.ShouldBeGreaterThanOrEqualTo(1);
        }

        using var verify = fixture.Services.CreateScope();
        var context = verify.ServiceProvider.GetRequiredService<BillingDbContext>();
        (await context.DispatchExceptions.AsNoTracking().SingleAsync(exception => exception.Id == exceptionId, Token)).Status.ShouldBe(DispatchExceptionStatus.Expired);
        (await context.OutboxMessages.AsNoTracking().Where(message => message.AggregateId == exceptionId).Select(message => message.EventType).ToListAsync(Token))
            .ShouldContain(DispatchExceptionExpired.Type);
        var trail = await verify.ServiceProvider.GetRequiredService<PlatformDbContext>().AuditEvents.AsNoTracking()
            .Where(entry => entry.EntityId == exceptionId).ToListAsync(Token);
        trail.Select(entry => entry.Action).ShouldContain("billing.expire_dispatch_exception");
    }

    [Fact]
    public async Task RefusesInsertingAJobIntoAnAlreadyApprovedExceptionsSetAndEveryUpdateAndDelete()
    {
        Assert.SkipUnless(DatabaseAvailability.IsAvailable, DatabaseAvailability.SkipReason);

        var scene = await BuildAsync(fixture, "dxe-trigger", "203.0.113.95", "DXET", RunToken);
        var (exceptionId, _) = await ApproveAsync(scene, "dxe-t-appr", "203.0.113.96");

        using var scope = fixture.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<BillingDbContext>();
        foreach (var sql in new[]
                 {
                     $"INSERT INTO billing.dispatch_exception_jobs (dispatch_exception_id, garment_job_id) VALUES ('{exceptionId}', '{Guid.NewGuid()}')",
                     $"UPDATE billing.dispatch_exception_jobs SET garment_job_id = '{Guid.NewGuid()}' WHERE dispatch_exception_id = '{exceptionId}'",
                     $"DELETE FROM billing.dispatch_exception_jobs WHERE dispatch_exception_id = '{exceptionId}'",
                 })
        {
            (await Should.ThrowAsync<PostgresException>(() => context.Database.ExecuteSqlRawAsync(sql, Token))).SqlState.ShouldBe(PostgresErrorCodes.RestrictViolation, sql);
        }
    }

    private async Task<(Guid ExceptionId, Guid ApprovedBy)> ApproveAsync(InvoiceScene scene, string prefix, string address)
    {
        using var approver = await AdministrationHarness.AdministratorAtBranchAsync(fixture, prefix, address, scene.Branch, BillingPermissions.ApproveDispatchException);
        var response = await approver.PostAsync(
            "/api/v1/billing/dispatch-exceptions",
            new { orderId = scene.OrderId, jobIds = scene.Jobs, maxOutstandingAmount = 500m, reasonCode = "CODE", reasonText = "Reason.", expiresAt = Now(fixture).AddHours(48) },
            Key());
        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync(Token));
        var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Token)).RootElement;
        return (body.GetProperty("id").GetGuid(), body.GetProperty("approvedBy").GetGuid());
    }

    private async Task<DispatchEligibilityResult> EligibilityAsync(Guid orderId, IReadOnlyCollection<Guid> jobIds)
    {
        using var scope = fixture.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<IDispatchEligibilityQuery>()
            .GetDispatchEligibilityAsync(orderId, jobIds, SessionTestData.OrganisationId, Token);
    }

    private async Task<Result> ConsumeAsync(Guid exceptionId, Guid orderId, IReadOnlyCollection<Guid> jobIds, Guid dispatcherId)
    {
        using var scope = fixture.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<IDispatchEligibilityQuery>()
            .ConsumeExceptionAsync(exceptionId, orderId, jobIds, dispatcherId, SessionTestData.OrganisationId, Token);
    }

    /// <summary>
    /// Inserts an already-past-due, never-consumed exception directly, bypassing
    /// <see cref="DispatchException.Approve"/>'s future-only expiry check — the only way to seed the
    /// state a real approval can never reach, since 72 hours cannot pass inside a test.
    /// </summary>
    private async Task<Guid> SeedPastDueExceptionAsync(Guid branchId, Guid orderId, IReadOnlyCollection<Guid> jobIds)
    {
        using var scope = fixture.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<BillingDbContext>();
        var exceptionId = Guid.NewGuid();
        var now = Now(fixture);

        // One transaction: the job rows' own trigger accepts an insert only in the same transaction as
        // the parent's, exactly as EF's own SaveChanges writes them together, so two separate statements
        // here would be refused the same way a real, illegitimate later insert is.
        await using var transaction = await context.Database.BeginTransactionAsync(Token);

        await context.Database.ExecuteSqlInterpolatedAsync(
            $"""
             INSERT INTO billing.dispatch_exceptions
                 (id, organisation_id, branch_id, order_id, policy_version, reason_code, reason_text,
                  approved_by, approved_at, expires_at, status, consumed_by, consumed_at, expired_at,
                  max_outstanding_amount_amount, max_outstanding_amount_currency)
             VALUES
                 ({exceptionId}, {SessionTestData.OrganisationId}, {branchId}, {orderId}, '1', 'CODE', 'Seeded past due for the worker test.',
                  {Guid.NewGuid()}, {now.AddHours(-3)}, {now.AddHours(-1)}, 0, NULL, NULL, NULL,
                  500.0000, 'INR')
             """,
            Token);

        foreach (var jobId in jobIds)
        {
            await context.Database.ExecuteSqlInterpolatedAsync(
                $"INSERT INTO billing.dispatch_exception_jobs (dispatch_exception_id, garment_job_id) VALUES ({exceptionId}, {jobId})", Token);
        }

        await transaction.CommitAsync(Token);

        return exceptionId;
    }

    private async Task SeedPaymentModesAsync()
    {
        using var scope = fixture.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<IBillingReferenceDataSeeder>().SeedPaymentModesAsync(SessionTestData.OrganisationId, Token);
    }
}
