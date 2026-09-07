using Microsoft.EntityFrameworkCore;
using Tailor360.Platform.Abstractions.Concurrency;
using Tailor360.Platform.Abstractions.FeatureFlags;
using Tailor360.Platform.Abstractions.Results;
using Tailor360.Platform.Abstractions.Time;
using Tailor360.Platform.Persistence.Concurrency;
using Tailor360.Platform.Persistence.Contexts;
using Tailor360.Platform.Persistence.Entities;

namespace Tailor360.Platform.Persistence.FeatureFlags;

/// <summary>
/// Reads and writes feature flags for an administrative screen.
/// </summary>
/// <remarks>
/// Organisation-scoped rows only. A branch override is a real part of the model and is deliberately not
/// editable here: a screen that could set a flag for one branch needs to show which branches already
/// differ, and that is a different screen from the one this issue builds. Until it exists, a branch
/// override is set by the command-line tool, where the operator can see what they are doing.
/// </remarks>
/// <param name="context">The platform context.</param>
/// <param name="flags">The evaluation cache, invalidated so the writing node sees its own change.</param>
/// <param name="clock">The clock.</param>
public sealed class FeatureFlagAdministration(
    PlatformDbContext context,
    FeatureFlagStore flags,
    IClock clock)
    : IFeatureFlagAdministration
{
    /// <inheritdoc />
    public async Task<IReadOnlyList<AdministeredFlag>> ListAsync(
        CancellationToken cancellationToken = default)
    {
        var rows = await Organisation().OrderBy(flag => flag.Key).ToListAsync(cancellationToken);

        return [.. rows.Select(Describe)];
    }

    /// <inheritdoc />
    public async Task<AdministeredFlag?> FindAsync(
        string key,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        var row = await Organisation()
            .FirstOrDefaultAsync(flag => flag.Key == key.Trim(), cancellationToken);

        return row is null ? null : Describe(row);
    }

    /// <inheritdoc />
    public async Task<Result<AdministeredFlag>> SetAsync(
        string key,
        bool enabled,
        string reason,
        EntityTag? expectedVersion,
        Guid actor,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        var normalised = key.Trim();

        var row = await Organisation()
            .FirstOrDefaultAsync(flag => flag.Key == normalised, cancellationToken);

        if (row is null)
        {
            // Never configured. There is nothing to have changed underneath the administrator, so a
            // precondition would be a demand they cannot satisfy.
            if (expectedVersion is not null)
            {
                return Result.Failure<AdministeredFlag>(FeatureFlagErrors.NotConfigured(normalised));
            }

            row = new FeatureFlag
            {
                Key = normalised,

                // The column defaults to the empty identifier for an organisation-scoped row and the
                // composite key includes it, so null here would be a different row from the one the
                // evaluator reads.
                ScopeType = FeatureFlagScopes.Organisation,
                ScopeId = Guid.Empty,
                Version = 0,
            };

            context.FeatureFlags.Add(row);
        }
        else if (expectedVersion is { } expected
                 && !expected.Matches(context.EntityTagOf(row)))
        {
            return Result.Failure<AdministeredFlag>(FeatureFlagErrors.VersionConflict);
        }

        row.Enabled = enabled;
        row.Reason = reason;
        row.UpdatedAt = clock.UtcNow;
        row.UpdatedBy = actor;
        row.Version++;

        try
        {
            await context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Result.Failure<AdministeredFlag>(FeatureFlagErrors.VersionConflict);
        }

        // This node sees its own change at once. Every other node converges within the configured
        // propagation bound, which the caller states on the screen rather than leaving somebody to
        // wonder why the toggle they just moved has not taken effect on the till.
        flags.Invalidate();

        return Result.Success(Describe(row));
    }

    private IQueryable<FeatureFlag> Organisation()
        => context.FeatureFlags.Where(flag => flag.ScopeType == FeatureFlagScopes.Organisation);

    private AdministeredFlag Describe(FeatureFlag row) => new(
        row.Key,
        row.Enabled,
        row.Reason,
        row.UpdatedAt,
        row.UpdatedBy,
        row.Version,
        context.EntityTagOf(row));
}
