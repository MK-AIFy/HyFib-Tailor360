using Shouldly;
using Tailor360.Platform.Abstractions.Multitenancy;
using Tailor360.Platform.Abstractions.Ports;
using Tailor360.Web.Timeline;

namespace Tailor360.ContractTests;

/// <summary>
/// The merge: what the host does with several modules' answers about one customer.
/// </summary>
/// <remarks>
/// <para>
/// Tested against stub sources rather than through the endpoint, because the merge is the part with no
/// database in it and the part a second module will change. What each source contributes is its own
/// module's test; what the host does with the contributions is this.
/// </para>
/// <para>
/// It sits in the contract tier rather than the unit tier for one reason: the composer is host code,
/// and the unit tier deliberately references no host. Dragging ASP.NET into that project to reach one
/// class would cost more than it is worth, and this tier already owns what the host publishes.
/// </para>
/// </remarks>
[Trait("Category", "Contract")]
public sealed class CustomerTimelineComposerTests
{
    private static readonly Guid CustomerId = Guid.Parse("0199c000-0000-7000-8000-00000000a001");
    private static readonly Guid OrganisationId = Guid.Parse("0199c000-0000-7000-8000-00000000a002");

    private static readonly DateTimeOffset Noon =
        new(2026, 9, 8, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task MergesEverySourceNewestFirst()
    {
        var composer = new CustomerTimelineComposer(
        [
            Source("orders", Entry("orders", Noon.AddHours(-2), 2), Entry("orders", Noon, 5)),
            Source("customers", Entry("customers", Noon.AddHours(-1), 1), Entry("customers", Noon.AddHours(-3), 9)),
        ]);

        var page = await composer.ComposeAsync(Request(), TestContext.Current.CancellationToken);

        page.Entries.Select(entry => entry.OccurredAt).ShouldBe(
            [Noon, Noon.AddHours(-1), Noon.AddHours(-2), Noon.AddHours(-3)]);

        page.UnavailableSources.ShouldBeEmpty();
        page.NextCursor.ShouldBeNull();
    }

    [Fact]
    public async Task BreaksATieOnTheIdentifierSoOneInstantIsStillAnOrder()
    {
        // Two entries at one instant is not a contrivance: a merge writes one against each record at
        // the same instant, by construction. Without the tie-break a page that stopped between them
        // would either repeat one or skip one, and which would depend on the order the sources
        // happened to answer in.
        var composer = new CustomerTimelineComposer(
        [
            Source("b", Entry("b", Noon, 7)),
            Source("a", Entry("a", Noon, 3)),
        ]);

        var page = await composer.ComposeAsync(Request(), TestContext.Current.CancellationToken);

        page.Entries.Select(entry => entry.Source).ShouldBe(["b", "a"]);
    }

    [Fact]
    public async Task KeepsTheNewestPageAndOffersACursorForTheRest()
    {
        var composer = new CustomerTimelineComposer(
        [
            Source("customers", Entry("customers", Noon, 1), Entry("customers", Noon.AddHours(-1), 2)),
            Source("orders", Entry("orders", Noon.AddHours(-2), 3)),
        ]);

        var page = await composer.ComposeAsync(Request(limit: 2), TestContext.Current.CancellationToken);

        page.Entries.Count.ShouldBe(2);
        page.NextCursor.ShouldNotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task OffersNoCursorWhenEverySourceHasBeenDrained()
    {
        var composer = new CustomerTimelineComposer([Source("customers", Entry("customers", Noon, 1))]);

        var page = await composer.ComposeAsync(Request(limit: 5), TestContext.Current.CancellationToken);

        page.Entries.Count.ShouldBe(1);
        page.NextCursor.ShouldBeNull("a cursor offered at the end sends a client round one more time for nothing");
    }

    [Fact]
    public async Task ResumesStrictlyAfterTheEntryThePreviousPageStoppedAt()
    {
        var recorded = new List<TimelineQuery>();
        var composer = new CustomerTimelineComposer(
            [Recording("customers", recorded, Entry("customers", Noon, 1), Entry("customers", Noon.AddHours(-1), 2))]);

        var first = await composer.ComposeAsync(Request(limit: 1), TestContext.Current.CancellationToken);
        first.NextCursor.ShouldNotBeNull();

        await composer.ComposeAsync(Request(limit: 1, cursor: first.NextCursor), TestContext.Current.CancellationToken);

        var resumed = recorded[^1].Before.ShouldNotBeNull();
        resumed.OccurredAt.ShouldBe(Noon);
        resumed.EntryId.ShouldBe(first.Entries[0].Id);
    }

    [Fact]
    public async Task TreatsAnUnreadableCursorAsTheNewestPageRatherThanAnError()
    {
        // The same posture the customer search takes. A cursor carries no authority — the customer and
        // the caller's permissions are settled before it is read — so the worst a mangled one can do is
        // start again from the top.
        var recorded = new List<TimelineQuery>();
        var composer = new CustomerTimelineComposer([Recording("customers", recorded, Entry("customers", Noon, 1))]);

        await composer.ComposeAsync(Request(cursor: "not-a-cursor-at-all"), TestContext.Current.CancellationToken);

        recorded[^1].Before.ShouldBeNull();
    }

    [Fact]
    public async Task ServesTheRestOfTheHistoryWhenOneSourceFailsAndSaysWhichOne()
    {
        var composer = new CustomerTimelineComposer(
        [
            Source("customers", Entry("customers", Noon, 1)),
            Failing("orders"),
        ]);

        var page = await composer.ComposeAsync(Request(), TestContext.Current.CancellationToken);

        page.Entries.Count.ShouldBe(1);
        page.UnavailableSources.ShouldBe(["orders"]);
    }

    [Fact]
    public async Task LetsCancellationThroughRatherThanReportingItAsAFailedSource()
    {
        // Cancellation is the caller going away, not a module being unwell. Swallowing it would turn
        // an abandoned request into a partial answer that nobody is waiting for.
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        var composer = new CustomerTimelineComposer([Cancelling("customers")]);

        await Should.ThrowAsync<OperationCanceledException>(
            () => composer.ComposeAsync(Request(), cancellation.Token));
    }

    [Fact]
    public async Task ClampsAPageSizeNobodyShouldBeAbleToAskFor()
    {
        var recorded = new List<TimelineQuery>();
        var composer = new CustomerTimelineComposer([Recording("customers", recorded)]);

        await composer.ComposeAsync(Request(limit: 10_000), TestContext.Current.CancellationToken);
        await composer.ComposeAsync(Request(limit: 0), TestContext.Current.CancellationToken);

        // One more than the clamped page: whether there is another page is a fact about the merge.
        recorded[0].Limit.ShouldBe(TimelineQuery.MaximumLimit + 1);
        recorded[1].Limit.ShouldBe(2);
    }

    [Fact]
    public async Task AsksEverySourceForTheSameThing()
    {
        var first = new List<TimelineQuery>();
        var second = new List<TimelineQuery>();

        var composer = new CustomerTimelineComposer(
            [Recording("customers", first), Recording("orders", second)]);

        await composer.ComposeAsync(Request(), TestContext.Current.CancellationToken);

        first[0].CustomerId.ShouldBe(CustomerId);
        first[0].Organisation.OrganisationId.ShouldBe(OrganisationId);
        first[0].ShouldBe(second[0], "a source that was asked something different would answer about a different caller");
    }

    private static CustomerTimelineRequest Request(int limit = 25, string? cursor = null)
        => new(
            CustomerId,
            new OrganisationContext(OrganisationId, null),
            new HashSet<Guid>(),
            new HashSet<string>(StringComparer.Ordinal) { "customers.read" },
            cursor,
            limit);

    private static TimelineEntry Entry(string source, DateTimeOffset occurredAt, byte id) => new(
        new Guid(id, 0, 0, [0, 0, 0, 0, 0, 0, 0, 0]),
        occurredAt,
        source,
        $"{source}.something.happened",
        "Something happened",
        "In more words.",
        Reason: null,
        ReasonPermission: null,
        ReferenceType: null,
        ReferenceId: null,
        ExpandPermission: null,
        BranchId: null,
        ActorDisplayName: null);

    private static StubSource Source(string name, params TimelineEntry[] entries)
        => new(name, entries, null);

    private static StubSource Recording(string name, List<TimelineQuery> recorded, params TimelineEntry[] entries)
        => new(name, entries, recorded);

    private static ThrowingSource Failing(string name)
        => new(name, () => new InvalidOperationException("the projection threw on a bad row"));

    private static ThrowingSource Cancelling(string name)
        => new(name, () => new OperationCanceledException());

    private sealed class StubSource(string name, TimelineEntry[] entries, List<TimelineQuery>? recorded)
        : ITimelineSource
    {
        public string SourceName => name;

        public Task<IReadOnlyList<TimelineEntry>> ReadAsync(
            TimelineQuery query,
            CancellationToken cancellationToken = default)
        {
            recorded?.Add(query);

            var page = entries
                .Where(entry => query.Before is not { } before
                                || entry.OccurredAt < before.OccurredAt
                                || (entry.OccurredAt == before.OccurredAt
                                    && entry.Id.CompareTo(before.EntryId) < 0))
                .OrderByDescending(entry => entry.OccurredAt)
                .ThenByDescending(entry => entry.Id)
                .Take(query.Limit)
                .ToArray();

            return Task.FromResult<IReadOnlyList<TimelineEntry>>(page);
        }
    }

    private sealed class ThrowingSource(string name, Func<Exception> thrown) : ITimelineSource
    {
        public string SourceName => name;

        public Task<IReadOnlyList<TimelineEntry>> ReadAsync(
            TimelineQuery query,
            CancellationToken cancellationToken = default)
            => throw thrown();
    }
}
