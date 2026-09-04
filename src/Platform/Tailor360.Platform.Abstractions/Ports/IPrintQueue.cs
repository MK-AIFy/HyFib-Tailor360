namespace Tailor360.Platform.Abstractions.Ports;

/// <summary>
/// Queues a document or label for printing. Printing is asynchronous and durable: the queue survives a
/// print station being offline, and a job is only marked done when the station confirms it. Platform
/// owns the queue and its table; Custody queues labels and Billing queues documents.
/// </summary>
public interface IPrintQueue
{
    /// <summary>Queues a job and returns its identity.</summary>
    Task<Guid> EnqueueAsync(PrintJobRequest request, CancellationToken cancellationToken = default);
}

/// <summary>A request to print something.</summary>
/// <param name="BranchId">The branch whose print station should pick the job up.</param>
/// <param name="Kind">What is being printed, for example <c>label.garment_job</c> or <c>document.invoice</c>.</param>
/// <param name="PayloadReference">Reference to the artefact to print, resolved by the print station.</param>
/// <param name="Copies">Number of copies; a reprint is a separate, audited job.</param>
/// <param name="PrinterHint">Optional printer name or profile when a branch has more than one.</param>
public sealed record PrintJobRequest(
    Guid BranchId,
    string Kind,
    string PayloadReference,
    int Copies = 1,
    string? PrinterHint = null);
