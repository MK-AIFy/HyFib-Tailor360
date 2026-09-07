using Tailor360.Platform.Abstractions.Multitenancy;
using Tailor360.Platform.Security.Authentication;

namespace Tailor360.Platform.Security.Authorisation;

/// <summary>
/// The caller, read from the session the authentication handler resolved for this request. When no
/// session was resolved it answers exactly as <see cref="AnonymousCurrentUser"/> does: nobody, holding
/// nothing, assigned to no branch.
/// </summary>
/// <remarks>
/// Everything here comes from the ticket, which was rebuilt from the database on this request. That is
/// what makes a permission removed a minute ago take effect now rather than at the holder's next
/// sign-in, and it is why there is no caching layer between this type and the session row.
/// </remarks>
/// <param name="session">The session resolved for this request.</param>
public sealed class SessionCurrentUser(SessionContext session) : ICurrentUser
{
    private static readonly IReadOnlySet<Guid> NoBranches = new HashSet<Guid>();
    private static readonly IReadOnlySet<string> NoPermissions = new HashSet<string>(StringComparer.Ordinal);
    private static readonly OrganisationContext NoOrganisation = new(Guid.Empty, null);

    /// <summary>The identifier used for an unauthenticated caller, matching the anonymous principal.</summary>
    public const string AnonymousPrincipalId = "anonymous";

    /// <inheritdoc />
    public bool IsAuthenticated => session.Ticket is not null;

    /// <inheritdoc />
    public Guid UserId => session.Ticket?.UserId ?? Guid.Empty;

    /// <summary>
    /// The account identifier, not the session identifier. Idempotency records key on it, so a command
    /// retried after a session rotation is recognised as the same command by the same person.
    /// </summary>
    public string PrincipalId
        => session.Ticket is { } ticket ? ticket.UserId.ToString("n") : AnonymousPrincipalId;

    /// <inheritdoc />
    public string DisplayName => session.Ticket?.DisplayName ?? "Anonymous";

    /// <inheritdoc />
    public OrganisationContext Context
        => session.Ticket is { } ticket
            ? new OrganisationContext(ticket.OrganisationId, ticket.ActiveBranchId)
            : NoOrganisation;

    /// <inheritdoc />
    public IReadOnlySet<Guid> AssignedBranches => session.Ticket?.AssignedBranches ?? NoBranches;

    /// <inheritdoc />
    public IReadOnlySet<string> Permissions => session.Ticket?.Permissions ?? NoPermissions;

    /// <inheritdoc />
    public bool MfaSatisfied => session.Ticket?.MfaSatisfied ?? false;

    /// <inheritdoc />
    public bool IsSignInComplete => session.Ticket?.SignInComplete ?? false;

    /// <inheritdoc />
    public DateTimeOffset? LastReauthenticatedAt => session.Ticket?.LastStrongAuthenticationAt;

    /// <inheritdoc />
    public bool HasPermission(string permissionKey)
        => !string.IsNullOrEmpty(permissionKey) && Permissions.Contains(permissionKey);

    /// <inheritdoc />
    public bool CanActInBranch(Guid branchId)
        => branchId != Guid.Empty && AssignedBranches.Contains(branchId);
}
