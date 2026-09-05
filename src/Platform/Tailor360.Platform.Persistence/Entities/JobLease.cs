namespace Tailor360.Platform.Persistence.Entities;

/// <summary>
/// A lease on a scheduled job. Two worker instances are normal, and both will wake for the same job;
/// the lease is what stops both from running it.
/// </summary>
public sealed class JobLease
{
    /// <summary>The job being leased.</summary>
    public string JobName { get; set; } = string.Empty;

    /// <summary>The instance holding the lease.</summary>
    public string Owner { get; set; } = string.Empty;

    /// <summary>When the lease was taken.</summary>
    public DateTimeOffset AcquiredAt { get; set; }

    /// <summary>
    /// When the lease expires. A crashed holder's lease lapses and another instance takes over without
    /// an operator having to clear anything.
    /// </summary>
    public DateTimeOffset ExpiresAt { get; set; }
}
