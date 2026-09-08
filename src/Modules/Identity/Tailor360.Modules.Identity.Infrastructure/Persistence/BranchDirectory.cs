using Microsoft.EntityFrameworkCore;
using Tailor360.Modules.Identity.Contracts.Directory;
using Tailor360.Modules.Identity.Domain.Branches;

namespace Tailor360.Modules.Identity.Infrastructure.Persistence;

/// <summary>
/// The published branch directory, over the <c>identity</c> schema.
/// </summary>
/// <remarks>
/// Projected in the database rather than by loading the aggregate: a caller that wants a branch code
/// to build one customer number must not fetch that branch's address, contacts and GST registration
/// to get it.
/// </remarks>
/// <param name="context">The module's context.</param>
public sealed class BranchDirectory(IdentityDbContext context) : IBranchDirectory
{
    /// <inheritdoc />
    public async Task<BranchSummary?> FindAsync(
        Guid branchId,
        CancellationToken cancellationToken = default)
    {
        var found = await ProjectAsync(branch => branch.Id == branchId, cancellationToken);

        return found.Count == 0 ? null : found[0];
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<BranchSummary>> FindManyAsync(
        IReadOnlyCollection<Guid> branchIds,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(branchIds);

        if (branchIds.Count == 0)
        {
            return [];
        }

        var wanted = branchIds.ToHashSet();

        return await ProjectAsync(branch => wanted.Contains(branch.Id), cancellationToken);
    }

    private async Task<List<BranchSummary>> ProjectAsync(
        System.Linq.Expressions.Expression<Func<Branch, bool>> predicate,
        CancellationToken cancellationToken)
        => await context.Branches
            .AsNoTracking()
            .Where(predicate)
            .Select(branch => new BranchSummary(
                branch.Id,
                branch.Code,
                branch.Name,
                branch.TimeZoneId,
                branch.Status == BranchStatus.Active))
            .ToListAsync(cancellationToken);
}
