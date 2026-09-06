using Microsoft.Extensions.Logging;

namespace Tailor360.Platform.Security.Background;

/// <summary>
/// Reads what an account may do right now, for a job that was queued on that account's behalf.
/// </summary>
/// <remarks>
/// <para>
/// This is a platform port: the security library declares it, and the module that owns accounts
/// implements it. The permission model has to be readable from the worker, which has no request, no
/// session and no reason to know how accounts are stored — and the module that owns accounts must not
/// be reached into from a host, which is what a port avoids.
/// </para>
/// <para>
/// Nothing here is read from the queued job. The requester's identifier is all the job carries; the
/// roles, permissions and branches are resolved again at the moment the job runs, which is what makes
/// a revocation take effect on work that was queued before it.
/// </para>
/// </remarks>
public interface IRequesterAuthorityStore
{
    /// <summary>Reads an account's current authority, or null when no such account exists.</summary>
    /// <param name="userId">The account the job was queued by.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<RequesterAuthority?> FindAsync(Guid userId, CancellationToken cancellationToken = default);
}

/// <summary>
/// What one account may do at this moment: whether it may act at all, what it may act on, and where.
/// </summary>
/// <param name="UserId">The account.</param>
/// <param name="DisplayName">
/// The account's display name, recorded as the audit actor. It is personal data: it belongs in the
/// audit trail and never in a log line, a metric or a trace attribute.
/// </param>
/// <param name="OrganisationId">The organisation the account belongs to.</param>
/// <param name="IsActive">
/// False when the account is suspended, deactivated or not yet activated. A job acting for an inactive
/// account is refused whatever permissions the account still nominally carries.
/// </param>
/// <param name="AssignedBranches">The branches the account is assigned to now.</param>
/// <param name="Permissions">The permission keys the account's roles grant now.</param>
public sealed record RequesterAuthority(
    Guid UserId,
    string DisplayName,
    Guid OrganisationId,
    bool IsActive,
    IReadOnlySet<Guid> AssignedBranches,
    IReadOnlySet<string> Permissions);

/// <summary>
/// The fail-closed authority store used when no module has supplied a real one: no account is known,
/// so every job that would act for a requester is refused.
/// </summary>
/// <remarks>
/// A host composed without the module that owns accounts cannot answer "may this person still do
/// this?", and the only safe answer to a question that cannot be answered is no. Registering nothing
/// at all would instead throw on the first impersonated job, which reads in a deployment log as a
/// defect rather than as the refusal it is.
/// </remarks>
/// <param name="logger">Logger.</param>
public sealed class UnavailableRequesterAuthorityStore(
    ILogger<UnavailableRequesterAuthorityStore> logger) : IRequesterAuthorityStore
{
    /// <inheritdoc />
    public Task<RequesterAuthority?> FindAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        logger.LogError(
            "A background job asked for its requester's authority but no authority store is "
            + "registered. The host is composed without the module that owns accounts, so every job "
            + "acting for a requester is refused.");

        return Task.FromResult<RequesterAuthority?>(null);
    }
}
