using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Tailor360.Platform.Abstractions.FeatureFlags;
using Tailor360.Platform.Abstractions.Multitenancy;
using Tailor360.Platform.Abstractions.Time;
using Tailor360.Platform.Persistence.Contexts;
using Tailor360.Platform.Persistence.Entities;

namespace Tailor360.Platform.Persistence.FeatureFlags;

/// <summary>
/// Evaluates feature flags from a cached snapshot. The snapshot is refreshed when it is older than the
/// configured propagation bound and immediately on the node that changed a value, which gives a stated
/// guarantee rather than the vaguer "eventually" a bare cache would offer.
/// </summary>
/// <param name="scopeFactory">Used to open a short-lived scope when refreshing.</param>
/// <param name="clock">The clock.</param>
/// <param name="options">Propagation configuration.</param>
public sealed class FeatureFlagStore(
    IServiceScopeFactory scopeFactory,
    IClock clock,
    IOptions<FeatureFlagOptions> options)
    : IFeatureFlags, IDisposable
{
    private readonly SemaphoreSlim _refreshGate = new(1, 1);
    private Snapshot _snapshot = Snapshot.Empty;

    /// <inheritdoc />
    public async Task<bool> IsEnabledAsync(
        string flagKey,
        OrganisationContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(flagKey);
        ArgumentNullException.ThrowIfNull(context);

        var snapshot = await GetSnapshotAsync(cancellationToken);

        // A branch value overrides the organisation value for that branch. An unknown flag is off: a
        // feature that is not yet configured must not be live.
        if (context.BranchId is { } branchId
            && snapshot.Branch.TryGetValue((flagKey, branchId), out var branchValue))
        {
            return branchValue;
        }

        return snapshot.Organisation.TryGetValue(flagKey, out var organisationValue) && organisationValue;
    }

    /// <summary>
    /// Discards the cached snapshot. Called on the node that changed a value so that the change is
    /// visible to it at once, without waiting for the propagation bound.
    /// </summary>
    public void Invalidate() => _snapshot = Snapshot.Empty;

    /// <summary>The age of the cached snapshot, exposed for the health check.</summary>
    public TimeSpan SnapshotAge => _snapshot.LoadedAt is null
        ? TimeSpan.MaxValue
        : clock.UtcNow - _snapshot.LoadedAt.Value;

    /// <summary>Releases the refresh gate.</summary>
    public void Dispose() => _refreshGate.Dispose();

    private async Task<Snapshot> GetSnapshotAsync(CancellationToken cancellationToken)
    {
        var current = _snapshot;
        if (current.LoadedAt is { } loadedAt && clock.UtcNow - loadedAt < options.Value.PropagationBound)
        {
            return current;
        }

        await _refreshGate.WaitAsync(cancellationToken);
        try
        {
            current = _snapshot;
            if (current.LoadedAt is { } stillFresh && clock.UtcNow - stillFresh < options.Value.PropagationBound)
            {
                return current;
            }

            using var scope = scopeFactory.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<PlatformDbContext>();

            var rows = await context.FeatureFlags.AsNoTracking().ToListAsync(cancellationToken);

            var organisation = rows
                .Where(r => r.ScopeType == FeatureFlagScopes.Organisation)
                .ToDictionary(r => r.Key, r => r.Enabled, StringComparer.Ordinal);

            var branch = rows
                .Where(r => r.ScopeType == FeatureFlagScopes.Branch && r.ScopeId is not null)
                .ToDictionary(r => (r.Key, r.ScopeId!.Value), r => r.Enabled);

            _snapshot = new Snapshot(organisation, branch, clock.UtcNow);
            return _snapshot;
        }
        finally
        {
            _refreshGate.Release();
        }
    }

    private sealed record Snapshot(
        IReadOnlyDictionary<string, bool> Organisation,
        IReadOnlyDictionary<(string Key, Guid BranchId), bool> Branch,
        DateTimeOffset? LoadedAt)
    {
        public static Snapshot Empty { get; } = new(
            new Dictionary<string, bool>(StringComparer.Ordinal),
            new Dictionary<(string, Guid), bool>(),
            null);
    }
}
