namespace Tailor360.Platform.Abstractions.Events;

/// <summary>
/// A print job was enqueued to <c>platform.print_jobs</c>.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Published by Platform, not by a module.</strong> Every other integration event in the solution
/// is declared in a module's own <c>Contracts</c> project, because <c>Contracts</c> is what ARCH-004 lets
/// cross a module boundary. Platform has no such project: <c>docs/architecture/module-ownership.md</c>
/// says Platform "publishes them as ports in <c>Platform.Abstractions</c>", so this event lives beside
/// <see cref="IIntegrationEvent"/> itself, and <c>IntegrationEventTests</c> treats the assembly
/// <c>Tailor360.Platform.Abstractions</c> as the <c>platform</c> module segment's published surface for
/// exactly that reason.
/// </para>
/// <para>
/// <strong>The payload says a job exists, not that it printed.</strong> It is written in the same
/// transaction as the row (<c>DatabasePrintQueue</c>), before any station has seen the job, so it carries
/// only what enqueuing knows: the artefact's reference and format, the branch, and the printer hint a
/// caller supplied. It names no customer, no order and no display number — a subscriber that wants those
/// reads the artefact by identifier through the module that queued it.
/// </para>
/// </remarks>
/// <param name="EventId">Identity of this occurrence.</param>
/// <param name="OccurredAt">When the job was enqueued, in UTC.</param>
/// <param name="AggregateId">The print job's own identity.</param>
/// <param name="BranchId">The branch whose station should drain it.</param>
/// <param name="Kind">What is being printed, for example <c>label.garment_job</c> or <c>document.invoice</c>.</param>
/// <param name="Format">The artefact's format, for example <c>pdf</c>.</param>
/// <param name="PayloadReference">
/// The opaque object-storage key naming the artefact. Carried so a subscriber — the print station screen,
/// and later the bridge of #55 — knows what to stream; never logged and never returned to a caller who
/// asks for anything but the bytes themselves.
/// </param>
/// <param name="PrinterHint">The printer name or profile the caller asked for, or null.</param>
public sealed record PrintJobQueued(
    Guid EventId,
    DateTimeOffset OccurredAt,
    Guid AggregateId,
    Guid BranchId,
    string Kind,
    string Format,
    string PayloadReference,
    string? PrinterHint)
    : IntegrationEvent(EventId, OccurredAt, AggregateId)
{
    /// <summary>The wire name. Subscribe by this constant rather than by a literal.</summary>
    public const string Type = "platform.print-job-queued.v1";

    /// <inheritdoc />
    public override string EventType => Type;
}
