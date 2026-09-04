using Microsoft.EntityFrameworkCore;
using Npgsql;
using Shouldly;
using Tailor360.Platform.Abstractions.Auditing;
using Tailor360.Platform.Abstractions.Identifiers;
using Tailor360.Platform.Abstractions.Time;
using Tailor360.Platform.Persistence.Auditing;
using Tailor360.Platform.Persistence.Contexts;
using Tailor360.Platform.Persistence.Entities;

namespace Tailor360.IntegrationTests.Platform;

/// <summary>
/// The audit trail's guarantees, exercised against a real database. These are the properties the trail
/// is for: an entry cannot be edited, an entry cannot exist for a change that rolled back, and tampering
/// by someone who can bypass the triggers is still detectable.
/// </summary>
[Collection(PlatformDatabaseCollection.Name)]
[Trait("Category", "Integration")]
public sealed class AuditTrailTests(PlatformDatabaseFixture fixture)
{
    [Fact(Skip = DatabaseAvailability.SkipMessage, SkipUnless = nameof(Available), SkipType = typeof(AuditTrailTests))]
    public async Task ChainsEachEntryToItsPredecessor()
    {
        await using var context = await fixture.CreateDatabaseAsync("auditchain");

        await WriteAsync(context, "orders.order.confirmed", "Order confirmed");
        await WriteAsync(context, "billing.invoice.posted", "Invoice posted");
        await WriteAsync(context, "custody.scan.recorded", "Scan recorded");

        var entries = await context.AuditEvents.AsNoTracking().OrderBy(e => e.Sequence).ToListAsync(
            TestContext.Current.CancellationToken);

        entries.Count.ShouldBe(3);
        entries[0].Sequence.ShouldBe(1);
        entries[0].PreviousHash.ShouldBe(new string('0', 64));
        entries[1].PreviousHash.ShouldBe(entries[0].Hash);
        entries[2].PreviousHash.ShouldBe(entries[1].Hash);
        entries.Select(e => e.Hash).Distinct().Count().ShouldBe(3);
    }

    [Fact(Skip = DatabaseAvailability.SkipMessage, SkipUnless = nameof(Available), SkipType = typeof(AuditTrailTests))]
    public async Task VerificationPassesOnAnUntouchedTrail()
    {
        await using var context = await fixture.CreateDatabaseAsync("auditverify");

        await WriteAsync(context, "orders.order.confirmed", "Order confirmed");
        await WriteAsync(context, "orders.order.revised", "Order revised");

        (await VerifyChainAsync(context)).ShouldBeNull();
    }

    [Fact(Skip = DatabaseAvailability.SkipMessage, SkipUnless = nameof(Available), SkipType = typeof(AuditTrailTests))]
    public async Task RefusesAnUpdate()
    {
        await using var context = await fixture.CreateDatabaseAsync("auditupdate");
        await WriteAsync(context, "orders.order.confirmed", "Order confirmed");

        var exception = await Should.ThrowAsync<PostgresException>(async () =>
            await context.Database.ExecuteSqlRawAsync(
                "UPDATE platform.audit_events SET summary = 'edited'",
                TestContext.Current.CancellationToken));

        exception.MessageText.ShouldContain("append-only");
    }

    [Fact(Skip = DatabaseAvailability.SkipMessage, SkipUnless = nameof(Available), SkipType = typeof(AuditTrailTests))]
    public async Task RefusesADelete()
    {
        await using var context = await fixture.CreateDatabaseAsync("auditdelete");
        await WriteAsync(context, "orders.order.confirmed", "Order confirmed");

        var exception = await Should.ThrowAsync<PostgresException>(async () =>
            await context.Database.ExecuteSqlRawAsync(
                "DELETE FROM platform.audit_events",
                TestContext.Current.CancellationToken));

        exception.MessageText.ShouldContain("append-only");
    }

    [Fact(Skip = DatabaseAvailability.SkipMessage, SkipUnless = nameof(Available), SkipType = typeof(AuditTrailTests))]
    public async Task DetectsTamperingPerformedWithTheTriggersDisabled()
    {
        // Someone holding database ownership can turn a trigger off. They cannot make the hashes agree,
        // which is the whole reason the chain exists rather than relying on the trigger alone.
        await using var context = await fixture.CreateDatabaseAsync("audittamper");

        await WriteAsync(context, "orders.order.confirmed", "Order confirmed");
        await WriteAsync(context, "billing.invoice.posted", "Invoice posted");
        await WriteAsync(context, "billing.payment.recorded", "Payment recorded");

        var partition = await ScalarAsync<string>(
            context,
            """
            SELECT c.relname FROM pg_class c
            JOIN pg_namespace n ON n.oid = c.relnamespace
            WHERE n.nspname = 'platform' AND c.relname LIKE 'audit_events_2%' AND c.relkind = 'r'
            ORDER BY c.relname LIMIT 1
            """);

        // The partition name comes from the catalogue, not from input, and a table name cannot be a
        // parameter in any case; the interpolation warning is suppressed for exactly that reason.
#pragma warning disable EF1002
        await context.Database.ExecuteSqlRawAsync(
            $"""
             ALTER TABLE platform."{partition}" DISABLE TRIGGER USER;
             UPDATE platform."{partition}" SET summary = 'tampered' WHERE sequence = 2;
             ALTER TABLE platform."{partition}" ENABLE TRIGGER USER;
             """,
            TestContext.Current.CancellationToken);
#pragma warning restore EF1002

        var broken = await VerifyChainAsync(context);

        broken.ShouldNotBeNull();
        broken.Value.Sequence.ShouldBe(2);
        broken.Value.Reason.ShouldBe("content hash mismatch");
    }

    [Fact(Skip = DatabaseAvailability.SkipMessage, SkipUnless = nameof(Available), SkipType = typeof(AuditTrailTests))]
    public async Task WritesTheEntryInTheSameTransactionAsTheChange()
    {
        await using var context = await fixture.CreateDatabaseAsync("auditatomic");

        await using (var transaction = await context.Database.BeginTransactionAsync(
            TestContext.Current.CancellationToken))
        {
            context.FeatureFlags.Add(new FeatureFlag
            {
                Key = "sample.flag",
                ScopeType = FeatureFlagScopes.Organisation,
                ScopeId = Guid.Empty,
                Enabled = true,
                UpdatedAt = DateTimeOffset.UtcNow,
            });

            await NewWriter(context).WriteAsync(
                new AuditEntry("platform.feature_flag.changed", "FeatureFlag", Guid.Empty, "Flag on"),
                TestContext.Current.CancellationToken);

            await context.SaveChangesAsync(TestContext.Current.CancellationToken);
            await transaction.RollbackAsync(TestContext.Current.CancellationToken);
        }

        // Neither the change nor the record of it survives. An audit trail that could disagree with the
        // data would be worse than none, because it would be believed.
        context.ChangeTracker.Clear();
        (await context.FeatureFlags.CountAsync(TestContext.Current.CancellationToken)).ShouldBe(0);
        (await context.AuditEvents.CountAsync(TestContext.Current.CancellationToken)).ShouldBe(0);
    }

    [Fact(Skip = DatabaseAvailability.SkipMessage, SkipUnless = nameof(Available), SkipType = typeof(AuditTrailTests))]
    public async Task StoresStateSnapshotsAsJson()
    {
        await using var context = await fixture.CreateDatabaseAsync("auditjson");

        await NewWriter(context).WriteAsync(
            new AuditEntry(
                "platform.feature_flag.changed",
                "FeatureFlag",
                Guid.Empty,
                "Flag changed",
                "Pilot rehearsal",
                Before: true,
                After: new { enabled = false, version = 2 }),
            TestContext.Current.CancellationToken);

        await context.SaveChangesAsync(TestContext.Current.CancellationToken);

        var entry = await context.AuditEvents.AsNoTracking().SingleAsync(TestContext.Current.CancellationToken);
        entry.Before.ShouldBe("true");
        entry.After.ShouldNotBeNull().ShouldContain("\"enabled\": false");
        entry.Reason.ShouldBe("Pilot rehearsal");
    }

    /// <summary>Gate used by the skip conditions on every test in this class.</summary>
    public static bool Available => PlatformDatabaseFixture.IsAvailable;

    private static AuditWriter NewWriter(PlatformDbContext context)
        => new(context, new SystemAuditContext(), new SystemClock(), new UuidV7IdGenerator());

    private static async Task WriteAsync(PlatformDbContext context, string action, string summary)
    {
        await NewWriter(context).WriteAsync(
            new AuditEntry(action, "Sample", Guid.CreateVersion7(), summary),
            TestContext.Current.CancellationToken);

        await context.SaveChangesAsync(TestContext.Current.CancellationToken);
        context.ChangeTracker.Clear();
    }

    private static async Task<(long Sequence, string Reason)?> VerifyChainAsync(PlatformDbContext context)
    {
        var connection = (NpgsqlConnection)context.Database.GetDbConnection();
        await context.Database.OpenConnectionAsync(TestContext.Current.CancellationToken);
        try
        {
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT broken_sequence, reason FROM platform.verify_audit_chain()";

            await using var reader = await command.ExecuteReaderAsync(TestContext.Current.CancellationToken);
            return await reader.ReadAsync(TestContext.Current.CancellationToken)
                ? (reader.GetInt64(0), reader.GetString(1))
                : null;
        }
        finally
        {
            await context.Database.CloseConnectionAsync();
        }
    }

    private static async Task<T> ScalarAsync<T>(PlatformDbContext context, string sql)
    {
        var connection = (NpgsqlConnection)context.Database.GetDbConnection();
        await context.Database.OpenConnectionAsync(TestContext.Current.CancellationToken);
        try
        {
            await using var command = connection.CreateCommand();
            command.CommandText = sql;
            var result = await command.ExecuteScalarAsync(TestContext.Current.CancellationToken);
            return (T)result!;
        }
        finally
        {
            await context.Database.CloseConnectionAsync();
        }
    }
}
