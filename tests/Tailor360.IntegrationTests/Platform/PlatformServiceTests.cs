using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Shouldly;
using Tailor360.Platform.Abstractions.FeatureFlags;
using Tailor360.Platform.Abstractions.Idempotency;
using Tailor360.Platform.Abstractions.Multitenancy;
using Tailor360.Platform.Abstractions.Time;
using Tailor360.Platform.Persistence.Contexts;
using Tailor360.Platform.Persistence.Entities;
using Tailor360.Platform.Persistence.FeatureFlags;
using Tailor360.Platform.Persistence.Idempotency;
using Tailor360.Platform.Persistence.Sequencing;

namespace Tailor360.IntegrationTests.Platform;

/// <summary>
/// Document numbering, idempotency, optimistic concurrency and feature flags, against a real database.
/// Each of these behaves correctly only because of something PostgreSQL does, so none of them can be
/// verified without it.
/// </summary>
[Collection(PlatformDatabaseCollection.Name)]
[Trait("Category", "Integration")]
public sealed class PlatformServiceTests(PlatformDatabaseFixture fixture)
{
    /// <summary>Gate used by the skip conditions on every test in this class.</summary>
    public static bool Available => PlatformDatabaseFixture.IsAvailable;

    [Fact(Skip = DatabaseAvailability.SkipMessage, SkipUnless = nameof(Available), SkipType = typeof(PlatformServiceTests))]
    public async Task DocumentNumbersAreAllocatedInSequenceAndPerScope()
    {
        await using var context = await fixture.CreateDatabaseAsync("sequences");
        var allocator = new SequenceAllocator(context);

        var first = await allocator.NextAsync("invoice", "BR1:2026-27", TestContext.Current.CancellationToken);
        var second = await allocator.NextAsync("invoice", "BR1:2026-27", TestContext.Current.CancellationToken);
        var otherBranch = await allocator.NextAsync("invoice", "BR2:2026-27", TestContext.Current.CancellationToken);
        var otherYear = await allocator.NextAsync("invoice", "BR1:2027-28", TestContext.Current.CancellationToken);

        first.ShouldBe(1);
        second.ShouldBe(2);

        // Each branch and each financial year numbers from one, because that is how the series are read.
        otherBranch.ShouldBe(1);
        otherYear.ShouldBe(1);
    }

    [Fact(Skip = DatabaseAvailability.SkipMessage, SkipUnless = nameof(Available), SkipType = typeof(PlatformServiceTests))]
    public async Task ARolledBackDocumentReturnsItsNumberSoTheSeriesHasNoGap()
    {
        await using var context = await fixture.CreateDatabaseAsync("sequencegap");
        var allocator = new SequenceAllocator(context);

        await using (var transaction = await context.Database.BeginTransactionAsync(
            TestContext.Current.CancellationToken))
        {
            (await allocator.NextAsync("invoice", "BR1:2026-27", TestContext.Current.CancellationToken))
                .ShouldBe(1);
            await transaction.RollbackAsync(TestContext.Current.CancellationToken);
        }

        // A statutory series with holes is a problem at audit time, which is why a database sequence is
        // not used here: a sequence deliberately keeps its value through a rollback.
        (await allocator.NextAsync("invoice", "BR1:2026-27", TestContext.Current.CancellationToken))
            .ShouldBe(1);
    }

    [Fact(Skip = DatabaseAvailability.SkipMessage, SkipUnless = nameof(Available), SkipType = typeof(PlatformServiceTests))]
    public async Task ARepeatedCommandReplaysTheFirstOutcome()
    {
        await using var context = await fixture.CreateDatabaseAsync("idempotency");
        var store = NewStore(context);

        var fingerprint = IdempotencyStore.Fingerprint("""{"orderId":"a"}""");

        var first = await store.ClaimAsync("user-1", "POST /orders", "key-1", fingerprint,
            TestContext.Current.CancellationToken);
        first.Outcome.ShouldBe(IdempotencyOutcome.Proceed);

        var whileRunning = await store.ClaimAsync("user-1", "POST /orders", "key-1", fingerprint,
            TestContext.Current.CancellationToken);
        whileRunning.Outcome.ShouldBe(IdempotencyOutcome.InProgress);

        await store.CompleteAsync("user-1", "POST /orders", "key-1", 201, """{"id":"o-1"}""",
            TestContext.Current.CancellationToken);

        var afterCompletion = await store.ClaimAsync("user-1", "POST /orders", "key-1", fingerprint,
            TestContext.Current.CancellationToken);

        afterCompletion.Outcome.ShouldBe(IdempotencyOutcome.ReplayStoredResponse);
        afterCompletion.StatusCode.ShouldBe(201);
        afterCompletion.ResponseBody.ShouldBe("""{"id":"o-1"}""");
    }

    [Fact(Skip = DatabaseAvailability.SkipMessage, SkipUnless = nameof(Available), SkipType = typeof(PlatformServiceTests))]
    public async Task ReusingAKeyForADifferentRequestIsRejected()
    {
        await using var context = await fixture.CreateDatabaseAsync("idempotencyreuse");
        var store = NewStore(context);

        await store.ClaimAsync("user-1", "POST /orders", "key-1",
            IdempotencyStore.Fingerprint("""{"orderId":"a"}"""), TestContext.Current.CancellationToken);

        var conflict = await store.ClaimAsync("user-1", "POST /orders", "key-1",
            IdempotencyStore.Fingerprint("""{"orderId":"b"}"""), TestContext.Current.CancellationToken);

        // Answering with the first result would silently discard the second order. The client is wrong
        // and has to be told so.
        conflict.Outcome.ShouldBe(IdempotencyOutcome.KeyReuseConflict);
    }

    [Fact(Skip = DatabaseAvailability.SkipMessage, SkipUnless = nameof(Available), SkipType = typeof(PlatformServiceTests))]
    public async Task OneUsersKeyDoesNotCollideWithAnothers()
    {
        await using var context = await fixture.CreateDatabaseAsync("idempotencyscope");
        var store = NewStore(context);
        var fingerprint = IdempotencyStore.Fingerprint("""{"orderId":"a"}""");

        await store.ClaimAsync("user-1", "POST /orders", "key-1", fingerprint,
            TestContext.Current.CancellationToken);

        var otherUser = await store.ClaimAsync("user-2", "POST /orders", "key-1", fingerprint,
            TestContext.Current.CancellationToken);
        var otherRoute = await store.ClaimAsync("user-1", "POST /payments", "key-1", fingerprint,
            TestContext.Current.CancellationToken);

        otherUser.Outcome.ShouldBe(IdempotencyOutcome.Proceed);
        otherRoute.Outcome.ShouldBe(IdempotencyOutcome.Proceed);
    }

    [Fact(Skip = DatabaseAvailability.SkipMessage, SkipUnless = nameof(Available), SkipType = typeof(PlatformServiceTests))]
    public async Task RetentionOutlivesTheLongestOfflineQueue()
    {
        var options = new IdempotencyOptions { MaximumOfflineQueueAge = TimeSpan.FromDays(5) };

        // A record deleted before its replay could arrive would let the command run a second time.
        options.Retention.ShouldBeGreaterThan(options.MaximumOfflineQueueAge);
        options.Retention.ShouldBe(TimeSpan.FromDays(10));

        new IdempotencyOptions { MaximumOfflineQueueAge = TimeSpan.FromHours(6) }
            .Retention.ShouldBe(TimeSpan.FromDays(7), "a floor keeps a short queue setting from shortening retention");

        await Task.CompletedTask;
    }

    [Fact(Skip = DatabaseAvailability.SkipMessage, SkipUnless = nameof(Available), SkipType = typeof(PlatformServiceTests))]
    public async Task ExpiredIdempotencyRecordsArePurged()
    {
        await using var context = await fixture.CreateDatabaseAsync("idempotencypurge");
        var store = NewStore(context);

        await store.ClaimAsync("user-1", "POST /orders", "key-1", "fingerprint",
            TestContext.Current.CancellationToken);

        await context.Database.ExecuteSqlRawAsync(
            "UPDATE platform.idempotency_keys SET expires_at = now() - interval '1 day'",
            TestContext.Current.CancellationToken);

        (await store.PurgeExpiredAsync(TestContext.Current.CancellationToken)).ShouldBe(1);
        (await context.IdempotencyRecords.CountAsync(TestContext.Current.CancellationToken)).ShouldBe(0);
    }

    [Fact(Skip = DatabaseAvailability.SkipMessage, SkipUnless = nameof(Available), SkipType = typeof(PlatformServiceTests))]
    public async Task ASecondWriterLosesOnAConcurrentUpdate()
    {
        await using var context = await fixture.CreateDatabaseAsync("concurrency");
        await using var other = PlatformDatabaseFixture.OpenSecondContext(context);

        context.FeatureFlags.Add(new FeatureFlag
        {
            Key = "sample.flag",
            ScopeType = FeatureFlagScopes.Organisation,
            ScopeId = Guid.Empty,
            Enabled = false,
            UpdatedAt = DateTimeOffset.UtcNow,
        });
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);
        context.ChangeTracker.Clear();

        var mine = await context.FeatureFlags.SingleAsync(TestContext.Current.CancellationToken);
        var theirs = await other.FeatureFlags.SingleAsync(TestContext.Current.CancellationToken);

        mine.Enabled = true;
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);

        // A different value, so that Entity Framework actually issues an update: assigning the value the
        // row already had would produce no statement and therefore no conflict to detect.
        theirs.Reason = "A conflicting change made from another session.";

        // Last write wins would silently discard the first change. The xmin token turns that into an
        // error the caller must resolve.
        await Should.ThrowAsync<DbUpdateConcurrencyException>(
            async () => await other.SaveChangesAsync(TestContext.Current.CancellationToken));
    }

    [Fact(Skip = DatabaseAvailability.SkipMessage, SkipUnless = nameof(Available), SkipType = typeof(PlatformServiceTests))]
    public async Task AnUnknownFlagIsOff()
    {
        await using var context = await fixture.CreateDatabaseAsync("flagsunknown");
        await using var provider = PlatformServiceHarness.Build(context);

        using var store = new FeatureFlagStore(
            provider.GetRequiredService<IServiceScopeFactory>(),
            new SystemClock(),
            Options.Create(new FeatureFlagOptions()));

        // A feature nobody has configured must not be live.
        (await store.IsEnabledAsync("never.configured", new OrganisationContext(Guid.Empty, null),
            TestContext.Current.CancellationToken)).ShouldBeFalse();
    }

    [Fact(Skip = DatabaseAvailability.SkipMessage, SkipUnless = nameof(Available), SkipType = typeof(PlatformServiceTests))]
    public async Task ABranchValueOverridesTheOrganisationValueForThatBranchOnly()
    {
        await using var context = await fixture.CreateDatabaseAsync("flagsscope");
        var branchId = Guid.CreateVersion7();
        var otherBranchId = Guid.CreateVersion7();

        context.FeatureFlags.AddRange(
            new FeatureFlag
            {
                Key = "pilot.feature",
                ScopeType = FeatureFlagScopes.Organisation,
                ScopeId = Guid.Empty,
                Enabled = true,
                UpdatedAt = DateTimeOffset.UtcNow,
            },
            new FeatureFlag
            {
                Key = "pilot.feature",
                ScopeType = FeatureFlagScopes.Branch,
                ScopeId = branchId,
                Enabled = false,
                UpdatedAt = DateTimeOffset.UtcNow,
            });
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);

        await using var provider = PlatformServiceHarness.Build(context);
        using var store = new FeatureFlagStore(
            provider.GetRequiredService<IServiceScopeFactory>(),
            new SystemClock(),
            Options.Create(new FeatureFlagOptions()));

        (await store.IsEnabledAsync("pilot.feature", new OrganisationContext(Guid.Empty, branchId),
            TestContext.Current.CancellationToken)).ShouldBeFalse("the branch value wins for that branch");

        (await store.IsEnabledAsync("pilot.feature", new OrganisationContext(Guid.Empty, otherBranchId),
            TestContext.Current.CancellationToken)).ShouldBeTrue("a branch with no value follows the organisation");

        (await store.IsEnabledAsync("pilot.feature", new OrganisationContext(Guid.Empty, null),
            TestContext.Current.CancellationToken)).ShouldBeTrue();
    }

    [Fact(Skip = DatabaseAvailability.SkipMessage, SkipUnless = nameof(Available), SkipType = typeof(PlatformServiceTests))]
    public async Task AChangeIsVisibleToTheNodeThatMadeItImmediately()
    {
        await using var context = await fixture.CreateDatabaseAsync("flagsinvalidate");
        await using var provider = PlatformServiceHarness.Build(context);

        using var store = new FeatureFlagStore(
            provider.GetRequiredService<IServiceScopeFactory>(),
            new SystemClock(),
            Options.Create(new FeatureFlagOptions { PropagationBound = TimeSpan.FromMinutes(5) }));

        var scope = new OrganisationContext(Guid.Empty, null);
        (await store.IsEnabledAsync("pilot.feature", scope, TestContext.Current.CancellationToken)).ShouldBeFalse();

        context.FeatureFlags.Add(new FeatureFlag
        {
            Key = "pilot.feature",
            ScopeType = FeatureFlagScopes.Organisation,
            ScopeId = Guid.Empty,
            Enabled = true,
            UpdatedAt = DateTimeOffset.UtcNow,
        });
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);

        // Without invalidation the node that made the change would keep serving the old value for the
        // whole propagation window, which is the most confusing possible moment to be stale.
        store.Invalidate();

        (await store.IsEnabledAsync("pilot.feature", scope, TestContext.Current.CancellationToken)).ShouldBeTrue();
    }

    private static IdempotencyStore NewStore(PlatformDbContext context)
        => new(context, new SystemClock(), Options.Create(new IdempotencyOptions()));
}
