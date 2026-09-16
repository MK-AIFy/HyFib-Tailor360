namespace Tailor360.Platform.Persistence.Entities;

/// <summary>
/// A job waiting to be printed, or already resolved by a station. The table is <c>platform.print_jobs</c>
/// (<c>docs/architecture/module-ownership.md</c> line 703): Platform owns the queue, and a queueing module
/// — Billing today, Custody once #191 lands — only ever enqueues through <c>IPrintQueue</c> and never maps
/// this table directly (ARCH-005).
/// </summary>
/// <remarks>
/// A queued job that is never drained stays queued for ever and blocks nothing
/// (<c>docs/architecture/invariants.md</c> line 339): printing is deliberately eventually consistent, so
/// nothing here may gate a confirmation, an invoice or a payment.
/// </remarks>
public sealed class PrintJob
{
    /// <summary>Identity of the job, minted by <c>IIdGenerator</c> at enqueue time.</summary>
    public Guid Id { get; set; }

    /// <summary>The branch whose print station should pick the job up.</summary>
    public Guid BranchId { get; set; }

    /// <summary>What is being printed, for example <c>label.garment_job</c> or <c>document.invoice</c>.</summary>
    public string Kind { get; set; } = string.Empty;

    /// <summary>
    /// The artefact's format, for example <c>pdf</c>. A free-text token supplied by the queueing module —
    /// the paper formats a label may use are OD-09's, not this table's.
    /// </summary>
    public string Format { get; set; } = string.Empty;

    /// <summary>
    /// An opaque object-storage key naming the artefact to print. Never dereferenced by this table and
    /// never disclosed to a caller: a station streams it through a server route that re-authorises the
    /// request (CLAUDE.md section 4 item 9).
    /// </summary>
    public string PayloadReference { get; set; } = string.Empty;

    /// <summary>Number of copies; a reprint is a separate, audited job.</summary>
    public int Copies { get; set; } = 1;

    /// <summary>Optional printer name or profile when a branch has more than one.</summary>
    public string? PrinterHint { get; set; }

    /// <summary>One of <see cref="PrintJobStatuses"/>. A resolution is one-way: queued to printed or failed.</summary>
    public string Status { get; set; } = PrintJobStatuses.Queued;

    /// <summary>Who asked for the print, or null when the caller is not attributable to a person.</summary>
    public Guid? RequestedBy { get; set; }

    /// <summary>When the job was enqueued, from <see cref="Tailor360.Platform.Abstractions.Time.IClock"/>.</summary>
    public DateTimeOffset RequestedAt { get; set; }

    /// <summary>When a station resolved the job, printed or failed.</summary>
    public DateTimeOffset? ResolvedAt { get; set; }

    /// <summary>Who resolved the job.</summary>
    public Guid? ResolvedBy { get; set; }

    /// <summary>The free-text name the station calls itself.</summary>
    public string? ResolvedStation { get; set; }

    /// <summary>Why a failed job failed. Never set for any other status.</summary>
    public string? FailureReason { get; set; }

    /// <summary>
    /// Moves a queued job to printed. Refuses — returning <see langword="false"/> rather than throwing —
    /// unless the job is still queued, which is what makes a job's resolution one-way: the caller is
    /// expected to turn a refusal into <c>print_job.already_resolved</c> rather than a raw exception.
    /// </summary>
    /// <param name="by">Who resolved it.</param>
    /// <param name="station">The station's own name.</param>
    /// <param name="at">When it resolved, from <c>IClock</c>.</param>
    public bool TryMarkPrinted(Guid? by, string station, DateTimeOffset at)
    {
        if (Status != PrintJobStatuses.Queued)
        {
            return false;
        }

        Status = PrintJobStatuses.Printed;
        ResolvedAt = at;
        ResolvedBy = by;
        ResolvedStation = station;
        FailureReason = null;

        return true;
    }

    /// <summary>
    /// Moves a queued job to failed. Refuses the same way <see cref="TryMarkPrinted"/> does for any job
    /// that is not still queued.
    /// </summary>
    /// <param name="by">Who resolved it.</param>
    /// <param name="station">The station's own name.</param>
    /// <param name="reason">Why it failed. The caller validates this is non-empty before the database is touched.</param>
    /// <param name="at">When it resolved, from <c>IClock</c>.</param>
    public bool TryMarkFailed(Guid? by, string station, string reason, DateTimeOffset at)
    {
        if (Status != PrintJobStatuses.Queued)
        {
            return false;
        }

        Status = PrintJobStatuses.Failed;
        ResolvedAt = at;
        ResolvedBy = by;
        ResolvedStation = station;
        FailureReason = reason;

        return true;
    }
}

/// <summary>The one-way statuses a print job moves through.</summary>
public static class PrintJobStatuses
{
    /// <summary>Waiting for a station to drain it. The only status a new job may hold.</summary>
    public const string Queued = "queued";

    /// <summary>A station printed it.</summary>
    public const string Printed = "printed";

    /// <summary>A station tried and failed.</summary>
    public const string Failed = "failed";
}
