using Tailor360.Modules.Identity.Domain.Access;
using Tailor360.Platform.Abstractions.Concurrency;
using Tailor360.Platform.Abstractions.Results;

namespace Tailor360.Modules.Identity.Application.Abstractions;

/// <summary>The role register's reads and writes.</summary>
/// <remarks>
/// Separate from <see cref="IUserAssignmentStore"/>, which answers "which roles may this account be
/// given" and touches roles only as a list to choose from. This one is about the roles themselves: what
/// each grants, who holds it, and what happens when an administrator changes that.
/// </remarks>
public interface IRoleStore
{
    /// <summary>Every role in the organisation with its grants loaded, in key order.</summary>
    /// <param name="organisationId">The organisation.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<IReadOnlyList<Role>> ListAsync(
        Guid organisationId,
        CancellationToken cancellationToken = default);

    /// <summary>One role with its grants loaded, or null when there is none.</summary>
    /// <param name="roleId">The role.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<Role?> FindAsync(Guid roleId, CancellationToken cancellationToken = default);

    /// <summary>Whether a key is already in use in this organisation.</summary>
    /// <param name="organisationId">The organisation.</param>
    /// <param name="key">The key being claimed.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<bool> IsKeyTakenAsync(
        Guid organisationId,
        string key,
        CancellationToken cancellationToken = default);

    /// <summary>How many accounts hold each role in the organisation, keyed by role.</summary>
    /// <remarks>
    /// Asked once for the whole list rather than per role, because the administration screen shows the
    /// count beside every row and the alternative is a query per row.
    /// </remarks>
    /// <param name="organisationId">The organisation.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<IReadOnlyDictionary<Guid, int>> CountHoldersAsync(
        Guid organisationId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// How many active accounts would still hold <paramref name="permissionKey"/> if
    /// <paramref name="exceptRoleId"/> stopped granting it.
    /// </summary>
    /// <remarks>
    /// The question behind the lockout guard on role editing. Asked as "who would be left" rather than
    /// "who holds it now", because the role being edited is by definition still granting it at the
    /// moment the question is asked.
    /// </remarks>
    /// <param name="organisationId">The organisation.</param>
    /// <param name="permissionKey">The permission that must not be lost.</param>
    /// <param name="exceptRoleId">The role being edited, whose grants are ignored.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<int> CountHoldersThroughOtherRolesAsync(
        Guid organisationId,
        string permissionKey,
        Guid exceptRoleId,
        CancellationToken cancellationToken = default);

    /// <summary>The permission keys an account holds through its roles, right now.</summary>
    /// <remarks>
    /// Read for the granter rather than taken from their session claims. A session is minted at sign-in
    /// and a permission taken away since then would still be in it, so a check that trusted the claim
    /// would let somebody pass on authority they no longer have.
    /// </remarks>
    /// <param name="userId">The account.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<IReadOnlySet<string>> PermissionsOfAsync(
        Guid userId,
        CancellationToken cancellationToken = default);

    /// <summary>Adds a newly defined role.</summary>
    /// <param name="role">The role.</param>
    void Add(Role role);

    /// <summary>Removes a role and, by cascade, its grants.</summary>
    /// <param name="role">The role.</param>
    void Remove(Role role);

    /// <summary>Reads a role's concurrency token, for the <c>ETag</c> an edit is made against.</summary>
    /// <param name="role">A role this store loaded or added.</param>
    EntityTag EntityTagOf(Role role);

    /// <summary>
    /// Marks the role row as changed even when only its grants moved.
    /// </summary>
    /// <remarks>
    /// The grants live in their own table and carry no token. Without this, replacing a role's
    /// permissions would leave the role row untouched, its version unchanged, and two administrators
    /// editing the same role at the same moment would both be told they had won. Touching the row is
    /// what makes them contend for something.
    /// </remarks>
    /// <param name="role">The role being edited.</param>
    void Touch(Role role);

    /// <summary>Commits, reporting a lost race rather than throwing.</summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<Result> TrySaveChangesAsync(CancellationToken cancellationToken = default);
}
