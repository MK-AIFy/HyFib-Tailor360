namespace Tailor360.Platform.Abstractions.Ports;

/// <summary>
/// Contributes entries to the customer timeline. Each module implements this for the facts it owns and
/// the web host composes the results, so no module reads another module's tables to build the view.
/// </summary>
public interface ITimelineSource
{
    /// <summary>A stable name for the contributing module, used for filtering and for partial-failure reporting.</summary>
    string SourceName { get; }

    /// <summary>
    /// Returns this module's timeline entries for a customer within the requested window. Implementations
    /// must respect the caller's branch scope and must not throw when they have nothing to contribute.
    /// </summary>
    Task<IReadOnlyList<TimelineEntry>> GetEntriesAsync(
        Guid customerId,
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken = default);
}

/// <summary>One entry on a customer's timeline.</summary>
/// <param name="OccurredAt">When it happened, in UTC.</param>
/// <param name="Kind">Stable kind, for example <c>order.confirmed</c>, used to choose an icon and a label.</param>
/// <param name="Title">Short human-readable title.</param>
/// <param name="Detail">Optional longer description.</param>
/// <param name="ReferenceType">The type of the entity the entry links to, for example <c>Order</c>.</param>
/// <param name="ReferenceId">Identity of the entity the entry links to.</param>
/// <param name="BranchId">The branch the entry belongs to.</param>
public sealed record TimelineEntry(
    DateTimeOffset OccurredAt,
    string Kind,
    string Title,
    string? Detail,
    string? ReferenceType,
    Guid? ReferenceId,
    Guid? BranchId);
