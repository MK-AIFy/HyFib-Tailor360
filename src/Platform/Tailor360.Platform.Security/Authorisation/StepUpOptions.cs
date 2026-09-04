using System.ComponentModel.DataAnnotations;

namespace Tailor360.Platform.Security.Authorisation;

/// <summary>How fresh a re-authentication must be for a step-up permission to be satisfied.</summary>
public sealed class StepUpOptions
{
    /// <summary>The configuration section these options bind to.</summary>
    public const string SectionName = "Security:StepUp";

    /// <summary>
    /// How long a re-authentication remains valid for step-up purposes. Short enough that an unattended
    /// device cannot be used to approve a financially final action.
    /// </summary>
    [Range(typeof(TimeSpan), "00:01:00", "00:30:00")]
    public TimeSpan Freshness { get; set; } = TimeSpan.FromMinutes(5);
}
