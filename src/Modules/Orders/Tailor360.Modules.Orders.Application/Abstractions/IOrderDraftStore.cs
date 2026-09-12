using Tailor360.Modules.Orders.Domain.Drafts;
using Tailor360.Platform.Abstractions.Concurrency;
using Tailor360.Platform.Abstractions.Results;

namespace Tailor360.Modules.Orders.Application.Abstractions;

/// <summary>
/// Reads and writes the order draft a counter builds before anything is committed to.
/// </summary>
/// <remarks>
/// <para>
/// The module's internal port, so it speaks in domain types — a published contract may not. It exists so a
/// command can be tested without a database, and so the one place that knows how the draft, its garment sections
/// and their dependencies are loaded stays in Infrastructure.
/// </para>
/// <para>
/// <strong>Every read is scoped by organisation and none is scoped by branch.</strong> The organisation is the
/// tenant boundary and is never the caller's to assert past; the branch is the endpoint's decision, evaluated
/// server-side against the draft's own <c>BranchId</c> (security rule 2), and a store that quietly returned null
/// for another branch's draft would make "not yours" and "not there" indistinguishable to the caller and
/// invisible to the audit trail.
/// </para>
/// </remarks>
public interface IOrderDraftStore
{
    /// <summary>
    /// Loads one draft for change, with its garment sections and their declared dependencies, or null when no
    /// draft has that identity within the organisation.
    /// </summary>
    /// <param name="orderDraftId">The draft.</param>
    /// <param name="organisationId">The tenant the caller is acting within.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The draft, or null.</returns>
    Task<OrderDraft?> FindAsync(
        Guid orderDraftId,
        Guid organisationId,
        CancellationToken cancellationToken = default);

    /// <summary>Adds a newly started draft to the unit of work.</summary>
    /// <param name="draft">The draft.</param>
    void Add(OrderDraft draft);

    /// <summary>The concurrency token an edit must be made against.</summary>
    /// <param name="draft">A tracked draft.</param>
    /// <returns>The entity tag.</returns>
    EntityTag EntityTagOf(OrderDraft draft);

    /// <summary>
    /// Commits, turning the failures a second counter can cause into results rather than exceptions.
    /// </summary>
    /// <remarks>
    /// Answers <c>OrdersErrors.ConcurrentChange</c> when the row moved between the read and the write,
    /// and <c>OrdersErrors.GarmentAlreadyOnDraft</c> when a garment identity was added twice.
    /// </remarks>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>Success, or the reason the write was refused.</returns>
    Task<Result> SaveAsync(CancellationToken cancellationToken = default);
}
