using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Tailor360.Modules.Identity.Domain.Branches;
using Tailor360.Modules.Identity.Infrastructure.Persistence;

namespace Tailor360.IntegrationTests.Identity;

/// <summary>
/// The branch register against a real database.
/// </summary>
/// <remarks>
/// Two of these are about what the register refuses. A branch cannot be closed while anybody still
/// works there, and it cannot be moved to a timezone that does not exist — and the second matters more
/// than it looks, because every due date, report cut-off and SLA clock for a branch is computed in its
/// zone. A typo there does not fail; it quietly computes all of them somewhere else.
/// </remarks>
[Collection(WebApplicationCollection.Name)]
[Trait("Category", "Integration")]
public sealed class BranchAdministrationEndpointTests(WebApplicationFixture fixture)
{
    private const string Reason = "Approved by the owner at the September planning meeting.";

    /// <summary>Whether a PostgreSQL instance was found for this run.</summary>
    public static bool Available => DatabaseAvailability.IsAvailable;

    [Fact(Skip = DatabaseAvailability.SkipMessage,
        SkipUnless = nameof(Available),
        SkipType = typeof(BranchAdministrationEndpointTests))]
    public async Task ABranchIsOpenedWithItsMasterDataAndKeepsItsCodeForever()
    {
        using var administrator = await AdministrationHarness.AdministratorAsync(
            fixture, "adm-branch", "203.0.113.100", Permissions.Branches);

        var code = $"B{AdministrationHarness.UniqueToken(7)}".ToUpperInvariant();
        var opened = await OpenAsync(administrator, code, "Asia/Kolkata");

        opened.StatusCode.ShouldBe(
            HttpStatusCode.Created,
            await opened.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));

        var branch = (await AuthenticationClient.ReadAsync<BranchBody>(opened)).ShouldNotBeNull();
        branch.Code.ShouldBe(code);
        branch.City.ShouldBe("Madurai");
        branch.GstRegistrationReference.ShouldBe("GSTIN-SYNTHETIC-0001");

        // The code is set once. A request that names a different one changes everything else and
        // leaves the code alone, because it is embedded in document numbers already printed.
        var reconfigured = await administrator.PutAsync(
            $"/api/v1/admin/branches/{branch.BranchId}",
            new { code = "SOMETHINGELSE", name = "Madurai Second", timeZoneId = "Asia/Kolkata", reason = Reason },
            ("If-Match", await VersionAsync(administrator, branch.BranchId)),
            ("Idempotency-Key", Guid.CreateVersion7().ToString()));

        reconfigured.StatusCode.ShouldBe(HttpStatusCode.OK);

        var after = (await AuthenticationClient.ReadAsync<BranchBody>(reconfigured)).ShouldNotBeNull();
        after.Code.ShouldBe(code, "the code a branch's document numbers carry cannot be edited");
        after.Name.ShouldBe("Madurai Second");
    }

    [Fact(Skip = DatabaseAvailability.SkipMessage,
        SkipUnless = nameof(Available),
        SkipType = typeof(BranchAdministrationEndpointTests))]
    public async Task ATimezoneThatDoesNotExistIsRefusedRatherThanStored()
    {
        using var administrator = await AdministrationHarness.AdministratorAsync(
            fixture, "adm-tz", "203.0.113.101", Permissions.Branches);

        var refused = await OpenAsync(
            administrator, $"T{AdministrationHarness.UniqueToken(7)}".ToUpperInvariant(), "Asia/Kolkatta");

        refused.StatusCode.ShouldBe(
            HttpStatusCode.BadRequest,
            "a mistyped zone would compute every due date for the branch somewhere else, silently");

        (await AuthenticationClient.CodeAsync(refused)).ShouldBe("identity.time-zone-not-recognised");
    }

    [Fact(Skip = DatabaseAvailability.SkipMessage,
        SkipUnless = nameof(Available),
        SkipType = typeof(BranchAdministrationEndpointTests))]
    public async Task ABranchStillStaffedCannotBeClosedAndIsCountedRatherThanNamed()
    {
        using var administrator = await AdministrationHarness.AdministratorAsync(
            fixture, "adm-close", "203.0.113.102", Permissions.Branches);

        var code = $"C{AdministrationHarness.UniqueToken(7)}".ToUpperInvariant();
        var opened = await OpenAsync(administrator, code, "Asia/Kolkata");
        opened.StatusCode.ShouldBe(
            HttpStatusCode.Created,
            await opened.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));

        var branch = (await AuthenticationClient.ReadAsync<BranchBody>(opened)).ShouldNotBeNull();

        var staff = await AuthenticationTestData.CreateSignInReadyUserAsync(fixture, "brstaff");
        await AssignAsync(staff.Id, branch.BranchId);

        var refused = await CloseAsync(administrator, branch.BranchId);
        refused.StatusCode.ShouldBe(HttpStatusCode.Conflict);

        var body = await refused.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        body.ShouldContain("1 account");
        body.ShouldNotContain(staff.UserName, Case.Insensitive);
        body.ShouldNotContain("synthetic.invalid", Case.Insensitive);

        // Move the person, and the same request now succeeds — which is the whole point of refusing:
        // it is a thing the administrator can fix, not a wall.
        await UnassignAsync(staff.Id, branch.BranchId);

        var closed = await CloseAsync(administrator, branch.BranchId);
        closed.StatusCode.ShouldBe(
            HttpStatusCode.OK,
            await closed.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));

        // Nothing was deleted: the row is still readable, which is what keeps every order and invoice
        // that names this branch resolvable.
        var reread = await administrator.GetAsync($"/api/v1/admin/branches/{branch.BranchId}");
        reread.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await AuthenticationClient.ReadAsync<BranchBody>(reread)).ShouldNotBeNull()
            .Status.ShouldBe(nameof(BranchStatus.Inactive));
    }

    private static Task<HttpResponseMessage> OpenAsync(
        AdministrationHarness.AdministratorClient administrator, string code, string timeZoneId)
        => administrator.PostAsync(
            "/api/v1/admin/branches/",
            new
            {
                code,
                name = "Madurai Main",
                timeZoneId,
                addressLine1 = "12 Example Street",
                city = "Madurai",
                state = "Tamil Nadu",
                postalCode = "625001",
                contactPhone = "+91 90000 00000",
                contactEmail = "madurai@synthetic.invalid",
                gstRegistrationReference = "GSTIN-SYNTHETIC-0001",
                reason = Reason,
            },
            ("Idempotency-Key", Guid.CreateVersion7().ToString()));

    private static async Task<HttpResponseMessage> CloseAsync(
        AdministrationHarness.AdministratorClient administrator, Guid branchId)
        => await administrator.PostAsync(
            $"/api/v1/admin/branches/{branchId}/close",
            new { reason = Reason },
            ("If-Match", await VersionAsync(administrator, branchId)),
            ("Idempotency-Key", Guid.CreateVersion7().ToString()));

    private static async Task<string> VersionAsync(
        AdministrationHarness.AdministratorClient administrator, Guid branchId)
    {
        var read = await administrator.GetAsync($"/api/v1/admin/branches/{branchId}");
        read.StatusCode.ShouldBe(HttpStatusCode.OK);
        return read.Headers.ETag.ShouldNotBeNull().Tag;
    }

    private async Task AssignAsync(Guid userId, Guid branchId)
    {
        using var scope = fixture.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var clock = scope.ServiceProvider.GetRequiredService<Tailor360.Platform.Abstractions.Time.IClock>();

        context.UserBranchAssignments.Add(
            Tailor360.Modules.Identity.Domain.Access.UserBranchAssignment.Create(
                userId, branchId, clock.UtcNow));

        await context.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    private async Task UnassignAsync(Guid userId, Guid branchId)
    {
        using var scope = fixture.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();

        var assignments = await context.UserBranchAssignments
            .Where(assignment => assignment.UserId == userId && assignment.BranchId == branchId)
            .ToListAsync(TestContext.Current.CancellationToken);

        context.UserBranchAssignments.RemoveRange(assignments);
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    private sealed record BranchBody(
        Guid BranchId,
        string Code,
        string Name,
        string TimeZoneId,
        string Status,
        string? City,
        string? GstRegistrationReference,
        string Version);
}
