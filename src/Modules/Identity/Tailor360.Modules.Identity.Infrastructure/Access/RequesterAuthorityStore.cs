using Microsoft.EntityFrameworkCore;
using Tailor360.Modules.Identity.Application.Access;
using Tailor360.Modules.Identity.Domain.Users;
using Tailor360.Modules.Identity.Infrastructure.Persistence;
using Tailor360.Platform.Security.Background;

namespace Tailor360.Modules.Identity.Infrastructure.Access;

/// <summary>
/// Answers the platform's requester-authority port from the <c>identity</c> schema, so that a
/// background job queued on somebody's behalf runs on the authority that person holds now.
/// </summary>
/// <remarks>
/// <para>
/// <b>Read at run time, never carried on the message.</b> The queued job carries the requester's
/// identifier and nothing else. Everything about what they may do — whether the account is still
/// usable, which branches it reaches, which permissions its roles grant — is resolved here, at the
/// moment the job runs. That is what makes a suspension or a revocation take effect on work queued
/// before it, which is the whole reason the port exists rather than a claim stamped into the message.
/// </para>
/// <para>
/// <b>Only an active account may be acted for.</b> Invited, suspended and deactivated all answer
/// <c>IsActive</c> false, and <c>WorkerScopeFactory</c> refuses the job. Invited is included on
/// purpose: an account that has never set a password has never proved it is held by anybody, so work
/// attributed to it is work attributed to nobody.
/// </para>
/// <para>
/// It is registered by <c>AddIdentityModule</c> and replaces the fail-closed default that
/// <c>AddTailor360WorkerScopes</c> registers. A host composed without this module keeps that default
/// and refuses every impersonated job, which is the right answer to a question it cannot answer.
/// </para>
/// </remarks>
/// <param name="context">The module's context.</param>
/// <param name="access">The caller's effective roles, permissions and branch assignments.</param>
public sealed class RequesterAuthorityStore(IdentityDbContext context, IUserAccessQuery access)
    : IRequesterAuthorityStore
{
    /// <inheritdoc />
    public async Task<RequesterAuthority?> FindAsync(
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        if (userId == Guid.Empty)
        {
            return null;
        }

        var account = await context.Users
            .AsNoTracking()
            .Where(user => user.Id == userId)
            .Select(user => new
            {
                user.DisplayName,
                user.OrganisationId,
                user.Status,
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (account is null)
        {
            return null;
        }

        // The same query the request pipeline reads, so a person reaches the same branches in a job as
        // they do in a request. Two answers to one question was a defect worth not repeating.
        var effective = await access.ResolveAsync(userId, cancellationToken);

        return new RequesterAuthority(
            userId,
            account.DisplayName,
            account.OrganisationId,
            account.Status == UserStatus.Active,
            effective.BranchIds,
            effective.Permissions);
    }
}
