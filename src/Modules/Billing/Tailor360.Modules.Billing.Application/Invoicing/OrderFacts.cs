using System.Text.Json;
using System.Text.Json.Serialization;
using Tailor360.Modules.Billing.Application.Abstractions;
using Tailor360.Platform.Abstractions.Time;

namespace Tailor360.Modules.Billing.Application.Invoicing;

/// <summary>
/// The shapes of Orders' events as Billing reads them: its own records over the payload's JSON, never
/// Orders' contract types (ARCH-010). Only the members Billing uses are declared; anything else in the
/// payload is ignored, which is what lets Orders add a field without a change here.
/// </summary>
public static class OrderFacts
{
    /// <summary><c>orders.order-confirmed.v1</c>.</summary>
    public const string OrderConfirmedType = "orders.order-confirmed.v1";

    /// <summary><c>orders.order-revised.v1</c>.</summary>
    public const string OrderRevisedType = "orders.order-revised.v1";

    /// <summary><c>orders.order-cancelled.v1</c>.</summary>
    public const string OrderCancelledType = "orders.order-cancelled.v1";

    /// <summary><c>orders.garment-job-created.v1</c>.</summary>
    public const string GarmentJobCreatedType = "orders.garment-job-created.v1";

    /// <summary><c>orders.job-cancelled.v1</c>.</summary>
    public const string GarmentJobCancelledType = "orders.job-cancelled.v1";

    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    /// <summary>Reads a payload as one of the fact shapes.</summary>
    public static TFact Read<TFact>(string payload)
        where TFact : class
        => JsonSerializer.Deserialize<TFact>(payload, Options)
           ?? throw new InvalidOperationException($"The payload could not be read as {typeof(TFact).Name}.");
}

/// <summary>What Billing reads from an order confirmation or a revision.</summary>
public sealed record OrderConfirmedFact(Guid AggregateId, Guid OrganisationId, Guid BranchId, Guid CustomerId, string OrderNumber, int RevisionNumber);

/// <summary>What Billing reads from an order cancellation.</summary>
public sealed record OrderCancelledFact(Guid AggregateId, Guid OrganisationId, Guid BranchId, Guid CustomerId, string OrderNumber, string ReasonCode);

/// <summary>What Billing reads from a garment job's creation.</summary>
public sealed record GarmentJobCreatedFact(Guid AggregateId, Guid OrganisationId, Guid BranchId, Guid OrderId, string GarmentJobNumber, int JobIndex);

/// <summary>What Billing reads from <c>orders.job-cancelled.v1</c>: the job, its order, and why.</summary>
public sealed record GarmentJobCancelledFact(Guid AggregateId, Guid OrganisationId, Guid BranchId, Guid OrderId, string GarmentJobNumber, string ReasonCode);

/// <summary>
/// Applies Orders' facts to Billing's own record of them. Idempotent and order-tolerant, as the
/// aggregate is: a message delivered twice or out of order leaves the same fact behind.
/// </summary>
/// <param name="store">The fact store.</param>
/// <param name="clock">The clock.</param>
public sealed class OrderFactProjector(IOrderFactStore store, IClock clock)
{
    /// <summary>Applies a confirmation or a revision.</summary>
    public async Task ApplyConfirmedAsync(OrderConfirmedFact fact, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(fact);
        Check(fact.AggregateId, fact.OrganisationId);

        var known = await store.FindAsync(fact.AggregateId, fact.OrganisationId, cancellationToken);
        if (known is null)
        {
            store.Add(Domain.Invoicing.OrderFact.Confirmed(
                fact.AggregateId, fact.OrganisationId, fact.BranchId, fact.CustomerId, fact.OrderNumber, fact.RevisionNumber, clock.UtcNow));
            return;
        }

        known.Confirm(fact.BranchId, fact.CustomerId, fact.OrderNumber, fact.RevisionNumber, clock.UtcNow);
    }

    /// <summary>Applies a cancellation, for an order heard of or not.</summary>
    public async Task ApplyCancelledAsync(OrderCancelledFact fact, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(fact);
        Check(fact.AggregateId, fact.OrganisationId);

        var known = await store.FindAsync(fact.AggregateId, fact.OrganisationId, cancellationToken);
        if (known is null)
        {
            store.Add(Domain.Invoicing.OrderFact.CancelledUnseen(
                fact.AggregateId, fact.OrganisationId, fact.BranchId, fact.CustomerId, fact.OrderNumber, fact.ReasonCode, clock.UtcNow));
            return;
        }

        known.Cancel(fact.ReasonCode, clock.UtcNow);
    }

    /// <summary>Applies a garment job's creation, for an order heard of or not.</summary>
    public async Task ApplyJobCreatedAsync(GarmentJobCreatedFact fact, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(fact);
        Check(fact.AggregateId, fact.OrganisationId);

        var known = await store.FindAsync(fact.OrderId, fact.OrganisationId, cancellationToken);
        if (known is null)
        {
            // The job arrived before its order's confirmation. The order is recorded as confirmed at
            // revision zero, which the confirmation will raise when it lands; nothing can be invoiced
            // meanwhile that the confirmation would refuse, because the customer is not yet known.
            known = Domain.Invoicing.OrderFact.Confirmed(
                fact.OrderId, fact.OrganisationId, fact.BranchId, Guid.Empty, string.Empty, 0, clock.UtcNow);
            store.Add(known);
        }

        var added = known.AddJob(fact.AggregateId, fact.GarmentJobNumber, fact.JobIndex, clock.UtcNow);
        if (added.IsFailure)
        {
            throw new InvalidOperationException($"The garment-job fact could not be recorded: {added.Error.Message}");
        }
    }

    /// <summary>Records a garment job's cancellation; a cancelled job is charged on no invoice.</summary>
    public async Task ApplyJobCancelledAsync(GarmentJobCancelledFact fact, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(fact);
        Check(fact.AggregateId, fact.OrganisationId);

        var known = await store.FindAsync(fact.OrderId, fact.OrganisationId, cancellationToken);
        if (known is null)
        {
            // Neither the order nor the job has been heard of: the same placeholder a job-created fact
            // makes, holding the job as cancelled from the start.
            known = Domain.Invoicing.OrderFact.Confirmed(
                fact.OrderId, fact.OrganisationId, fact.BranchId, Guid.Empty, string.Empty, 0, clock.UtcNow);
            store.Add(known);
        }

        known.CancelJob(fact.AggregateId, fact.GarmentJobNumber, fact.ReasonCode, clock.UtcNow);
    }

    private static void Check(Guid aggregateId, Guid organisationId)
    {
        if (aggregateId == Guid.Empty || organisationId == Guid.Empty)
        {
            // Left to retry and then dead-letter rather than recorded as an order of no organisation.
            throw new InvalidOperationException("The payload carries no aggregate or no organisation identifier.");
        }
    }
}
