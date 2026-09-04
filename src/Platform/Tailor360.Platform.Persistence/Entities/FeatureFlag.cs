namespace Tailor360.Platform.Persistence.Entities;

/// <summary>
/// A feature flag value. Flags are owned by the Platform module; the administration screens in Identity
/// change them through the platform contract rather than by writing to this table.
/// </summary>
public sealed class FeatureFlag
{
    /// <summary>The flag key.</summary>
    public string Key { get; set; } = string.Empty;

    /// <summary>
    /// The scope the value applies to: the whole organisation, or one branch. A branch value overrides
    /// the organisation value for that branch only.
    /// </summary>
    public string ScopeType { get; set; } = FeatureFlagScopes.Organisation;

    /// <summary>The branch identity when the scope is a branch.</summary>
    public Guid? ScopeId { get; set; }

    /// <summary>Whether the feature is on. The safe default for an absent flag is off.</summary>
    public bool Enabled { get; set; }

    /// <summary>Incremented on every change, so an evaluation can record exactly which value it used.</summary>
    public int Version { get; set; } = 1;

    /// <summary>When the value last changed.</summary>
    public DateTimeOffset UpdatedAt { get; set; }

    /// <summary>Who changed it.</summary>
    public Guid? UpdatedBy { get; set; }

    /// <summary>Why it was changed. Required, because a flag change is an operational act.</summary>
    public string? Reason { get; set; }
}

/// <summary>The scopes a flag value can apply to.</summary>
public static class FeatureFlagScopes
{
    /// <summary>Applies to the whole organisation.</summary>
    public const string Organisation = "organisation";

    /// <summary>Applies to one branch, overriding the organisation value there.</summary>
    public const string Branch = "branch";
}
