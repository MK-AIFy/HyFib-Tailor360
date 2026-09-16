using Microsoft.EntityFrameworkCore;
using Tailor360.Platform.Abstractions.Auditing;
using Tailor360.Platform.Abstractions.Ports;
using Tailor360.Platform.Abstractions.Results;
using Tailor360.Platform.Abstractions.Time;
using Tailor360.Platform.Persistence.Contexts;
using Tailor360.Platform.Persistence.Entities;

namespace Tailor360.Platform.Persistence.Printing;

/// <summary>
/// The read-and-resolve side of the print queue: what a station lists, and how it reports a job printed
/// or failed. <see cref="DatabasePrintQueue"/> is the write side a module enqueues through.
/// </summary>
/// <remarks>
/// <para>
/// <strong>The two resolutions are a conditional update on the job still being queued.</strong>
/// <see cref="PrintJob.TryMarkPrinted"/> and <see cref="PrintJob.TryMarkFailed"/> refuse in memory before
/// anything is written when the job has already moved; the genuinely concurrent case — two stations
/// loading the same queued row before either has saved — is caught by the <c>xmin</c> token
/// <c>UseRowVersion</c> maps onto <c>print_jobs</c> (<c>PlatformDbContext</c>), which turns the loser's
/// <c>SaveChangesAsync</c> into a <see cref="DbUpdateConcurrencyException"/> rather than a silent
/// overwrite. Both paths answer the caller the same way: <see cref="PrintJobErrors.AlreadyResolved"/>.
/// </para>
/// <para>
/// <strong>The audit entry rides the same save as the row.</strong> <c>IAuditWriter</c> is already bound
/// to <see cref="PlatformDbContext"/> (the module guide's default binding), so staging the entry before
/// <c>SaveChangesAsync</c> and calling that once is what makes the resolution and its trail entry commit
/// or roll back together — there is no second, separate <c>audit.SaveAsync()</c> here, unlike
/// <c>OutboxAdministration</c>, whose message lives in a different module's schema and genuinely needs
/// the two-context split.
/// </para>
/// </remarks>
/// <param name="context">The platform context.</param>
/// <param name="audit">The platform's default audit writer, bound to this same context.</param>
/// <param name="clock">The clock.</param>
public sealed class PrintStationQueue(
    PlatformDbContext context,
    IAuditWriter audit,
    IClock clock)
    : IPrintStationQueue
{
    /// <summary>The audit action a printed resolution is recorded under.</summary>
    public const string PrintedAction = "platform.print_job.printed";

    /// <summary>The audit action a failed resolution is recorded under.</summary>
    public const string FailedAction = "platform.print_job.failed";

    /// <inheritdoc />
    public async Task<IReadOnlyList<PrintJobView>> ListQueuedAsync(
        Guid branchId,
        int limit = PrintJobView.DefaultLimit,
        CancellationToken cancellationToken = default)
    {
        var wanted = Math.Clamp(limit, 1, PrintJobView.MaximumLimit);

        var rows = await context.PrintJobs
            .AsNoTracking()
            .Where(job => job.BranchId == branchId && job.Status == PrintJobStatuses.Queued)
            .OrderBy(job => job.RequestedAt)
            .ThenBy(job => job.Id)
            .Take(wanted)
            .ToListAsync(cancellationToken);

        return [.. rows.Select(Describe)];
    }

    /// <inheritdoc />
    public async Task<PrintJobView?> FindAsync(Guid jobId, CancellationToken cancellationToken = default)
    {
        var row = await context.PrintJobs.AsNoTracking()
            .FirstOrDefaultAsync(job => job.Id == jobId, cancellationToken);

        return row is null ? null : Describe(row);
    }

    /// <inheritdoc />
    public Task<Result<PrintJobView>> MarkPrintedAsync(
        Guid jobId,
        Guid? by,
        string station,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(station);

        return ResolveAsync(
            jobId,
            job => job.TryMarkPrinted(by, station, clock.UtcNow),
            PrintedAction,
            $"Printed at station '{station}'.",
            by,
            cancellationToken);
    }

    /// <inheritdoc />
    public Task<Result<PrintJobView>> MarkFailedAsync(
        Guid jobId,
        Guid? by,
        string station,
        string reason,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(station);
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);

        return ResolveAsync(
            jobId,
            job => job.TryMarkFailed(by, station, reason, clock.UtcNow),
            FailedAction,
            $"Failed at station '{station}'.",
            by,
            cancellationToken);
    }

    private async Task<Result<PrintJobView>> ResolveAsync(
        Guid jobId,
        Func<PrintJob, bool> resolve,
        string auditAction,
        string summary,
        Guid? actor,
        CancellationToken cancellationToken)
    {
        var job = await context.PrintJobs.FirstOrDefaultAsync(row => row.Id == jobId, cancellationToken);

        if (job is null)
        {
            return Result.Failure<PrintJobView>(PrintJobErrors.NotFound);
        }

        if (!resolve(job))
        {
            return Result.Failure<PrintJobView>(PrintJobErrors.AlreadyResolved);
        }

        await audit.WriteAsync(
            new AuditEntry(auditAction, nameof(PrintJob), job.Id, summary, ActorId: actor),
            cancellationToken);

        try
        {
            await context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            // Another station answered first, between our load and our save. The row it wrote is the
            // truth; we have nothing further to tell it.
            return Result.Failure<PrintJobView>(PrintJobErrors.AlreadyResolved);
        }

        return Result.Success(Describe(job));
    }

    private static PrintJobView Describe(PrintJob job) => new(
        job.Id,
        job.BranchId,
        job.Kind,
        job.Format,
        job.PayloadReference,
        job.Copies,
        job.PrinterHint,
        job.Status,
        job.RequestedBy,
        job.RequestedAt,
        job.ResolvedAt,
        job.ResolvedBy,
        job.ResolvedStation,
        job.FailureReason);
}
