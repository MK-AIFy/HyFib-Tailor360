using Microsoft.Extensions.Logging;
using Tailor360.Platform.Abstractions.Identifiers;
using Tailor360.Platform.Abstractions.Ports;

namespace Tailor360.Modules.Integration.Infrastructure.Printing;

/// <summary>
/// The interim print queue (ADR-0014): the request is acknowledged with an identifier and written to the
/// log by identifiers only, until the print bridge of #55 carries it to a printer. It exists so that the
/// routes that print are built, permissioned and audited now, against the port they will keep.
/// </summary>
/// <param name="ids">The identifier generator.</param>
/// <param name="logger">The log; carries the branch, the kind and the reference — never a document's content.</param>
public sealed partial class LoggingPrintQueue(IIdGenerator ids, ILogger<LoggingPrintQueue> logger) : IPrintQueue
{
    /// <inheritdoc />
    public Task<Guid> EnqueueAsync(PrintJobRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var jobId = ids.NewId();
        LogQueued(logger, jobId, request.BranchId, request.Kind, request.PayloadReference, request.Copies);

        return Task.FromResult(jobId);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Print job {PrintJobId} queued for branch {BranchId}: {Kind} {PayloadReference} x{Copies}; no print bridge is configured (#55), so the job is acknowledged and not printed.")]
    private static partial void LogQueued(ILogger logger, Guid printJobId, Guid branchId, string kind, string payloadReference, int copies);
}
