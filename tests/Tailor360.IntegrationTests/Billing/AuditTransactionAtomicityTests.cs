using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Shouldly;
using Tailor360.Modules.Billing.Application.Abstractions;
using Tailor360.Modules.Billing.Domain.Payments;
using Tailor360.Modules.Billing.Infrastructure.Persistence;
using Tailor360.Platform.Abstractions.Auditing;
using Tailor360.Platform.Abstractions.Money;

namespace Tailor360.IntegrationTests.Billing;

/// <summary>
/// Proves the platform fix for issue #179 (G-2): an audit entry staged through
/// <see cref="IBillingAuditWriter"/> is tracked by <see cref="BillingDbContext"/>'s own change tracker —
/// not a second, separate <c>PlatformDbContext</c> — so it is saved by the SAME <c>SaveChangesAsync</c>
/// call, and therefore the same transaction, as the business change it describes.
/// </summary>
/// <remarks>
/// A test that only checks both writes land together would pass even against the bug this issue
/// describes (audit committed through its own, separate context, right after the business change had
/// already committed through its own). Instead, this forces the ONE save that now carries both writes to
/// fail on the audit half — a summary far past its column's limit, which only PostgreSQL, not a
/// client-side check, rejects — after the business row has already been staged, and asserts that neither
/// the business row nor the audit row exists afterward. Against the pre-fix mechanism this scenario is
/// not even expressible: the old <c>IAuditWriter</c> wrote through a hard-typed <c>PlatformDbContext</c>,
/// so there was no way to stage an audit entry on <see cref="BillingDbContext"/>'s own tracker at all.
/// </remarks>
[Trait("Category", "Integration")]
[Collection(WebApplicationCollection.Name)]
public sealed class AuditTransactionAtomicityTests(WebApplicationFixture fixture)
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task ABusinessRowAndItsAuditEntryRollBackTogetherWhenTheSaveFails()
    {
        Assert.SkipUnless(DatabaseAvailability.IsAvailable, DatabaseAvailability.SkipReason);

        var sessionId = Guid.CreateVersion7();
        var organisationId = Guid.CreateVersion7();
        var branchId = Guid.CreateVersion7();
        var cashierId = Guid.CreateVersion7();

        using (var scope = fixture.Services.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<BillingDbContext>();
            var audit = scope.ServiceProvider.GetRequiredService<IBillingAuditWriter>();

            var opened = CashierSession.Open(sessionId, organisationId, branchId, cashierId, Money.Rupees(1000m), DateTimeOffset.UtcNow);
            opened.IsSuccess.ShouldBeTrue();
            context.CashierSessions.Add(opened.Value);

            // Staged on the SAME context as the session above — the fix under test. `Summary` is
            // `HasMaxLength(2000)`; PostgreSQL enforces that at the column, so this INSERT is refused by
            // the database itself, deep inside the one SaveChangesAsync call that also carries the
            // session.
            await audit.WriteAsync(new AuditEntry(
                "payments.open_session", "CashierSession", sessionId, new string('x', 4000)), Token);

            var thrown = await Should.ThrowAsync<DbUpdateException>(() => context.SaveChangesAsync(Token));
            (thrown.InnerException as PostgresException)?.SqlState.ShouldBe(PostgresErrorCodes.StringDataRightTruncation);
        }

        // A fresh, untracked scope: proves the session was never committed, not merely that this
        // context's own change tracker still remembers trying to add it.
        using (var verify = fixture.Services.CreateScope())
        {
            var context = verify.ServiceProvider.GetRequiredService<BillingDbContext>();
            (await context.CashierSessions.AsNoTracking().AnyAsync(session => session.Id == sessionId, Token)).ShouldBeFalse();

            var platform = verify.ServiceProvider.GetRequiredService<Tailor360.Platform.Persistence.Contexts.PlatformDbContext>();
            (await platform.AuditEvents.AsNoTracking().AnyAsync(entry => entry.EntityId == sessionId, Token)).ShouldBeFalse();
        }
    }

    [Fact]
    public async Task ABusinessRowAndItsAuditEntryCommitTogetherWhenTheSaveSucceeds()
    {
        Assert.SkipUnless(DatabaseAvailability.IsAvailable, DatabaseAvailability.SkipReason);

        var sessionId = Guid.CreateVersion7();
        var organisationId = Guid.CreateVersion7();
        var branchId = Guid.CreateVersion7();
        var cashierId = Guid.CreateVersion7();

        using (var scope = fixture.Services.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<BillingDbContext>();
            var audit = scope.ServiceProvider.GetRequiredService<IBillingAuditWriter>();

            var opened = CashierSession.Open(sessionId, organisationId, branchId, cashierId, Money.Rupees(1000m), DateTimeOffset.UtcNow);
            context.CashierSessions.Add(opened.Value);

            await audit.WriteAsync(new AuditEntry(
                "payments.open_session", "CashierSession", sessionId, "Cashier session opened."), Token);

            await context.SaveChangesAsync(Token);
        }

        using (var verify = fixture.Services.CreateScope())
        {
            var context = verify.ServiceProvider.GetRequiredService<BillingDbContext>();
            (await context.CashierSessions.AsNoTracking().AnyAsync(session => session.Id == sessionId, Token)).ShouldBeTrue();

            var platform = verify.ServiceProvider.GetRequiredService<Tailor360.Platform.Persistence.Contexts.PlatformDbContext>();
            (await platform.AuditEvents.AsNoTracking().AnyAsync(entry => entry.EntityId == sessionId && entry.Action == "payments.open_session", Token)).ShouldBeTrue();
        }
    }
}
