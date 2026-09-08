using System.Globalization;
using System.Text;
using Tailor360.Platform.Abstractions.Ports;

namespace Tailor360.Web.Timeline;

/// <summary>
/// Merges every module's contribution to one customer's timeline into a single page.
/// </summary>
/// <remarks>
/// <para>
/// This lives in the host, and that is an architectural decision rather than a convenience.
/// <c>docs/architecture/architecture-rules.md</c> records it as ROD-02: the port is declared in
/// <c>Platform.Abstractions</c>, each module implements it over the facts it owns, and the host merges
/// the answers. No module reads another module's tables, and none of them knows the others exist. A
/// cross-module <em>join</em> would need an architecture decision record; a merge does not, because
/// nothing here queries anything — it sorts lists.
/// </para>
/// <para>
/// <strong>A source that fails does not fail the timeline.</strong> A customer's history is worth
/// reading when one module is unwell, and a screen that showed nothing at all would be a worse answer
/// than one that showed the rest and said which part is missing. So a throwing source is caught, named
/// in the response, and the page is built from the others. The one thing that is never done is
/// pretending: a page assembled without a source says so.
/// </para>
/// </remarks>
/// <param name="sources">Every module that contributes entries.</param>
public sealed class CustomerTimelineComposer(IEnumerable<ITimelineSource> sources)
{
    private readonly ITimelineSource[] _sources = [.. sources];

    /// <summary>Composes one page.</summary>
    /// <param name="request">Which customer, for whom, and where to resume.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The page, newest first.</returns>
    public async Task<CustomerTimelinePage> ComposeAsync(
        CustomerTimelineRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var limit = Math.Clamp(request.Limit, 1, TimelineQuery.MaximumLimit);

        // One more than the caller asked for, from every source. Whether there is another page is then
        // a fact about the merged list rather than a question put back to each source: if the merge
        // holds more than the caller asked for, at least one entry is being left behind.
        var query = new TimelineQuery(
            request.CustomerId,
            request.Organisation,
            request.AssignedBranches,
            request.Permissions,
            Decode(request.Cursor),
            limit + 1);

        var reads = _sources.Select(source => ReadAsync(source, query, cancellationToken));
        var answers = await Task.WhenAll(reads);

        var unavailable = answers
            .Where(answer => !answer.Succeeded)
            .Select(answer => answer.Source)
            .Order(StringComparer.Ordinal)
            .ToArray();

        var merged = answers
            .SelectMany(answer => answer.Entries)
            .OrderByDescending(entry => entry.OccurredAt)
            .ThenByDescending(entry => entry.Id)
            .ToArray();

        var page = merged.Take(limit).ToArray();
        var hasMore = merged.Length > limit;

        return new CustomerTimelinePage(
            page,
            hasMore && page.Length > 0 ? Encode(page[^1].Position) : null,
            unavailable);
    }

    private static async Task<SourceAnswer> ReadAsync(
        ITimelineSource source,
        TimelineQuery query,
        CancellationToken cancellationToken)
    {
        try
        {
            var entries = await source.ReadAsync(query, cancellationToken);

            return new SourceAnswer(source.SourceName, entries ?? [], Succeeded: true);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // Deliberately every exception but cancellation. A source is another module's code reading
            // another module's database, and the failures worth surviving here are the ones nobody
            // predicted — a migration half-applied, a connection exhausted, a projection that threw on
            // one bad row. Cancellation is the caller going away, which is not a partial failure.
            return new SourceAnswer(source.SourceName, [], Succeeded: false);
        }
    }

    /// <summary>Encodes a position as the opaque cursor a client sends back.</summary>
    /// <remarks>
    /// The same shape the customer search uses, and for the same reason: keyset, never offset. It is
    /// deliberately <em>not</em> the audit trail's own cursor, which is keyed on the chain sequence —
    /// that counts every write the installation has ever made, and
    /// <c>docs/architecture/conventions.md</c> section 3.1 says a sequential identifier is never
    /// exposed.
    /// </remarks>
    private static string Encode(TimelinePosition position)
        => Convert.ToBase64String(Encoding.UTF8.GetBytes(
            string.Create(CultureInfo.InvariantCulture, $"{position.OccurredAt:O}|{position.EntryId}")));

    /// <summary>
    /// Reads a cursor, answering "start at the newest" for anything it cannot read.
    /// </summary>
    /// <remarks>
    /// Defensive on purpose, exactly as the customer search is: a truncated or edited cursor is a first
    /// page, not a 400. It carries no authority — the customer, the organisation and the caller's
    /// permissions are settled before it is looked at, so the worst a forged cursor achieves is a page
    /// of the same customer's history at a different point.
    /// </remarks>
    private static TimelinePosition? Decode(string? cursor)
    {
        if (string.IsNullOrWhiteSpace(cursor))
        {
            return null;
        }

        try
        {
            var parts = Encoding.UTF8.GetString(Convert.FromBase64String(cursor)).Split('|');

            if (parts.Length is not 2
                || !DateTimeOffset.TryParse(
                    parts[0], CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var occurredAt)
                || !Guid.TryParse(parts[1], out var entryId))
            {
                return null;
            }

            return new TimelinePosition(occurredAt, entryId);
        }
        catch (FormatException)
        {
            return null;
        }
    }

    private sealed record SourceAnswer(string Source, IReadOnlyList<TimelineEntry> Entries, bool Succeeded);
}

/// <summary>What a composed page was asked for.</summary>
/// <param name="CustomerId">The customer.</param>
/// <param name="Organisation">The organisation the request belongs to.</param>
/// <param name="AssignedBranches">The branches the caller may act in.</param>
/// <param name="Permissions">The caller's effective permissions.</param>
/// <param name="Cursor">Where to resume, or null for the newest page.</param>
/// <param name="Limit">How many entries to return.</param>
public sealed record CustomerTimelineRequest(
    Guid CustomerId,
    Tailor360.Platform.Abstractions.Multitenancy.OrganisationContext Organisation,
    IReadOnlySet<Guid> AssignedBranches,
    IReadOnlySet<string> Permissions,
    string? Cursor,
    int Limit);

/// <summary>One page of a customer's merged history.</summary>
/// <param name="Entries">The entries, newest first.</param>
/// <param name="NextCursor">Where the next page starts, or null at the end.</param>
/// <param name="UnavailableSources">
/// The modules that could not answer, named so that a screen can say which part of the history is
/// missing rather than implying it did not happen. Empty when every source answered.
/// </param>
public sealed record CustomerTimelinePage(
    IReadOnlyList<TimelineEntry> Entries,
    string? NextCursor,
    IReadOnlyList<string> UnavailableSources);
