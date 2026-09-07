namespace Tailor360.Platform.Security.Authentication;

/// <summary>
/// Everything the request pipeline learns from a session cookie. It is assembled by the module that
/// owns the session table and handed to the authentication handler; the cookie itself carries none of
/// it, which is the point of a server-side session.
/// </summary>
/// <remarks>
/// Nothing here is signed or encrypted for the client, because none of it ever reaches the client.
/// A token that carried claims would have to be trusted for as long as it was valid; this record is
/// rebuilt from the database on every request, so a change to the holder's permissions, branches or
/// account status takes effect on the next request rather than at the next sign-in.
/// </remarks>
/// <param name="SessionId">Identity of the session row. Never the cookie value.</param>
/// <param name="UserId">The account the session belongs to.</param>
/// <param name="DisplayName">The name shown in the interface and in audit summaries.</param>
/// <param name="OrganisationId">The organisation the session is working in.</param>
/// <param name="ActiveBranchId">The branch the session is currently working in, when it has one.</param>
/// <param name="AssignedBranches">The branches the holder may act in.</param>
/// <param name="Permissions">The permission keys the holder's roles grant.</param>
/// <param name="MfaSatisfied">True once a second factor has been satisfied on this session.</param>
/// <param name="SignInComplete">
/// True when the holder has finished signing in under this session. A session that answered only the
/// first factor is authenticated and <em>not</em> complete: it may finish the sign-in and it may end
/// itself, and it reaches nothing else. The module that owns the session table decides this, because
/// only it knows what the sign-in still owed.
/// </param>
/// <param name="LastStrongAuthenticationAt">When the holder last proved a strong factor, for step-up freshness.</param>
/// <param name="IdleExpiresAt">When the session ends if nothing further uses it.</param>
/// <param name="AbsoluteExpiresAt">When the session ends however active it is.</param>
public sealed record SessionTicket(
    Guid SessionId,
    Guid UserId,
    string DisplayName,
    Guid OrganisationId,
    Guid? ActiveBranchId,
    IReadOnlySet<Guid> AssignedBranches,
    IReadOnlySet<string> Permissions,
    bool MfaSatisfied,
    bool SignInComplete,
    DateTimeOffset? LastStrongAuthenticationAt,
    DateTimeOffset IdleExpiresAt,
    DateTimeOffset AbsoluteExpiresAt);
