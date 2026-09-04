namespace Tailor360.Platform.Persistence.Entities;

/// <summary>
/// A gap-free counter for statutory document numbers. A PostgreSQL sequence is not used because a
/// sequence deliberately leaves gaps when a transaction rolls back, and a tax invoice series with gaps
/// is a problem at audit time.
/// </summary>
public sealed class SequenceRow
{
    /// <summary>What is being numbered, for example <c>invoice</c>.</summary>
    public string SequenceKey { get; set; } = string.Empty;

    /// <summary>The scope with its own numbering, typically branch and financial year.</summary>
    public string Scope { get; set; } = string.Empty;

    /// <summary>The value the next allocation will return.</summary>
    public long NextValue { get; set; } = 1;

    /// <summary>When the sequence last moved.</summary>
    public DateTimeOffset UpdatedAt { get; set; }
}
