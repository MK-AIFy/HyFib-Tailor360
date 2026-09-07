using Tailor360.Modules.Identity.Domain.Branches;
using Tailor360.Platform.Abstractions.Concurrency;
using Tailor360.Platform.Abstractions.Results;

namespace Tailor360.Modules.Identity.Application.Abstractions;

/// <summary>The branch register's reads and writes.</summary>
public interface IBranchStore
{
    /// <summary>Every branch in the organisation, in code order.</summary>
    /// <param name="organisationId">The organisation.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<IReadOnlyList<Branch>> ListAsync(
        Guid organisationId,
        CancellationToken cancellationToken = default);

    /// <summary>One branch, or null when there is none.</summary>
    /// <param name="branchId">The branch.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<Branch?> FindAsync(Guid branchId, CancellationToken cancellationToken = default);

    /// <summary>Whether a code is already in use in this organisation.</summary>
    /// <param name="organisationId">The organisation.</param>
    /// <param name="code">The code being claimed.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<bool> IsCodeTakenAsync(
        Guid organisationId,
        string code,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// How many accounts still depend on this branch: assigned to it, or calling it home.
    /// </summary>
    /// <remarks>
    /// The question behind controlled deactivation. Closing a branch that people are still assigned to
    /// would leave them with reach into somewhere that no longer operates, and their screens pointing
    /// at it — so the register refuses, and the administrator moves them first.
    /// </remarks>
    /// <param name="branchId">The branch.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<int> CountDependentAccountsAsync(Guid branchId, CancellationToken cancellationToken = default);

    /// <summary>Adds a newly opened branch.</summary>
    /// <param name="branch">The branch.</param>
    void Add(Branch branch);

    /// <summary>Reads a branch's concurrency token, for the <c>ETag</c> an edit is made against.</summary>
    /// <param name="branch">A branch this store loaded or added.</param>
    EntityTag EntityTagOf(Branch branch);

    /// <summary>Commits, reporting a lost race rather than throwing.</summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<Result> TrySaveChangesAsync(CancellationToken cancellationToken = default);
}
