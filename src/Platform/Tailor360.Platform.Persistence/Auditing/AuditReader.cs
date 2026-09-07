using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Tailor360.Platform.Abstractions.Auditing;
using Tailor360.Platform.Persistence.Contexts;

namespace Tailor360.Platform.Persistence.Auditing;

/// <summary>
/// Reads the audit trail over its own indexes.
/// </summary>
/// <remarks>
/// Paging is keyset on the sequence rather than by offset, and the reason is stronger here than
/// elsewhere: the table is append-only and is being appended to while somebody reads it, so an offset
/// would shift under every page and an investigation would silently skip entries. The sequence is
/// monotonic and unique, which makes it the whole key — no tie-break is needed.
/// </remarks>
/// <param name="context">The platform context.</param>
public sealed class AuditReader(PlatformDbContext context) : IAuditReader
{
    /// <inheritdoc />
    public async Task<AuditPage> SearchAsync(
        AuditQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        var limit = Math.Clamp(query.Limit, 1, AuditQuery.MaximumExportLimit);
        var entries = context.AuditEvents.AsNoTracking();

        if (query.EntityType is { Length: > 0 } entityType)
        {
            entries = entries.Where(entry => entry.EntityType == entityType);

            if (query.EntityId is { } entityId)
            {
                entries = entries.Where(entry => entry.EntityId == entityId);
            }
        }

        if (query.ActorId is { } actorId)
        {
            entries = entries.Where(entry => entry.ActorId == actorId);
        }

        if (query.ActionPrefix is { Length: > 0 } prefix)
        {
            entries = entries.Where(entry => entry.Action.StartsWith(prefix));
        }

        if (query.From is { } from)
        {
            entries = entries.Where(entry => entry.OccurredAt >= from);
        }

        if (query.To is { } to)
        {
            entries = entries.Where(entry => entry.OccurredAt < to);
        }

        if (Decode(query.Cursor) is { } after)
        {
            // Newest first, so continuing means going further back: strictly below the last sequence
            // this reader has already been shown.
            entries = entries.Where(entry => entry.Sequence < after);
        }

        var page = await entries
            .OrderByDescending(entry => entry.Sequence)
            .Take(limit + 1)
            .Select(entry => new AuditRecord(
                entry.Sequence,
                entry.OccurredAt,
                entry.Action,
                entry.EntityType,
                entry.EntityId,
                entry.ActorId,
                entry.ActorDisplayName,
                entry.BranchId,
                entry.CorrelationId,
                entry.Reason,
                entry.Summary,
                entry.Before,
                entry.After))
            .ToListAsync(cancellationToken);

        var hasMore = page.Count > limit;
        var shown = page.Take(limit).ToList();

        return new AuditPage(
            shown,
            hasMore && shown.Count > 0
                ? shown[^1].Sequence.ToString(CultureInfo.InvariantCulture)
                : null);
    }

    private static long? Decode(string? cursor)
        => long.TryParse(cursor, NumberStyles.None, CultureInfo.InvariantCulture, out var sequence)
            ? sequence
            : null;
}
