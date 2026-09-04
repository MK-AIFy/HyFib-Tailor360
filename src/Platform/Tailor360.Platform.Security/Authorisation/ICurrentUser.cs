using Tailor360.Platform.Abstractions.Multitenancy;

namespace Tailor360.Platform.Security.Authorisation;

/// <summary>
/// The authenticated caller, resolved once per request from the session cookie. Application code
/// depends on this rather than on <c>HttpContext</c>, which keeps it testable and lets worker jobs
/// supply a system or operator principal through the same interface.
/// </summary>
public interface ICurrentUser
{
    /// <summary>True when a session was presented and accepted.</summary>
    bool IsAuthenticated { get; }

    /// <summary>The user identity, or <see cref="Guid.Empty"/> when unauthenticated.</summary>
    Guid UserId { get; }

    /// <summary>A stable identifier suitable for idempotency keys and audit entries.</summary>
    string PrincipalId { get; }

    /// <summary>The display name, used only in the user interface and in audit summaries.</summary>
    string DisplayName { get; }

    /// <summary>The organisation and the branch the caller is currently acting in.</summary>
    OrganisationContext Context { get; }

    /// <summary>The branches the caller is assigned to.</summary>
    IReadOnlySet<Guid> AssignedBranches { get; }

    /// <summary>The permission keys the caller holds, resolved from their roles.</summary>
    IReadOnlySet<string> Permissions { get; }

    /// <summary>True when the current session completed a multi-factor challenge.</summary>
    bool MfaSatisfied { get; }

    /// <summary>When the caller last re-authenticated, used to evaluate step-up freshness.</summary>
    DateTimeOffset? LastReauthenticatedAt { get; }

    /// <summary>True when the caller holds the named permission.</summary>
    bool HasPermission(string permissionKey);

    /// <summary>True when the caller may act on data owned by <paramref name="branchId"/>.</summary>
    bool CanActInBranch(Guid branchId);
}
