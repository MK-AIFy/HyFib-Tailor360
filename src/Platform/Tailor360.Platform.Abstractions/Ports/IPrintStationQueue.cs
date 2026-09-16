using Tailor360.Platform.Abstractions.Results;

namespace Tailor360.Platform.Abstractions.Ports;

/// <summary>
/// Drains <c>platform.print_jobs</c>: what a print station lists, and how it resolves a job it printed
/// or could not print. <see cref="IPrintQueue"/> is the write side a module enqueues through; this is the
/// read-and-resolve side a station drives.
/// </summary>
/// <remarks>
/// Every method takes the branch or the job as a parameter and never reads an ambient context, so the
/// authorisation decision — which branch a caller may drain — stays in the host that composes a route
/// over this port (E07-F01-5b) rather than leaking into Platform.
/// </remarks>
public interface IPrintStationQueue
{
    /// <summary>Jobs still queued for a branch, oldest first.</summary>
    /// <param name="branchId">The branch to list. Never inferred; a caller filters by the branch it was given.</param>
    /// <param name="limit">How many to return, clamped to <see cref="PrintJobView.MaximumLimit"/>.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<IReadOnlyList<PrintJobView>> ListQueuedAsync(
        Guid branchId,
        int limit = PrintJobView.DefaultLimit,
        CancellationToken cancellationToken = default);

    /// <summary>One job by identity, or null when there is no such job.</summary>
    /// <param name="jobId">The job.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<PrintJobView?> FindAsync(Guid jobId, CancellationToken cancellationToken = default);

    /// <summary>
    /// A station reports it printed a job. The first station to answer wins; a second is told
    /// <see cref="PrintJobErrors.AlreadyResolved"/> and the first outcome, its actor and its station are
    /// left untouched.
    /// </summary>
    /// <param name="jobId">The job.</param>
    /// <param name="by">Who resolved it, or null when the caller is not attributable to a person.</param>
    /// <param name="station">The station's own name.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<Result<PrintJobView>> MarkPrintedAsync(
        Guid jobId,
        Guid? by,
        string station,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// A station reports it could not print a job. Refused before it reaches the database when
    /// <paramref name="reason"/> is empty, and refused the same way <see cref="MarkPrintedAsync"/> is for
    /// a job that is not still queued.
    /// </summary>
    /// <param name="jobId">The job.</param>
    /// <param name="by">Who resolved it, or null when the caller is not attributable to a person.</param>
    /// <param name="station">The station's own name.</param>
    /// <param name="reason">Why it failed. Required: a failure with no reason helps nobody act on it.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<Result<PrintJobView>> MarkFailedAsync(
        Guid jobId,
        Guid? by,
        string station,
        string reason,
        CancellationToken cancellationToken = default);
}

/// <summary>One print job, as a station or an operator sees it.</summary>
/// <param name="Id">Identity of the job.</param>
/// <param name="BranchId">The branch whose station should drain it.</param>
/// <param name="Kind">What is being printed.</param>
/// <param name="Format">The artefact's format.</param>
/// <param name="PayloadReference">The opaque object-storage key naming the artefact. Never disclosed to a caller.</param>
/// <param name="Copies">Number of copies.</param>
/// <param name="PrinterHint">Optional printer name or profile.</param>
/// <param name="Status">One of <c>queued</c>, <c>printed</c> or <c>failed</c>, mirrored here so a caller outside Persistence never references the entity.</param>
/// <param name="RequestedBy">Who asked for the print, or null.</param>
/// <param name="RequestedAt">When it was enqueued.</param>
/// <param name="ResolvedAt">When a station resolved it, or null while queued.</param>
/// <param name="ResolvedBy">Who resolved it, or null.</param>
/// <param name="ResolvedStation">The station that resolved it, or null while queued.</param>
/// <param name="FailureReason">Why it failed, or null unless it did.</param>
public sealed record PrintJobView(
    Guid Id,
    Guid BranchId,
    string Kind,
    string Format,
    string PayloadReference,
    int Copies,
    string? PrinterHint,
    string Status,
    Guid? RequestedBy,
    DateTimeOffset RequestedAt,
    DateTimeOffset? ResolvedAt,
    Guid? ResolvedBy,
    string? ResolvedStation,
    string? FailureReason)
{
    /// <summary>How many <see cref="IPrintStationQueue.ListQueuedAsync"/> returns when the caller names no limit.</summary>
    public const int DefaultLimit = 50;

    /// <summary>The most a single call will return.</summary>
    public const int MaximumLimit = 200;
}

/// <summary>The failures print-station resolution reports.</summary>
public static class PrintJobErrors
{
    /// <summary>
    /// The job is not there to resolve: it does not exist, or it already moved to printed or failed. One
    /// code covers both, the way <c>OutboxErrors.NotDeadLettered</c> does, because to the caller they mean
    /// the same thing — there is nothing left here to resolve.
    /// </summary>
    public static Error AlreadyResolved { get; } = Error.Conflict(
        "print_job.already_resolved",
        "This print job is no longer queued. It has already been printed or marked failed.");

    /// <summary>There is no such print job.</summary>
    public static Error NotFound { get; } = Error.NotFound(
        "print_job.not_found",
        "There is no print job with that identifier.");
}
