namespace Tailor360.Platform.Abstractions.Sequencing;

/// <summary>
/// Allocates gap-free, per-scope document numbers (invoice, estimate, receipt, credit note, job card).
/// Statutory documents may not have gaps, so allocation participates in the caller's transaction and a
/// rolled-back transaction returns the number to the sequence.
/// </summary>
public interface ISequenceAllocator
{
    /// <summary>
    /// Allocates the next value of a sequence, creating the sequence on first use.
    /// </summary>
    /// <param name="sequenceKey">Sequence identity, for example <c>invoice</c>.</param>
    /// <param name="scope">Scope that owns its own numbering, typically branch and financial year.</param>
    Task<long> NextAsync(string sequenceKey, string scope, CancellationToken cancellationToken = default);
}
