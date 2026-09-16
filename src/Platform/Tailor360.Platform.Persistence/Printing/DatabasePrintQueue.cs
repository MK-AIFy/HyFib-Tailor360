using Tailor360.Platform.Abstractions.Auditing;
using Tailor360.Platform.Abstractions.Events;
using Tailor360.Platform.Abstractions.Identifiers;
using Tailor360.Platform.Abstractions.Ports;
using Tailor360.Platform.Abstractions.Time;
using Tailor360.Platform.Persistence.Contexts;
using Tailor360.Platform.Persistence.Entities;
using Tailor360.Platform.Persistence.Outbox;

namespace Tailor360.Platform.Persistence.Printing;

/// <summary>
/// The durable print queue (ADR-0014). Enqueuing writes a row to <c>platform.print_jobs</c> and publishes
/// <see cref="PrintJobQueued"/> in the same transaction, replacing the interim <c>LoggingPrintQueue</c>,
/// which minted an identifier, logged it and printed — and persisted — nothing.
/// </summary>
/// <remarks>
/// <c>context</c> is Platform's own, not the caller's: a module enqueuing a job (Billing today) holds its
/// own module context, and this write commits on a separate connection and transaction from whatever the
/// caller does next. That is the trade-off the "Platform port" mechanism accepts by design
/// (<c>src/Modules/CLAUDE.md</c> section 2) — printing is deliberately eventually consistent
/// (<c>docs/architecture/invariants.md</c> line 339), so nothing about a caller's own commit may depend on
/// this one, and nothing here may gate it either.
/// </remarks>
/// <param name="context">The platform context.</param>
/// <param name="publisher">The platform's own event publisher, over its own outbox.</param>
/// <param name="clock">The clock.</param>
/// <param name="ids">The identifier generator.</param>
/// <param name="auditContext">Who is enqueuing, when the caller resolves to a person.</param>
public sealed class DatabasePrintQueue(
    PlatformDbContext context,
    ModuleEventPublisher<PlatformDbContext> publisher,
    IClock clock,
    IIdGenerator ids,
    IAuditContext auditContext)
    : IPrintQueue
{
    /// <inheritdoc />
    public async Task<Guid> EnqueueAsync(PrintJobRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var jobId = ids.NewId();
        var requestedAt = clock.UtcNow;

        context.PrintJobs.Add(new PrintJob
        {
            Id = jobId,
            BranchId = request.BranchId,
            Kind = request.Kind,
            Format = request.Format,
            PayloadReference = request.PayloadReference,
            Copies = request.Copies,
            PrinterHint = request.PrinterHint,
            Status = PrintJobStatuses.Queued,
            RequestedBy = auditContext.ActorId,
            RequestedAt = requestedAt,
        });

        publisher.Publish(new PrintJobQueued(
            ids.NewId(),
            requestedAt,
            jobId,
            request.BranchId,
            request.Kind,
            request.Format,
            request.PayloadReference,
            request.PrinterHint));

        await context.SaveChangesAsync(cancellationToken);

        return jobId;
    }
}
