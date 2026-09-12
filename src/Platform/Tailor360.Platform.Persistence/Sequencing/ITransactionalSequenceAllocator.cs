using System.Data.Common;

namespace Tailor360.Platform.Persistence.Sequencing;

/// <summary>
/// Allocates a document number inside the caller's own transaction, holding the sequence row's lock until
/// that transaction ends: two documents of one scope are numbered one after the other, and a document that
/// rolls back returns its number. For the statutory series that may not hold a gap — invoices and the
/// notes — where <see cref="Tailor360.Platform.Abstractions.Sequencing.ISequenceAllocator"/>, which commits
/// on the platform connection at once, would leave one. Reached from a module's <c>Infrastructure</c>
/// project, which is the layer that holds the transaction.
/// </summary>
public interface ITransactionalSequenceAllocator
{
    /// <summary>Allocates the next value of a sequence on the transaction given.</summary>
    /// <param name="sequenceKey">Sequence identity, for example <c>invoice</c>.</param>
    /// <param name="scope">Scope that owns its own numbering: the organisation, the branch and the financial year.</param>
    /// <param name="transaction">The open PostgreSQL transaction the document commits in.</param>
    /// <param name="cancellationToken">Cancels the allocation.</param>
    Task<long> NextAsync(string sequenceKey, string scope, DbTransaction transaction, CancellationToken cancellationToken = default);
}
