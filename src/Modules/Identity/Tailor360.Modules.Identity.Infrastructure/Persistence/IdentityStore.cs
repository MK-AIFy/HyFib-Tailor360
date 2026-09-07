using Microsoft.EntityFrameworkCore;
using Tailor360.Modules.Identity.Application.Abstractions;
using Tailor360.Modules.Identity.Domain;
using Tailor360.Modules.Identity.Domain.Recovery;
using Tailor360.Modules.Identity.Domain.Users;
using Tailor360.Platform.Abstractions.Concurrency;
using Tailor360.Platform.Abstractions.Results;
using Tailor360.Platform.Persistence.Concurrency;

namespace Tailor360.Modules.Identity.Infrastructure.Persistence;

/// <summary>
/// The multi-factor and recovery handlers' view of the <c>identity</c> schema.
/// </summary>
/// <remarks>
/// Both user lookups load the whole aggregate. That is more rows than a given handler reads, and it is
/// deliberate: every rule these handlers apply — may this account enrol, does it still have a second
/// factor after a password reset, has this recovery code already been spent — is a question about the
/// aggregate rather than about one of its tables, and an aggregate loaded in pieces answers those
/// questions confidently and wrongly. An account has one password, one authenticator, a sheet of
/// codes and a handful of devices, so the whole of it is a small read.
/// <para>
/// Which tables that is comes from <see cref="IdentityAggregate"/> rather than being written out here.
/// The include graph <em>is</em> the definition of the whole aggregate, and a second copy of it is a
/// second thing to keep in step; the failure when they drift is a handler asking a partly-loaded
/// account whether it has a second factor and getting a confident wrong answer.
/// </para>
/// </remarks>
/// <param name="context">The module's context.</param>
public sealed class IdentityStore(IdentityDbContext context) : IIdentityStore
{
    /// <inheritdoc />
    public Task<StaffUser?> FindUserAsync(Guid userId, CancellationToken cancellationToken = default)
        => WholeAggregate().FirstOrDefaultAsync(user => user.Id == userId, cancellationToken);

    /// <inheritdoc />
    public Task<StaffUser?> FindUserByNormalisedEmailAsync(
        string normalisedEmail,
        CancellationToken cancellationToken = default)
        => WholeAggregate()
            .FirstOrDefaultAsync(user => user.NormalisedEmail == normalisedEmail, cancellationToken);

    /// <inheritdoc />
    public void AddRecoveryToken(RecoveryToken token) => context.RecoveryTokens.Add(token);

    /// <inheritdoc />
    public Task<RecoveryToken?> FindRecoveryTokenAsync(
        string tokenHash,
        CancellationToken cancellationToken = default)
        => context.RecoveryTokens
            .FirstOrDefaultAsync(token => token.TokenHash == tokenHash, cancellationToken);

    /// <inheritdoc />
    public async Task<int> InvalidateOutstandingRecoveryTokensAsync(
        Guid userId,
        RecoveryPurpose purpose,
        DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        // Loaded and invalidated through the domain rather than updated in place, because "invalidate
        // only what is neither spent nor already withdrawn" is the aggregate's rule and restating it as
        // a WHERE clause is how the two drift apart. An account has at most a few outstanding tokens.
        var outstanding = await context.RecoveryTokens
            .Where(token => token.UserId == userId
                            && token.Purpose == purpose
                            && token.ConsumedAt == null
                            && token.InvalidatedAt == null)
            .ToListAsync(cancellationToken);

        foreach (var token in outstanding)
        {
            token.Invalidate(now);
        }

        return outstanding.Count;
    }

    /// <inheritdoc />
    public EntityTag EntityTagOf(StaffUser user) => context.EntityTagOf(user);

    /// <inheritdoc />
    public Task SaveChangesAsync(CancellationToken cancellationToken = default)
        => context.SaveChangesAsync(cancellationToken);

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
            // The row moved under us. Every entity this store writes carries the xmin token, so the
            // update matched nothing and nothing was written — there is no partial state to undo, and
            // the caller's decision was made against a row that no longer says what it read.
            return Result.Failure(IdentityErrors.ConcurrentChange);
        }
    }

    private IQueryable<StaffUser> WholeAggregate() => IdentityAggregate.WholeAggregate(context);
}
