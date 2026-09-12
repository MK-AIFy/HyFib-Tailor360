using System.Data;
using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Tailor360.Modules.Orders.Contracts.Orders;
using Tailor360.Modules.Orders.Domain.DisplayNumbers;
using Tailor360.Modules.Orders.Domain.Jobs;
using Tailor360.Modules.Orders.Domain.Orders;
using Tailor360.Modules.Orders.Domain.Snapshots;
using Tailor360.Modules.Orders.Infrastructure.Persistence;

namespace Tailor360.Modules.Orders.Infrastructure.Orders;

/// <summary>
/// The published read of an order and its garment jobs (<c>IOrderSnapshotQuery</c>). The only way
/// <c>orders.*</c> leaves the module.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Every read is projected and none is loaded.</strong> A consumer that wants a job number and a due date
/// to put on a delivery queue must not pull a whole order with a measurement document per garment, and the
/// contract's answer is a record rather than an aggregate — so the query stops at the columns the record names.
/// <c>AsNoTracking</c> throughout: none of this precedes a write, and tracking it would fill the change tracker
/// for a request that saves nothing.
/// </para>
/// <para>
/// <strong>The money is read by a query of its own, not projected and then dropped.</strong> The contract is
/// explicit that a dispatch scan and a workload count read a garment's state "without ever receiving" a price,
/// and the price snapshot is Confidential (<c>docs/nfr/data-classification.md</c> section 5.7). Reading the
/// amounts on every state read and discarding them would put them on the wire between PostgreSQL and the host
/// for callers that are not entitled to them, which is a weaker position than not reading them at all.
/// </para>
/// <para>
/// <strong>No branch filter and no organisation filter.</strong> Which callers may read an order is the
/// endpoint's decision and the authorisation pipeline's, and the pipeline is where the audit entry is written;
/// a query that quietly answered null for another branch's order would make "not yours" and "not there"
/// indistinguishable to the caller and invisible to the trail. <c>OrderSnapshot.BranchId</c> is returned so the
/// caller can evaluate its own reach, which is what the contract's remarks ask for. It is the position
/// <c>MeasurementSnapshotQuery</c> takes for the same reason.
/// </para>
/// <para>
/// <strong>The published enumerations are converted member by member.</strong> <c>OrderState</c>,
/// <c>GarmentJobState</c>, <c>ReadyBlockReason</c> and <c>JobDependencyRelation</c> mirror the domain's
/// enumerations one for one today, and a cast would work today — and would silently couple a published set to an
/// internal one, so that adding a member on one side changed what the other means. A switch fails to compile
/// instead.
/// </para>
/// </remarks>
/// <param name="context">The module's context.</param>
public sealed class OrderSnapshotQuery(OrdersDbContext context) : IOrderSnapshotQuery
{
    /// <summary>
    /// The columns a garment job's published state is built from.
    /// </summary>
    /// <remarks>
    /// Declared once and used by all three job reads, so that the three cannot disagree about what a garment job
    /// is. It carries no amount, no measurement, no design selection, no free text and no reference media
    /// identifier — the contract's remarks say why of each.
    /// </remarks>
    private static readonly Expression<Func<GarmentJob, JobRow>> JobProjection = job => new JobRow(
        job.Id,
        job.OrderId,
        job.OrganisationId,
        job.BranchId,
        job.JobNumber,
        job.JobIndex,
        job.CategoryKey,
        job.ServiceTypeKey,
        job.Design.CategoryLabel,
        job.Design.ServiceTypeLabel,
        job.WorkflowDefinitionId,
        job.WorkflowVersionId,
        job.DueDate,
        job.Status,
        job.ConfirmedAt,
        job.ProductionStartedAt,
        job.DeliveredAt,
        job.CancelledAt,
        job.HoldReasonCode,
        job.HeldAt,
        job.CancellationReasonCode,
        job.IsReadyForDelivery,
        job.ReadyStateComputedAt,
        job.ReadyStateBlocks,
        job.Dependencies
            .Select(dependency => new DependencyRow(dependency.PrerequisiteGarmentJobId, dependency.Kind))
            .ToList());

    /// <inheritdoc />
    public async Task<OrderSnapshot?> GetAsync(Guid orderId, CancellationToken cancellationToken = default)
    {
        var order = await context.Orders
            .AsNoTracking()
            .Where(one => one.Id == orderId)
            .Select(one => new OrderRow(
                one.Id,
                one.OrderNumber,
                one.OrganisationId,
                one.BranchId,
                one.CustomerId,
                one.OrderDraftId,
                one.EstimateId,
                one.Status,
                one.RevisionNumber,
                one.DueDate,
                one.ConfirmedAt,
                one.ProductionStartedAt,
                one.DeliveredAt,
                one.CancelledAt,
                one.CancellationReasonCode))
            .FirstOrDefaultAsync(cancellationToken);

        return order is null ? null : Map(order, await JobsOfAsync(orderId, cancellationToken));
    }

    /// <inheritdoc />
    public async Task<GarmentJobSnapshot?> GetJobAsync(
        Guid garmentJobId,
        CancellationToken cancellationToken = default)
    {
        var job = await context.GarmentJobs
            .AsNoTracking()
            .Where(one => one.Id == garmentJobId)
            .Select(JobProjection)
            .FirstOrDefaultAsync(cancellationToken);

        return job is null ? null : Map(job);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<GarmentJobSnapshot>> GetJobsAsync(
        IReadOnlyCollection<Guid> garmentJobIds,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(garmentJobIds);

        // Answered before the database is touched. A parcel check at the door with nothing in it is a round trip
        // that can only return nothing — the shape MeasurementSnapshotQuery.ExistingAsync uses.
        if (garmentJobIds.Count == 0)
        {
            return [];
        }

        var jobs = await context.GarmentJobs
            .AsNoTracking()
            .Where(one => garmentJobIds.Contains(one.Id))
            .OrderBy(one => one.JobIndex)
            .Select(JobProjection)
            .ToListAsync(cancellationToken);

        return [.. jobs.Select(Map)];
    }

    /// <inheritdoc />
    /// <remarks>
    /// <para>
    /// <strong>Every statement below reads one snapshot, which is what makes this one read rather than three
    /// that can disagree.</strong> The answer is composed from three statements — <see cref="GetAsync"/> is
    /// two of them — and PostgreSQL's default <c>READ COMMITTED</c> takes a fresh snapshot at the start of
    /// <em>each</em> statement, grouped into a transaction or not. A revision committing while this ran would
    /// therefore hand Billing revision <em>n</em>'s state and revision number beside revision <em>n+1</em>'s
    /// amounts, or an order total from one revision beside garment totals from another. <c>Order.Revise</c>
    /// rewrites <c>orders.totals_*</c> and every <c>garment_jobs.price_*</c> in one transaction, so the rows
    /// never disagree on disk; only a reader taking three snapshots can see them do so, and an invoice raised
    /// from a mismatched pair is an invoice for the wrong amount. This is the cross-module money read —
    /// <c>docs/architecture/module-ownership.md</c> section 5.8 has Billing converting an order into an invoice
    /// through it — and the contract promises the state arrives with the money "so that eligibility and
    /// money are judged from one read of one aggregate rather than from two reads that can disagree".
    /// </para>
    /// <para>
    /// <strong>The isolation level is the fix; the transaction is only how it is asked for.</strong>
    /// <c>REPEATABLE READ</c> fixes the snapshot at the first statement of the transaction and shows every later
    /// statement in it exactly that one. Nothing here writes, so it takes no row lock and cannot lose a
    /// serialisation race — PostgreSQL raises <c>40001</c> at this level only against a write — and the
    /// whole cost is one <c>BEGIN</c> and one <c>COMMIT</c> around statements that were being run anyway.
    /// </para>
    /// <para>
    /// <strong>A single statement was the alternative, and what it would cost the state reads is what ruled it
    /// out.</strong> <c>PriceSnapshot</c> is a complex property, so <c>orders.totals_*</c> already sits in the
    /// order's own row and <c>garment_jobs.price_*</c> in each garment's, and one statement per table could
    /// carry state and money together. But <see cref="GetAsync"/> and <see cref="JobProjection"/> are shared
    /// with the reads that must never receive an amount — Custody's dispatch scan, Reporting's
    /// reconciliation — so folding the money in would either put a Confidential field on the wire for them
    /// (<c>docs/nfr/data-classification.md</c> section 5.7) or need a second copy of both projections kept in
    /// step by hand, which is the disagreement <see cref="JobProjection"/> exists to prevent. Collapsing all
    /// three statements into one costs more again: it is the order joined to its garments joined to their
    /// dependencies, which is the Cartesian product <see cref="JobsOfAsync"/> gives its own reasons for
    /// refusing.
    /// </para>
    /// <para>
    /// <strong><see cref="GetAsync"/> is left exactly as it was</strong>, which is the constraint that shaped
    /// the choice. The callers that read state alone are the majority and must not begin paying for a
    /// transaction only Billing needs, so the level is asked for here, on the one method that answers with
    /// money.
    /// </para>
    /// <para>
    /// <strong>An ambient transaction is joined rather than nested.</strong> The context is scoped and shared,
    /// and a confirmation-participant hook (<c>src/Modules/CLAUDE.md</c> section 2) reads through this contract
    /// from inside <c>OrderStore.InConfirmationTransactionAsync</c>'s transaction. Opening a second transaction
    /// throws, and so does starting an execution strategy while one is open; the caller's transaction is the
    /// snapshot those reads belong to in any case.
    /// </para>
    /// </remarks>
    public async Task<PricedOrderSnapshot?> GetPricedAsync(
        Guid orderId,
        IReadOnlyCollection<string> callerPermissions,
        CancellationToken cancellationToken = default)
    {
        // callerPermissions is validated here and read by nothing after it, exactly as the contract's own
        // remarks instruct: no key in the Orders catalogue separates reading an order from reading its money,
        // and coining one would decide OD-13. It is taken now so that the mask, when the decision lands, changes
        // behaviour inside this module and nowhere else.
        ArgumentNullException.ThrowIfNull(callerPermissions);

        if (context.Database.CurrentTransaction is not null)
        {
            return await PricedAsync(orderId, cancellationToken);
        }

        // The connection retries on a transient failure, and a transaction opened outside the strategy would be
        // rejected by EF for exactly that reason — the constraint OrderStore records at its own transaction.
        var strategy = context.Database.CreateExecutionStrategy();

        return await strategy.ExecuteAsync(async () =>
        {
            await using var transaction = await context.Database.BeginTransactionAsync(
                IsolationLevel.RepeatableRead, cancellationToken);

            var priced = await PricedAsync(orderId, cancellationToken);

            // Committed rather than rolled back although nothing was written. The two are the same thing to
            // PostgreSQL here, and a rollback in the log of a read that succeeded reads as though it had not.
            await transaction.CommitAsync(cancellationToken);

            return priced;
        });
    }

    /// <summary>Where an order stands, as the published set names it.</summary>
    /// <param name="status">The module's own status.</param>
    /// <returns>The published state.</returns>
    /// <exception cref="ArgumentOutOfRangeException">A domain status no published member mirrors.</exception>
    private static OrderState StateOf(OrderStatus status) => status switch
    {
        OrderStatus.Draft => OrderState.Draft,
        OrderStatus.Confirmed => OrderState.Confirmed,
        OrderStatus.InProduction => OrderState.InProduction,
        OrderStatus.Ready => OrderState.Ready,
        OrderStatus.Delivered => OrderState.Delivered,
        OrderStatus.Closed => OrderState.Closed,
        OrderStatus.Cancelled => OrderState.Cancelled,
        _ => throw new ArgumentOutOfRangeException(nameof(status), status, "No published order state mirrors it."),
    };

    /// <summary>Where a garment job stands, as the published set names it.</summary>
    /// <param name="status">The module's own status.</param>
    /// <returns>The published state.</returns>
    /// <exception cref="ArgumentOutOfRangeException">A domain status no published member mirrors.</exception>
    private static GarmentJobState StateOf(GarmentJobStatus status) => status switch
    {
        GarmentJobStatus.Confirmed => GarmentJobState.Confirmed,
        GarmentJobStatus.InProduction => GarmentJobState.InProduction,
        GarmentJobStatus.OnHold => GarmentJobState.OnHold,
        GarmentJobStatus.Ready => GarmentJobState.Ready,
        GarmentJobStatus.Delivered => GarmentJobState.Delivered,
        GarmentJobStatus.Closed => GarmentJobState.Closed,
        GarmentJobStatus.Cancelled => GarmentJobState.Cancelled,
        _ => throw new ArgumentOutOfRangeException(nameof(status), status, "No published job state mirrors it."),
    };

    /// <summary>Which binding one garment declared on another, as the published set names it.</summary>
    /// <param name="kind">The module's own kind.</param>
    /// <returns>The published relation.</returns>
    /// <exception cref="ArgumentOutOfRangeException">A domain kind no published member mirrors.</exception>
    private static JobDependencyRelation RelationOf(JobDependencyKind kind) => kind switch
    {
        JobDependencyKind.FinishBefore => JobDependencyRelation.FinishBefore,
        JobDependencyKind.DeliverTogether => JobDependencyRelation.DeliverTogether,
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "No published relation mirrors it."),
    };

    /// <summary>Which ready-gate predicate blocked, as the published set names it.</summary>
    /// <param name="predicate">The module's own predicate.</param>
    /// <returns>The published reason.</returns>
    /// <exception cref="ArgumentOutOfRangeException">A domain predicate no published member mirrors.</exception>
    private static ReadyBlockReason ReasonOf(ReadyGatePredicate predicate) => predicate switch
    {
        ReadyGatePredicate.WorkflowComplete => ReadyBlockReason.WorkflowComplete,
        ReadyGatePredicate.QcPassed => ReadyBlockReason.QcPassed,
        ReadyGatePredicate.DocumentationComplete => ReadyBlockReason.DocumentationComplete,
        ReadyGatePredicate.NoOpenHold => ReadyBlockReason.NoOpenHold,
        ReadyGatePredicate.DependenciesMet => ReadyBlockReason.DependenciesMet,
        ReadyGatePredicate.CustodyReconciled => ReadyBlockReason.CustodyReconciled,
        _ => throw new ArgumentOutOfRangeException(
            nameof(predicate), predicate, "No published ready-block reason mirrors it."),
    };

    /// <summary>
    /// One ready-gate block as the contract carries it — predicate and reference both, unchanged.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <strong>Nothing is dropped here any more, because nothing that cannot be published is stored.</strong>
    /// This used to re-state <c>ReadyStateBlock.Of</c>'s four validation rules by hand and fold any reference
    /// that failed them to null, which was the least bad of three answers while the domain's
    /// <c>ReadyGateBlock</c> accepted two hundred characters of anything: <c>Of</c> refuses by
    /// <em>throwing</em>, so a legitimately stored ready state could otherwise have made
    /// <see cref="GetJobAsync"/> throw at a module boundary. It bought that with a rule duplicated in two
    /// assemblies and a silent loss across a boundary — a Tailor Master told which of six things blocked the
    /// garment and not which phase.
    /// </para>
    /// <para>
    /// The third answer was taken instead, and it is a Domain change: <c>ReadyGateBlock.IsCarriable</c> is the
    /// one statement of the rule and it is applied where a reference <em>enters</em> the module — on
    /// <c>ReadyGateInputs</c>, where the application hands over the facts it gathered, and on
    /// <c>GarmentJob</c>'s reason codes. A reference the contract cannot carry is now refused at the point it
    /// was supplied, with <c>orders.reference-not-carriable</c> naming the field, rather than accepted and then
    /// lost where nobody can see it. The contract's own length moved to meet the module in one place — forty was
    /// shorter than a garment job number, which <c>DependenciesMet</c> names — and the two constants are held
    /// together by a unit test rather than by this comment.
    /// </para>
    /// </remarks>
    /// <param name="block">The module's own block.</param>
    /// <returns>The published block.</returns>
    private static ReadyStateBlock PublishedBlock(ReadyGateBlock block)
        => ReadyStateBlock.Of(ReasonOf(block.Predicate), block.Reference);

    /// <summary>One frozen price snapshot as the contract carries it.</summary>
    /// <param name="price">The module's own snapshot.</param>
    /// <returns>The published totals, with the three configuration versions INV-ORD-02 requires beside them.</returns>
    private static PricedTotals TotalsOf(PriceSnapshot price) => new(
        price.CatalogVersionId,
        price.PriceListVersionId,
        price.TaxConfigurationVersionId,
        price.Subtotal,
        price.DiscountTotal,
        price.TaxableValue,
        price.CentralTax,
        price.StateTax,
        price.IntegratedTax,
        price.Cess,
        price.RoundOff,
        price.GrandTotal,
        price.CalculatedAt);

    /// <summary>Builds the published order from the rows read for it.</summary>
    /// <param name="order">The order's own columns.</param>
    /// <param name="jobs">Its garment jobs, already in job-index order.</param>
    /// <returns>The snapshot.</returns>
    private static OrderSnapshot Map(OrderRow order, IReadOnlyList<JobRow> jobs) => new(
        order.OrderId,
        order.OrderNumber.Value,
        order.OrganisationId,
        order.BranchId,
        order.CustomerId,
        order.OrderDraftId,
        order.EstimateId,
        StateOf(order.Status),
        order.RevisionNumber,
        order.DueDate,
        order.ConfirmedAt,
        order.ProductionStartedAt,
        order.DeliveredAt,
        order.CancelledAt,
        order.CancellationReasonCode,
        [.. jobs.Select(Map)]);

    /// <summary>Builds one published garment job from the row read for it.</summary>
    /// <param name="job">The garment job's own columns.</param>
    /// <returns>The snapshot.</returns>
    private static GarmentJobSnapshot Map(JobRow job) => new(
        job.GarmentJobId,
        job.OrderId,
        job.OrganisationId,
        job.BranchId,
        job.JobNumber.Value,
        job.JobIndex,
        job.CategoryKey,
        job.ServiceTypeKey,
        job.CategoryLabel,
        job.ServiceTypeLabel,
        job.WorkflowDefinitionId,
        job.WorkflowVersionId,
        job.DueDate,
        StateOf(job.Status),
        job.ConfirmedAt,
        job.ProductionStartedAt,
        job.DeliveredAt,
        job.CancelledAt,
        job.HoldReasonCode,
        job.HeldAt,
        job.CancellationReasonCode,
        job.IsReadyForDelivery,
        job.ReadyStateComputedAt,
        [.. job.ReadyStateBlocks.Select(PublishedBlock)],
        [.. job.Dependencies.Select(
            dependency => new GarmentJobDependency(
                dependency.PrerequisiteGarmentJobId, RelationOf(dependency.Kind)))]);

    /// <summary>Every garment job of one order, in job-index order.</summary>
    /// <remarks>
    /// A second statement rather than a collection projected inside the order's own, because the jobs carry two
    /// owned tables and a nested dependency collection each and the composed query is what a reader has to
    /// understand when it is slow. The two run on one connection inside one request and a garment job never
    /// changes the order it belongs to, so the pair cannot describe a set that never existed — only a garment
    /// whose state moved between the two reads, which is the same staleness any read has the moment it returns
    /// and which CI-03 already makes the caller check against
    /// <c>GarmentJobSnapshot.ReadyStateComputedAt</c> rather than against the read's own instant.
    /// </remarks>
    /// <param name="orderId">The order.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The rows, which are none when the order has no garment jobs.</returns>
    private async Task<IReadOnlyList<JobRow>> JobsOfAsync(Guid orderId, CancellationToken cancellationToken)
        => await context.GarmentJobs
            .AsNoTracking()
            .Where(job => job.OrderId == orderId)
            .OrderBy(job => job.JobIndex)
            .Select(JobProjection)
            .ToListAsync(cancellationToken);

    /// <summary>Composes the priced answer, on the snapshot its caller has already fixed.</summary>
    /// <remarks>
    /// Separate from <see cref="GetPricedAsync"/> so that the transaction is opened in exactly one place and
    /// these three statements cannot be run outside it by a later edit that only meant to add a column.
    /// </remarks>
    /// <param name="orderId">The order.</param>
    /// <param name="cancellationToken">Cancels the reads.</param>
    /// <returns>The priced snapshot, or null when no order has that identity.</returns>
    private async Task<PricedOrderSnapshot?> PricedAsync(Guid orderId, CancellationToken cancellationToken)
    {
        var order = await GetAsync(orderId, cancellationToken);

        if (order is null)
        {
            return null;
        }

        var totals = await context.Orders
            .AsNoTracking()
            .Where(one => one.Id == orderId)
            .Select(one => one.Totals)
            .FirstOrDefaultAsync(cancellationToken);

        var jobTotals = await context.GarmentJobs
            .AsNoTracking()
            .Where(one => one.OrderId == orderId)
            .OrderBy(one => one.JobIndex)
            .Select(one => new JobPriceRow(one.Id, one.Price))
            .ToListAsync(cancellationToken);

        // TotalsIncluded is true on every answer, for the reason GetPricedAsync's argument check records.
        return new PricedOrderSnapshot(
            order,
            TotalsIncluded: true,
            totals is null ? null : TotalsOf(totals),
            [.. jobTotals.Select(row => new GarmentJobPricedTotals(row.GarmentJobId, TotalsOf(row.Price)))]);
    }

    /// <summary>The columns one order's published state is built from. Carries no amount.</summary>
    private sealed record OrderRow(
        Guid OrderId,
        OrderNumber OrderNumber,
        Guid OrganisationId,
        Guid BranchId,
        Guid CustomerId,
        Guid OrderDraftId,
        Guid? EstimateId,
        OrderStatus Status,
        int RevisionNumber,
        DateOnly DueDate,
        DateTimeOffset ConfirmedAt,
        DateTimeOffset? ProductionStartedAt,
        DateTimeOffset? DeliveredAt,
        DateTimeOffset? CancelledAt,
        string? CancellationReasonCode);

    /// <summary>The columns one garment job's published state is built from. Carries no amount.</summary>
    private sealed record JobRow(
        Guid GarmentJobId,
        Guid OrderId,
        Guid OrganisationId,
        Guid BranchId,
        GarmentJobNumber JobNumber,
        int JobIndex,
        string CategoryKey,
        string ServiceTypeKey,
        string CategoryLabel,
        string ServiceTypeLabel,
        Guid WorkflowDefinitionId,
        Guid? WorkflowVersionId,
        DateOnly DueDate,
        GarmentJobStatus Status,
        DateTimeOffset ConfirmedAt,
        DateTimeOffset? ProductionStartedAt,
        DateTimeOffset? DeliveredAt,
        DateTimeOffset? CancelledAt,
        string? HoldReasonCode,
        DateTimeOffset? HeldAt,
        string? CancellationReasonCode,
        bool IsReadyForDelivery,
        DateTimeOffset? ReadyStateComputedAt,
        IReadOnlyCollection<ReadyGateBlock> ReadyStateBlocks,
        IReadOnlyList<DependencyRow> Dependencies);

    /// <summary>One declared binding, as the projection reads it before the published relation is chosen.</summary>
    private sealed record DependencyRow(Guid PrerequisiteGarmentJobId, JobDependencyKind Kind);

    /// <summary>One garment job's frozen price, read only by the one method that answers with money.</summary>
    private sealed record JobPriceRow(Guid GarmentJobId, PriceSnapshot Price);
}
