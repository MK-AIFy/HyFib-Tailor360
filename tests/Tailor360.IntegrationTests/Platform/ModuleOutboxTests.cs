using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Tailor360.IntegrationTests.Customers;
using Tailor360.Modules.Customers.Application.Abstractions;
using Tailor360.Modules.Customers.Infrastructure.Persistence;
using Tailor360.Platform.Abstractions.Events;
using Tailor360.Platform.Abstractions.Identifiers;
using Tailor360.Platform.Abstractions.Time;
using Tailor360.Platform.Persistence.Contexts;

namespace Tailor360.IntegrationTests.Platform;

/// <summary>
/// The property a transactional outbox exists for, asked of a real module: an event and the change it
/// describes commit together, or neither does.
/// </summary>
/// <remarks>
/// <para>
/// This could not be asserted before issue #77. The outbox was one shared table on the platform's
/// context while a module's change was on the module's own, so publishing beside a write was two
/// connections and two transactions: saving the change first could commit work whose event was lost,
/// and saving the event first could announce work that rolled back. The existing
/// <see cref="OutboxTests"/> rollback test passed throughout, because it published and rolled back on
/// the platform context alone — the one caller for whom the guarantee already held.
/// </para>
/// <para>
/// So these run against the composed application and a real module. What is under test is the
/// arrangement, not a class: that the publisher a module resolves writes into the module's own
/// context, and therefore into the module's own transaction.
/// </para>
/// </remarks>
[Trait("Category", "Integration")]
[Collection(WebApplicationCollection.Name)]
public sealed class ModuleOutboxTests(WebApplicationFixture fixture)
{
    private static readonly Guid BranchId = Guid.Parse("0199c000-0000-7000-8000-0000000000d1");

    private const string BranchCode = "OUTB1";

    /// <summary>
    /// The direction that used to lose events. The change commits, and the event is there because the
    /// same save wrote it.
    /// </summary>
    [Fact]
    public async Task AnEventCommitsWithTheChangeItDescribes()
    {
        Assert.SkipUnless(DatabaseAvailability.IsAvailable, DatabaseAvailability.SkipReason);

        var customerId = await CustomerAsync();
        var eventId = Guid.CreateVersion7();

        using (var scope = fixture.Services.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<CustomersDbContext>();
            var publisher = scope.ServiceProvider.GetRequiredService<ICustomersEventPublisher>();
            var clock = scope.ServiceProvider.GetRequiredService<IClock>();

            var customer = await context.Customers.SingleAsync(
                record => record.Id == customerId, TestContext.Current.CancellationToken);

            customer.Deactivate(clock.UtcNow, null).IsSuccess.ShouldBeTrue();
            publisher.Publish(new SampleModuleEvent(eventId, clock.UtcNow, customerId));

            // One save. Not one for the aggregate and another for the event.
            await context.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        (await OutboxRowsAsync(eventId)).ShouldBe(1);
    }

    /// <summary>
    /// The direction the old arrangement got right by accident, asked of a module: nobody may hear
    /// about work that did not happen.
    /// </summary>
    [Fact]
    public async Task RollingTheModulesChangeBackDiscardsItsEvent()
    {
        Assert.SkipUnless(DatabaseAvailability.IsAvailable, DatabaseAvailability.SkipReason);

        var customerId = await CustomerAsync();
        var eventId = Guid.CreateVersion7();

        using (var scope = fixture.Services.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<CustomersDbContext>();
            var publisher = scope.ServiceProvider.GetRequiredService<ICustomersEventPublisher>();
            var clock = scope.ServiceProvider.GetRequiredService<IClock>();

            // The module's context retries on transient failure, and a retrying strategy refuses a
            // transaction it did not open — the same reason production code that spans a transaction
            // goes through the strategy.
            var strategy = context.Database.CreateExecutionStrategy();

            await strategy.ExecuteAsync(async () =>
            {
                await using var transaction = await context.Database.BeginTransactionAsync(
                    TestContext.Current.CancellationToken);

                var customer = await context.Customers.SingleAsync(
                    record => record.Id == customerId, TestContext.Current.CancellationToken);

                customer.Deactivate(clock.UtcNow, null).IsSuccess.ShouldBeTrue();
                publisher.Publish(new SampleModuleEvent(eventId, clock.UtcNow, customerId));

                await context.SaveChangesAsync(TestContext.Current.CancellationToken);
                await transaction.RollbackAsync(TestContext.Current.CancellationToken);
            });
        }

        (await OutboxRowsAsync(eventId)).ShouldBe(0);

        // And the change went with it, which is the other half of "together".
        using var reading = fixture.Services.CreateScope();
        var reader = reading.ServiceProvider.GetRequiredService<CustomersDbContext>();

        (await reader.Customers.AsNoTracking().SingleAsync(
            record => record.Id == customerId, TestContext.Current.CancellationToken))
            .Status.ShouldBe(Modules.Customers.Domain.Customers.CustomerStatus.Active);
    }

    /// <summary>
    /// A module's events land in the module's own schema. The publisher is bound to one context, so
    /// there is no reachable arrangement in which they land anywhere else — which is what keeps
    /// publishing inside the boundary ARCH-005 draws.
    /// </summary>
    [Fact]
    public async Task AModulesEventIsWrittenToItsOwnOutboxAndNotThePlatforms()
    {
        Assert.SkipUnless(DatabaseAvailability.IsAvailable, DatabaseAvailability.SkipReason);

        var customerId = await CustomerAsync();
        var eventId = Guid.CreateVersion7();

        using (var scope = fixture.Services.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<CustomersDbContext>();
            var publisher = scope.ServiceProvider.GetRequiredService<ICustomersEventPublisher>();
            var clock = scope.ServiceProvider.GetRequiredService<IClock>();

            publisher.Publish(new SampleModuleEvent(eventId, clock.UtcNow, customerId));
            await context.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        (await OutboxRowsAsync(eventId)).ShouldBe(1);

        using var reading = fixture.Services.CreateScope();
        var platform = reading.ServiceProvider.GetRequiredService<PlatformDbContext>();

        (await platform.OutboxMessages
            .AsNoTracking()
            .CountAsync(message => message.Id == eventId, TestContext.Current.CancellationToken))
            .ShouldBe(0);
    }

    private async Task<int> OutboxRowsAsync(Guid eventId)
    {
        using var scope = fixture.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<CustomersDbContext>();

        return await context.OutboxMessages
            .AsNoTracking()
            .CountAsync(message => message.Id == eventId, TestContext.Current.CancellationToken);
    }

    private async Task<Guid> CustomerAsync()
    {
        await CustomerHarness.BranchAsync(fixture, BranchId, BranchCode);

        return await CustomerHarness.CustomerAsync(fixture, BranchId);
    }

    /// <summary>
    /// A fact with no consumer, published only so that the arrangement can be asked about. The three
    /// events issue #26 names wait on their own change; what is under test here is that a module can
    /// publish anything at all without losing it.
    /// </summary>
    private sealed record SampleModuleEvent(Guid EventId, DateTimeOffset OccurredAt, Guid AggregateId)
        : IntegrationEvent(EventId, OccurredAt, AggregateId)
    {
        public override string EventType => "tests.module-published.v1";
    }
}
