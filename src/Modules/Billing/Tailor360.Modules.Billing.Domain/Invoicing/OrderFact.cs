using Tailor360.Platform.Abstractions.Results;

namespace Tailor360.Modules.Billing.Domain.Invoicing;

/// <summary>
/// What Billing knows about an order: enough to draft an invoice for it and nothing else. Written only
/// by the consumers of Orders' integration events, never by a request, and never by reading Orders'
/// tables (ARCH-010).
/// </summary>
/// <remarks>
/// Events arrive at least once and not always in order — a job-created message may land before the
/// confirmation it belongs to, and a cancellation may arrive for an order this module has not heard
/// of. Every method here is therefore idempotent and tolerant of order: a fact is upserted, never
/// refused for arriving early, and a later revision number wins over an earlier one.
/// </remarks>
public sealed class OrderFact
{
    /// <summary>The longest display number kept.</summary>
    public const int MaximumNumberLength = 40;

    private readonly List<OrderFactJob> _jobs = [];

    private OrderFact()
    {
        // The persistence layer materialises instances through this constructor.
    }

    private OrderFact(Guid orderId, Guid organisationId, Guid branchId, Guid customerId, string orderNumber, DateTimeOffset now)
    {
        OrderId = orderId;
        OrganisationId = organisationId;
        BranchId = branchId;
        CustomerId = customerId;
        OrderNumber = orderNumber;
        Status = OrderFactStatus.Confirmed;
        FirstSeenAt = now;
        UpdatedAt = now;
    }

    /// <summary>The order's identity in Orders, which is the key here too.</summary>
    public Guid OrderId { get; private set; }

    /// <summary>The organisation.</summary>
    public Guid OrganisationId { get; private set; }

    /// <summary>The branch the order was taken at.</summary>
    public Guid BranchId { get; private set; }

    /// <summary>The customer the order is for.</summary>
    public Guid CustomerId { get; private set; }

    /// <summary>The display number, for the invoice to name.</summary>
    public string OrderNumber { get; private set; } = string.Empty;

    /// <summary>The highest revision heard of.</summary>
    public int RevisionNumber { get; private set; }

    /// <summary>Confirmed or cancelled.</summary>
    public OrderFactStatus Status { get; private set; }

    /// <summary>When the cancellation was heard, or null.</summary>
    public DateTimeOffset? CancelledAt { get; private set; }

    /// <summary>The reason code the cancellation carried, or null.</summary>
    public string? CancellationReasonCode { get; private set; }

    /// <summary>When the first fact arrived.</summary>
    public DateTimeOffset FirstSeenAt { get; private set; }

    /// <summary>When the last fact arrived.</summary>
    public DateTimeOffset UpdatedAt { get; private set; }

    /// <summary>The garment jobs heard of.</summary>
    public IReadOnlyCollection<OrderFactJob> Jobs => _jobs;

    /// <summary>Whether a draft invoice may be created for the order.</summary>
    public bool IsInvoiceable => Status == OrderFactStatus.Confirmed;

    /// <summary>Records a confirmation: the first fact, or a later revision of one already known.</summary>
    public static OrderFact Confirmed(Guid orderId, Guid organisationId, Guid branchId, Guid customerId, string orderNumber, int revisionNumber, DateTimeOffset now)
    {
        var fact = new OrderFact(orderId, organisationId, branchId, customerId, Trim(orderNumber), now);
        fact.RevisionNumber = revisionNumber;

        return fact;
    }

    /// <summary>Records a cancellation heard before any confirmation.</summary>
    public static OrderFact CancelledUnseen(Guid orderId, Guid organisationId, Guid branchId, Guid customerId, string orderNumber, string reasonCode, DateTimeOffset now)
    {
        var fact = new OrderFact(orderId, organisationId, branchId, customerId, Trim(orderNumber), now);
        fact.Cancel(reasonCode, now);

        return fact;
    }

    /// <summary>Applies a confirmation or a revision. A lower revision than the one known changes nothing.</summary>
    public void Confirm(Guid branchId, Guid customerId, string orderNumber, int revisionNumber, DateTimeOffset now)
    {
        if (revisionNumber < RevisionNumber)
        {
            return;
        }

        BranchId = branchId;
        CustomerId = customerId;
        OrderNumber = Trim(orderNumber);
        RevisionNumber = revisionNumber;
        UpdatedAt = now;
    }

    /// <summary>Applies a cancellation. A second cancellation changes nothing.</summary>
    public void Cancel(string reasonCode, DateTimeOffset now)
    {
        if (Status == OrderFactStatus.Cancelled)
        {
            return;
        }

        Status = OrderFactStatus.Cancelled;
        CancelledAt = now;
        CancellationReasonCode = Trim(reasonCode);
        UpdatedAt = now;
    }

    /// <summary>Records a garment job of the order. A job already known is left as it is.</summary>
    public Result AddJob(Guid garmentJobId, string garmentJobNumber, int jobIndex, DateTimeOffset now)
    {
        if (garmentJobId == Guid.Empty)
        {
            return Result.Failure(BillingErrors.Required("garmentJobId"));
        }

        if (_jobs.Any(job => job.GarmentJobId == garmentJobId))
        {
            // Delivered again, or the cancellation arrived first: what is recorded stands.
            return Result.Success();
        }

        _jobs.Add(OrderFactJob.For(OrderId, garmentJobId, Trim(garmentJobNumber), jobIndex));
        UpdatedAt = now;

        return Result.Success();
    }

    /// <summary>
    /// Records that a garment job was cancelled at the counter. A job not yet heard of is recorded as
    /// cancelled from the start, so that its creation arriving later cannot revive it.
    /// </summary>
    /// <param name="garmentJobId">The job.</param>
    /// <param name="garmentJobNumber">Its display number, for a job not yet heard of.</param>
    /// <param name="reasonCode">The configured cancellation reason.</param>
    /// <param name="now">When.</param>
    public void CancelJob(Guid garmentJobId, string garmentJobNumber, string reasonCode, DateTimeOffset now)
    {
        var job = FindJob(garmentJobId);
        if (job is null)
        {
            job = OrderFactJob.For(OrderId, garmentJobId, Trim(garmentJobNumber), 0);
            _jobs.Add(job);
        }

        job.Cancel(Trim(reasonCode), now);
        UpdatedAt = now;
    }

    /// <summary>The job with that identity, or null.</summary>
    public OrderFactJob? FindJob(Guid garmentJobId) => _jobs.Find(job => job.GarmentJobId == garmentJobId);

    private static string Trim(string value)
    {
        var trimmed = (value ?? string.Empty).Trim();

        return trimmed.Length > MaximumNumberLength ? trimmed[..MaximumNumberLength] : trimmed;
    }
}

/// <summary>One garment job of an order, as Orders announced it.</summary>
public sealed class OrderFactJob
{
    private OrderFactJob()
    {
        // The persistence layer materialises instances through this constructor.
    }

    private OrderFactJob(Guid orderId, Guid garmentJobId, string garmentJobNumber, int jobIndex)
    {
        OrderId = orderId;
        GarmentJobId = garmentJobId;
        GarmentJobNumber = garmentJobNumber;
        JobIndex = jobIndex;
    }

    /// <summary>The order.</summary>
    public Guid OrderId { get; private set; }

    /// <summary>The job's identity in Orders, which is the key an invoice line names.</summary>
    public Guid GarmentJobId { get; private set; }

    /// <summary>The display number.</summary>
    public string GarmentJobNumber { get; private set; } = string.Empty;

    /// <summary>The job's position in the order, from one; zero for a job heard of only through its cancellation.</summary>
    public int JobIndex { get; private set; }

    /// <summary>When the job was cancelled, or null while it is live.</summary>
    public DateTimeOffset? CancelledAt { get; private set; }

    /// <summary>The configured reason the job was cancelled under, or null while it is live.</summary>
    public string? CancellationReasonCode { get; private set; }

    /// <summary>True once the job was cancelled: a cancelled job is charged on no invoice.</summary>
    public bool IsCancelled => CancelledAt is not null;

    internal void Cancel(string reasonCode, DateTimeOffset now)
    {
        if (IsCancelled)
        {
            return;
        }

        CancelledAt = now;
        CancellationReasonCode = reasonCode;
    }

    /// <summary>A job row.</summary>
    public static OrderFactJob For(Guid orderId, Guid garmentJobId, string garmentJobNumber, int jobIndex)
        => new(orderId, garmentJobId, garmentJobNumber, jobIndex);
}
