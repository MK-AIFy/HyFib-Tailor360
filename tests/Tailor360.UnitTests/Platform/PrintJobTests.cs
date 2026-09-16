using System.Globalization;
using Shouldly;
using Tailor360.Platform.Persistence.Entities;

namespace Tailor360.UnitTests.Platform;

/// <summary>
/// The print job's status machine: <c>queued</c> moves to <c>printed</c> or <c>failed</c> exactly once,
/// and every other transition is refused. No database is needed for this — <c>PrintJob</c> is a plain
/// entity, and the two mutators are pure functions of its current status.
/// </summary>
[Trait("Category", "Unit")]
public sealed class PrintJobTests
{
    private static readonly Guid Station = Guid.Parse("019bd000-0000-7000-8000-000000000001");
    private static readonly DateTimeOffset At = DateTimeOffset.Parse("2026-09-16T10:00:00Z", CultureInfo.InvariantCulture);

    [Fact]
    public void AQueuedJobMovesToPrinted()
    {
        var job = Queued();

        job.TryMarkPrinted(Station, "counter-1", At).ShouldBeTrue();

        job.Status.ShouldBe(PrintJobStatuses.Printed);
        job.ResolvedAt.ShouldBe(At);
        job.ResolvedBy.ShouldBe(Station);
        job.ResolvedStation.ShouldBe("counter-1");
        job.FailureReason.ShouldBeNull();
    }

    [Fact]
    public void AQueuedJobMovesToFailed()
    {
        var job = Queued();

        job.TryMarkFailed(Station, "counter-1", "Out of paper.", At).ShouldBeTrue();

        job.Status.ShouldBe(PrintJobStatuses.Failed);
        job.ResolvedAt.ShouldBe(At);
        job.ResolvedBy.ShouldBe(Station);
        job.ResolvedStation.ShouldBe("counter-1");
        job.FailureReason.ShouldBe("Out of paper.");
    }

    [Fact]
    public void AResolutionAttributesToNoOneWhenTheCallerIsNotAPerson()
    {
        var job = Queued();

        job.TryMarkPrinted(by: null, "kiosk-1", At).ShouldBeTrue();

        job.ResolvedBy.ShouldBeNull();
        job.ResolvedStation.ShouldBe("kiosk-1");
    }

    [Theory]
    [InlineData(PrintJobStatuses.Printed)]
    [InlineData(PrintJobStatuses.Failed)]
    public void APrintedOrFailedJobRefusesAFurtherPrintedResolution(string alreadyResolved)
    {
        var job = Resolved(alreadyResolved);

        job.TryMarkPrinted(Station, "counter-2", At.AddMinutes(1)).ShouldBeFalse();

        // The refusal leaves the first outcome, its actor and its station untouched.
        job.Status.ShouldBe(alreadyResolved);
        job.ResolvedStation.ShouldBe("counter-1");
        job.ResolvedAt.ShouldBe(At);
    }

    [Theory]
    [InlineData(PrintJobStatuses.Printed)]
    [InlineData(PrintJobStatuses.Failed)]
    public void APrintedOrFailedJobRefusesAFurtherFailedResolution(string alreadyResolved)
    {
        var job = Resolved(alreadyResolved);
        var reasonBeforeTheSecondAttempt = job.FailureReason;

        job.TryMarkFailed(Station, "counter-2", "Jammed.", At.AddMinutes(1)).ShouldBeFalse();

        job.Status.ShouldBe(alreadyResolved);
        job.ResolvedStation.ShouldBe("counter-1");
        job.FailureReason.ShouldBe(reasonBeforeTheSecondAttempt, "a refused resolution changes nothing");
    }

    [Fact]
    public void ANewJobIsAlwaysQueued()
        => new PrintJob().Status.ShouldBe(PrintJobStatuses.Queued);

    private static PrintJob Queued() => new()
    {
        Id = Guid.Parse("019bd000-0000-7000-8000-0000000000aa"),
        BranchId = Guid.Parse("019bd000-0000-7000-8000-0000000000bb"),
        Kind = "document.receipt",
        Format = "pdf",
        PayloadReference = "documents/receipts/019bd000.pdf",
        Copies = 1,
        Status = PrintJobStatuses.Queued,
        RequestedAt = At.AddMinutes(-5),
    };

    private static PrintJob Resolved(string status)
    {
        var job = Queued();

        var resolved = status == PrintJobStatuses.Printed
            ? job.TryMarkPrinted(Station, "counter-1", At)
            : job.TryMarkFailed(Station, "counter-1", "First failure.", At);

        resolved.ShouldBeTrue("test setup must resolve the job before exercising a second resolution");

        return job;
    }
}
