using Shouldly;
using Tailor360.Modules.Billing.Application.Abstractions;
using Tailor360.Modules.Billing.Application.Invoicing;
using Tailor360.Modules.Billing.Domain.Invoicing;
using Tailor360.Platform.Abstractions.Time;

namespace Tailor360.UnitTests.Billing;

/// <summary>
/// What Billing holds about an order (#153): a fact recorded from the outbox, revised only forward, cancelled
/// once, and its garment jobs recorded once each whichever order the events arrive in.
/// </summary>
[Trait("Category", "Unit")]
public sealed class OrderFactTests
{
    private static readonly Guid OrderId = BillingTestData.Id("order-1");
    private static readonly Guid CustomerId = BillingTestData.Id("customer-1");
    private static readonly Guid JobOne = BillingTestData.Id("job-1");
    private static readonly Guid JobTwo = BillingTestData.Id("job-2");

    [Fact]
    public void ConfirmationRevisesForwardOnlyAndTrimsTheNumber()
    {
        var fact = OrderFact.Confirmed(OrderId, BillingTestData.Organisation, BillingTestData.MainBranch, CustomerId, "  ORD-1 ", 1, BillingTestData.Now);

        fact.OrderNumber.ShouldBe("ORD-1");
        fact.IsInvoiceable.ShouldBeTrue();
        fact.RevisionNumber.ShouldBe(1);

        // A later revision moves the order to another branch and raises the revision.
        fact.Confirm(BillingTestData.SecondBranch, CustomerId, "ORD-1", 3, BillingTestData.Now.AddMinutes(1));
        fact.BranchId.ShouldBe(BillingTestData.SecondBranch);
        fact.RevisionNumber.ShouldBe(3);

        // An earlier one delivered late changes nothing: the outbox is at least once and in no promised order.
        fact.Confirm(BillingTestData.MainBranch, CustomerId, "STALE", 2, BillingTestData.Now.AddMinutes(2));
        fact.BranchId.ShouldBe(BillingTestData.SecondBranch);
        fact.OrderNumber.ShouldBe("ORD-1");
        fact.RevisionNumber.ShouldBe(3);
    }

    [Fact]
    public void CancellationIsRecordedOnceAndEndsInvoicing()
    {
        var fact = OrderFact.Confirmed(OrderId, BillingTestData.Organisation, BillingTestData.MainBranch, CustomerId, "ORD-2", 1, BillingTestData.Now);

        fact.Cancel("CUSTOMER_REQUEST", BillingTestData.Now.AddHours(1));
        fact.IsInvoiceable.ShouldBeFalse();
        fact.CancelledAt.ShouldBe(BillingTestData.Now.AddHours(1));
        fact.CancellationReasonCode.ShouldBe("CUSTOMER_REQUEST");

        // Delivered again: the first cancellation stands.
        fact.Cancel("OTHER", BillingTestData.Now.AddHours(2));
        fact.CancelledAt.ShouldBe(BillingTestData.Now.AddHours(1));
        fact.CancellationReasonCode.ShouldBe("CUSTOMER_REQUEST");

        var unseen = OrderFact.CancelledUnseen(BillingTestData.Id("order-3"), BillingTestData.Organisation, BillingTestData.MainBranch, CustomerId, "ORD-3", "NO_SHOW", BillingTestData.Now);
        unseen.IsInvoiceable.ShouldBeFalse();
        unseen.Status.ShouldBe(OrderFactStatus.Cancelled);
    }

    [Fact]
    public void JobsAreRecordedOnceEachAndAnEmptyIdentifierIsRefused()
    {
        var fact = OrderFact.Confirmed(OrderId, BillingTestData.Organisation, BillingTestData.MainBranch, CustomerId, "ORD-4", 1, BillingTestData.Now);

        fact.AddJob(JobOne, "ORD-4-01", 1, BillingTestData.Now).IsSuccess.ShouldBeTrue();
        fact.AddJob(JobOne, "ORD-4-01", 1, BillingTestData.Now).IsSuccess.ShouldBeTrue();
        fact.AddJob(JobTwo, "ORD-4-02", 2, BillingTestData.Now).IsSuccess.ShouldBeTrue();
        fact.Jobs.Count.ShouldBe(2);
        fact.FindJob(JobTwo).ShouldNotBeNull().JobIndex.ShouldBe(2);
        fact.FindJob(BillingTestData.Id("job-9")).ShouldBeNull();

        var refused = fact.AddJob(Guid.Empty, "ORD-4-03", 3, BillingTestData.Now);
        refused.IsFailure.ShouldBeTrue();
        refused.Error.Target.ShouldBe("garmentJobId");
    }

    [Fact]
    public void ACancelledJobStaysCancelledWhicheverOrderTheEventsArriveIn()
    {
        var fact = OrderFact.Confirmed(OrderId, BillingTestData.Organisation, BillingTestData.MainBranch, CustomerId, "ORD-8", 1, BillingTestData.Now);
        fact.AddJob(JobOne, "ORD-8-01", 1, BillingTestData.Now).IsSuccess.ShouldBeTrue();

        fact.CancelJob(JobOne, "ORD-8-01", "WRONG_SIZE", BillingTestData.Now.AddHours(1));
        var cancelled = fact.FindJob(JobOne).ShouldNotBeNull();
        cancelled.IsCancelled.ShouldBeTrue();
        cancelled.CancelledAt.ShouldBe(BillingTestData.Now.AddHours(1));
        cancelled.CancellationReasonCode.ShouldBe("WRONG_SIZE");

        // Delivered again: the first cancellation stands.
        fact.CancelJob(JobOne, "ORD-8-01", "OTHER", BillingTestData.Now.AddHours(2));
        cancelled.CancellationReasonCode.ShouldBe("WRONG_SIZE");

        // The cancellation of a job not yet heard of records it as cancelled from the start, and the
        // creation arriving afterwards does not revive it.
        fact.CancelJob(JobTwo, "ORD-8-02", "NO_SHOW", BillingTestData.Now.AddHours(3));
        fact.AddJob(JobTwo, "ORD-8-02", 2, BillingTestData.Now.AddHours(4)).IsSuccess.ShouldBeTrue();
        fact.FindJob(JobTwo).ShouldNotBeNull().IsCancelled.ShouldBeTrue();
        fact.Jobs.Count.ShouldBe(2);
        fact.IsInvoiceable.ShouldBeTrue("a cancelled garment does not cancel its order");
    }

    [Fact]
    public async Task TheProjectorRecordsAJobCancellationForAnOrderItHasNotHeardOf()
    {
        var store = new InMemoryOrderFacts();
        var projector = new OrderFactProjector(store, new FixedClock(BillingTestData.Now));

        await projector.ApplyJobCancelledAsync(
            new GarmentJobCancelledFact(JobOne, BillingTestData.Organisation, BillingTestData.MainBranch, OrderId, "ORD-9-01", "WRONG_SIZE"), CancellationToken.None);

        var placeholder = store.Facts.Single();
        placeholder.CustomerId.ShouldBe(Guid.Empty);
        placeholder.FindJob(JobOne).ShouldNotBeNull().IsCancelled.ShouldBeTrue();
    }

    [Fact]
    public async Task TheProjectorRecordsAJobThatArrivesBeforeItsOrderAndTheConfirmationCompletesIt()
    {
        var store = new InMemoryOrderFacts();
        var projector = new OrderFactProjector(store, new FixedClock(BillingTestData.Now));

        await projector.ApplyJobCreatedAsync(
            new GarmentJobCreatedFact(JobOne, BillingTestData.Organisation, BillingTestData.MainBranch, OrderId, "ORD-5-01", 1), CancellationToken.None);

        var placeholder = store.Facts.Single();
        placeholder.OrderId.ShouldBe(OrderId);
        placeholder.CustomerId.ShouldBe(Guid.Empty, "nothing may be invoiced on an order whose customer is not known yet");
        placeholder.RevisionNumber.ShouldBe(0);
        placeholder.Jobs.Single().GarmentJobId.ShouldBe(JobOne);

        await projector.ApplyConfirmedAsync(
            new OrderConfirmedFact(OrderId, BillingTestData.Organisation, BillingTestData.MainBranch, CustomerId, "ORD-5", 1), CancellationToken.None);

        store.Facts.Count.ShouldBe(1);
        placeholder.CustomerId.ShouldBe(CustomerId);
        placeholder.OrderNumber.ShouldBe("ORD-5");
        placeholder.RevisionNumber.ShouldBe(1);
        placeholder.Jobs.Count.ShouldBe(1);

        await projector.ApplyCancelledAsync(
            new OrderCancelledFact(OrderId, BillingTestData.Organisation, BillingTestData.MainBranch, CustomerId, "ORD-5", "CHANGED_MIND"), CancellationToken.None);
        placeholder.IsInvoiceable.ShouldBeFalse();
    }

    [Fact]
    public async Task TheProjectorRefusesAPayloadWithNoAggregateOrNoOrganisation()
    {
        var projector = new OrderFactProjector(new InMemoryOrderFacts(), new FixedClock(BillingTestData.Now));

        await Should.ThrowAsync<InvalidOperationException>(() => projector.ApplyConfirmedAsync(
            new OrderConfirmedFact(Guid.Empty, BillingTestData.Organisation, BillingTestData.MainBranch, CustomerId, "ORD-6", 1), CancellationToken.None));
        await Should.ThrowAsync<InvalidOperationException>(() => projector.ApplyCancelledAsync(
            new OrderCancelledFact(OrderId, Guid.Empty, BillingTestData.MainBranch, CustomerId, "ORD-6", "X"), CancellationToken.None));
    }

    [Fact]
    public void PayloadsAreReadByTheWireNamesTheOrdersModulePublishes()
    {
        // The Orders contract serialises with the web defaults: camel case, enums as strings. Only the
        // members Billing needs are read; the rest of the payload is ignored rather than refused.
        const string payload = """
            {"eventId":"0199c000-0000-7000-8000-000000000001","occurredAt":"2026-09-12T04:30:00+00:00",
             "aggregateId":"0199c000-0000-7000-8000-000000000002","organisationId":"0199c000-0000-7000-8000-0000000000aa",
             "branchId":"0199c000-0000-7000-8000-00000000000a","customerId":"0199c000-0000-7000-8000-000000000003",
             "orderNumber":"ORD-7","orderDraftId":"0199c000-0000-7000-8000-000000000004","estimateId":null,
             "dueDate":"2026-09-20","garmentJobCount":2,"revisionNumber":1,"eventType":"orders.order-confirmed.v1"}
            """;

        var fact = OrderFacts.Read<OrderConfirmedFact>(payload);

        fact.AggregateId.ShouldBe(Guid.Parse("0199c000-0000-7000-8000-000000000002"));
        fact.CustomerId.ShouldBe(Guid.Parse("0199c000-0000-7000-8000-000000000003"));
        fact.OrderNumber.ShouldBe("ORD-7");
        fact.RevisionNumber.ShouldBe(1);
    }

    private sealed class InMemoryOrderFacts : IOrderFactStore
    {
        public List<OrderFact> Facts { get; } = [];

        public Task<OrderFact?> FindAsync(Guid orderId, Guid organisationId, CancellationToken cancellationToken = default)
            => Task.FromResult(Facts.Find(fact => fact.OrderId == orderId && fact.OrganisationId == organisationId));

        public void Add(OrderFact fact) => Facts.Add(fact);

        public Task LockForReadAsync(Guid orderId, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class FixedClock(DateTimeOffset now) : IClock
    {
        public DateTimeOffset UtcNow => now;

        public DateOnly TodayIn(TimeZoneInfo timeZone) => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(now, timeZone).DateTime);
    }
}
