using Tailor360.Platform.Abstractions.Concurrency;
using Tailor360.Platform.Abstractions.Results;

namespace Tailor360.Platform.Abstractions.FeatureFlags;

/// <summary>
/// Reading and changing feature flags as an administrator, as opposed to evaluating them.
/// </summary>
/// <remarks>
/// Separate from <see cref="IFeatureFlags"/> because the two are different jobs with different
/// guarantees. Evaluation answers "is this on for me" from a cached snapshot, thousands of times a
/// second, and must never touch the database on the request path; administration reads the stored row
/// with its version and its history, once, when somebody opens a screen.
/// <para>
/// It is a Platform contract because <c>platform.feature_flags</c> is Platform's table. A module that
/// published the screens for it drives them through this port rather than mapping the table, which is
/// what keeps the module boundary a boundary (ARCH-005, module-ownership MO-2).
/// </para>
/// </remarks>
public interface IFeatureFlagAdministration
{
    /// <summary>Every flag configured for the organisation, in key order.</summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<IReadOnlyList<AdministeredFlag>> ListAsync(CancellationToken cancellationToken = default);

    /// <summary>One flag, or null when it has never been configured.</summary>
    /// <param name="key">The flag key.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<AdministeredFlag?> FindAsync(string key, CancellationToken cancellationToken = default);

    /// <summary>
    /// Sets a flag's organisation-wide value, creating it if it has never been configured.
    /// </summary>
    /// <param name="key">The flag key.</param>
    /// <param name="enabled">What it should be.</param>
    /// <param name="reason">Why, as the administrator typed it.</param>
    /// <param name="expectedVersion">
    /// The version the administrator read, or null when they are configuring the flag for the first
    /// time and there is nothing to have changed underneath them.
    /// </param>
    /// <param name="actor">The administrator.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<Result<AdministeredFlag>> SetAsync(
        string key,
        bool enabled,
        string reason,
        EntityTag? expectedVersion,
        Guid actor,
        CancellationToken cancellationToken = default);
}

/// <summary>How long a changed flag takes to reach every node.</summary>
/// <remarks>
/// Declared beside the port rather than in the persistence options, because it is part of what this
/// contract promises: a screen that lets somebody change a flag has to be able to tell them roughly how
/// long the tills will take to agree, and the constant is that number. The persistence options default
/// to it and are what actually governs the refresh.
/// </remarks>
public static class FeatureFlagPropagation
{
    /// <summary>The bound, in whole seconds.</summary>
    public const int DefaultSeconds = 30;
}

/// <summary>One feature flag as an administrative screen sees it.</summary>
/// <param name="Key">The flag key.</param>
/// <param name="Enabled">Whether it is on for the organisation.</param>
/// <param name="Reason">Why it was last changed.</param>
/// <param name="UpdatedAt">When it was last changed.</param>
/// <param name="UpdatedBy">Who last changed it, or null for a value that arrived with the seed data.</param>
/// <param name="Revision">How many times it has been set, which is what an operator quotes.</param>
/// <param name="Version">The concurrency token an edit must present.</param>
public sealed record AdministeredFlag(
    string Key,
    bool Enabled,
    string? Reason,
    DateTimeOffset UpdatedAt,
    Guid? UpdatedBy,
    int Revision,
    EntityTag Version);

/// <summary>The failures flag administration reports.</summary>
public static class FeatureFlagErrors
{
    /// <summary>A precondition was presented for a flag that has never been configured.</summary>
    public static Error NotConfigured(string key) => Error.NotFound(
        "platform.feature-flag-not-configured",
        $"The flag '{key}' has never been configured, so there is no version to edit against.");

    /// <summary>Somebody else changed the flag first.</summary>
    public static Error VersionConflict { get; } = Error.Conflict(
        "platform.feature-flag-version-conflict",
        "Somebody else changed this flag while you had it open. Reload it and decide again.");
}
