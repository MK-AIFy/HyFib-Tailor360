using Microsoft.EntityFrameworkCore;
using Tailor360.Modules.Identity.Application.Abstractions;
using Tailor360.Modules.Identity.Domain;
using Tailor360.Modules.Identity.Domain.Branches;
using Tailor360.Platform.Abstractions.Concurrency;
using Tailor360.Platform.Abstractions.Results;
using Tailor360.Platform.Persistence.Concurrency;

namespace Tailor360.Modules.Identity.Infrastructure.Persistence;

/// <summary>The branch register over the <c>identity</c> schema.</summary>
/// <param name="context">The module's context.</param>
public sealed class BranchStore(IdentityDbContext context) : IBranchStore
{
    /// <inheritdoc />
    public async Task<IReadOnlyList<Branch>> ListAsync(
        Guid organisationId,
        CancellationToken cancellationToken = default)
        => await context.Branches
            .Where(branch => branch.OrganisationId == organisationId)
            .OrderBy(branch => branch.Code)
            .ToListAsync(cancellationToken);

    /// <inheritdoc />
    public Task<Branch?> FindAsync(Guid branchId, CancellationToken cancellationToken = default)
        => context.Branches.FirstOrDefaultAsync(branch => branch.Id == branchId, cancellationToken);

    /// <inheritdoc />
    public Task<bool> IsCodeTakenAsync(
        Guid organisationId,
        string code,
        CancellationToken cancellationToken = default)
        => context.Branches.AsNoTracking().AnyAsync(
            branch => branch.OrganisationId == organisationId && branch.Code == code, cancellationToken);

    /// <inheritdoc />
    public Task<int> CountDependentAccountsAsync(
        Guid branchId,
        CancellationToken cancellationToken = default)
        => context.Users
            .AsNoTracking()
            .CountAsync(
                user => user.Status != Domain.Users.UserStatus.Deactivated
                        && (user.HomeBranchId == branchId
                            || context.UserBranchAssignments.Any(
                                assignment => assignment.UserId == user.Id
                                              && assignment.BranchId == branchId)),
                cancellationToken);

    /// <inheritdoc />
    public void Add(Branch branch) => context.Branches.Add(branch);

    /// <inheritdoc />
    public EntityTag EntityTagOf(Branch branch) => context.EntityTagOf(branch);

    /// <inheritdoc />
    public async Task<Result> TrySaveChangesAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await context.SaveChangesAsync(cancellationToken);
            return Result.Success();
        }
        catch (DbUpdateConcurrencyException)
        {
            return Result.Failure(IdentityErrors.ConcurrentChange);
        }
    }
}
