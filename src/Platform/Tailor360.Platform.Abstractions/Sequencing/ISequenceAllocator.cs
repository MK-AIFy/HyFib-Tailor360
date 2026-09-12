namespace Tailor360.Platform.Abstractions.Sequencing;

/// <summary>
/// Allocates per-scope document numbers (order, estimate, job card) on the platform connection,
/// committed at once: a number it hands out is taken whether or not the caller's own write commits, which
/// is what a display number whose series may hold a gap accepts (<c>docs/architecture/conventions.md</c>
/// section 3.2). A series that may not hold a gap — an invoice, a credit note, a receipt — is allocated
/// inside the document's own transaction through the persistence layer's transactional allocator, which
/// an <c>Infrastructure</c> project reaches and this abstraction deliberately does not expose.
/// </summary>
public interface ISequenceAllocator
{
    /// <summary>
    /// Allocates the next value of a sequence, creating the sequence on first use.
    /// </summary>
    /// <param name="sequenceKey">Sequence identity, for example <c>order</c>.</param>
    /// <param name="scope">Scope that owns its own numbering, typically branch and financial year.</param>
    /// <param name="cancellationToken">Cancels the allocation.</param>
    Task<long> NextAsync(string sequenceKey, string scope, CancellationToken cancellationToken = default);
}
