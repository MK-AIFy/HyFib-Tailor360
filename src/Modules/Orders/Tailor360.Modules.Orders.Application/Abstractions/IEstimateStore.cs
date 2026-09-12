using Tailor360.Modules.Orders.Domain.DisplayNumbers;
using Tailor360.Modules.Orders.Domain.Estimates;
using Tailor360.Platform.Abstractions.Concurrency;
using Tailor360.Platform.Abstractions.Results;

namespace Tailor360.Modules.Orders.Application.Abstractions;

/// <summary>
/// Reads and writes the priced estimates issued against a draft.
/// </summary>
/// <remarks>
/// <para>
/// The module's internal port, and separate from <see cref="IOrderDraftStore"/> because the two records have
/// different lifetimes: a draft expires and is swept, while an estimate is a commercial document that was handed
/// to a customer and stays readable for as long as the order it became does.
/// </para>
/// <para>
/// Reads are scoped by organisation and not by branch, for the reason <see cref="IOrderDraftStore"/> gives.
/// </para>
/// </remarks>
public interface IEstimateStore
{
    /// <summary>Loads one estimate for change, or null when none has that identity within the organisation.</summary>
    /// <param name="estimateId">The estimate.</param>
    /// <param name="organisationId">The tenant the caller is acting within.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The estimate, or null.</returns>
    Task<Estimate?> FindAsync(
        Guid estimateId,
        Guid organisationId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Every estimate issued against one draft, most recently issued first.
    /// </summary>
    /// <remarks>
    /// What the supersede path reads: re-pricing a draft issues a new estimate and supersedes the standing one,
    /// and the counter's screen shows the customer which quotations they have already been given.
    /// </remarks>
    /// <param name="orderDraftId">The draft the estimates were priced from.</param>
    /// <param name="organisationId">The tenant the caller is acting within.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The estimates, which may be none.</returns>
    Task<IReadOnlyList<Estimate>> ListForDraftAsync(
        Guid orderDraftId,
        Guid organisationId,
        CancellationToken cancellationToken = default);

    /// <summary>Adds a newly issued estimate to the unit of work.</summary>
    /// <param name="estimate">The estimate.</param>
    void Add(Estimate estimate);

    /// <summary>The concurrency token an edit must be made against.</summary>
    /// <param name="estimate">A tracked estimate.</param>
    /// <returns>The entity tag.</returns>
    EntityTag EntityTagOf(Estimate estimate);

    /// <summary>
    /// Allocates the next position in the estimate sequence for a branch and financial year.
    /// </summary>
    /// <remarks>
    /// The position, not the number: composing it is <c>EstimateNumber.Create</c>'s job, in the Domain, where the
    /// format rule lives. A gap is acceptable in a display number and a reuse is not
    /// (<c>docs/architecture/conventions.md</c> section 3.2), so an estimate whose own save then fails simply
    /// leaves its position unused.
    /// </remarks>
    /// <param name="branchCode">The branch's short code, as Identity holds it.</param>
    /// <param name="financialYear">The financial year, evaluated in the branch timezone.</param>
    /// <param name="cancellationToken">Cancels the allocation.</param>
    /// <returns>The next one-based position within that branch and year.</returns>
    Task<long> NextEstimateSequenceAsync(
        string branchCode,
        FinancialYear financialYear,
        CancellationToken cancellationToken = default);

    /// <summary>Commits, turning a lost race into a result rather than an exception.</summary>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>Success, or <c>OrdersErrors.ConcurrentChange</c>.</returns>
    Task<Result> SaveAsync(CancellationToken cancellationToken = default);
}
