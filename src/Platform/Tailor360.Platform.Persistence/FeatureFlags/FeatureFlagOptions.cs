using System.ComponentModel.DataAnnotations;
using Tailor360.Platform.Abstractions.FeatureFlags;

namespace Tailor360.Platform.Persistence.FeatureFlags;

/// <summary>Feature flag evaluation configuration.</summary>
public sealed class FeatureFlagOptions
{
    /// <summary>The configuration section these options bind to.</summary>
    public const string SectionName = "FeatureFlags";

    /// <summary>
    /// How long a cached flag snapshot may be served before it is refreshed. This is the documented
    /// propagation bound: a change is visible on the node that made it immediately, and on every other
    /// node within this window. Evaluating a flag per request against the database instead would put a
    /// query on every code path that asks.
    /// </summary>
    [Range(typeof(TimeSpan), "00:00:01", "00:05:00")]
    public TimeSpan PropagationBound { get; set; } =
        TimeSpan.FromSeconds(FeatureFlagPropagation.DefaultSeconds);
}
