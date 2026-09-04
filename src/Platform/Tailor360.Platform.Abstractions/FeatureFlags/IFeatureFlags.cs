using Tailor360.Platform.Abstractions.Multitenancy;

namespace Tailor360.Platform.Abstractions.FeatureFlags;

/// <summary>
/// Evaluates feature flags. Flags are owned by the Platform module (ADR-0013): the administration
/// surface in Identity calls this contract rather than reading the flag tables. Evaluation is cached
/// in process and invalidated by a database notification; a change is visible on the mutating node
/// immediately and on every node within 30 seconds.
/// </summary>
public interface IFeatureFlags
{
    /// <summary>
    /// Whether a flag is on for the supplied context. Unknown flags evaluate to their safe default,
    /// which is off, so a missing flag never enables an unfinished feature.
    /// </summary>
    Task<bool> IsEnabledAsync(
        string flagKey,
        OrganisationContext context,
        CancellationToken cancellationToken = default);
}
