namespace Tailor360.Modules.Identity.Api;

/// <summary>
/// Where this module's endpoints live.
/// </summary>
/// <remarks>
/// Authentication does not sit under <c>/api/v1/identity</c>, and that is deliberate rather than an
/// inconsistency. <c>/api/v1/auth/login</c>, <c>/api/v1/me</c> and <c>/api/v1/sessions</c> are about
/// the caller's relationship with the application, not about a resource the Identity module owns;
/// naming the module in the path would make the client's most basic calls depend on which module
/// happens to implement them today.
/// <para>
/// The administrative surface sits under <c>/api/v1/admin</c> for the same reason turned round. An
/// administrator's screens are one surface — accounts, branches, roles, feature flags, the audit
/// trail — and which module happens to own each of those is a fact about this codebase, not about the
/// job somebody is doing. Splitting them across <c>/identity</c> and <c>/platform</c> would publish our
/// internal boundary as the client's navigation. This is the endpoint list in
/// <c>docs/IMPLEMENTATION_PLAN.md</c> for #25; it is the module that owns each route's implementation,
/// and the module boundary is enforced where it means something, in the project graph.
/// </para>
/// </remarks>
public static class IdentityRoutes
{
    /// <summary>Signing in, answering a challenge, passkeys, signing out and recovery.</summary>
    public const string Auth = "/api/v1/auth";

    /// <summary>The caller's own account, preferences and session facts.</summary>
    public const string Me = "/api/v1/me";

    /// <summary>The caller's own session and device inventory.</summary>
    public const string Sessions = "/api/v1/sessions";

    /// <summary>Administering staff accounts.</summary>
    public const string AdminUsers = "/api/v1/admin/users";

    /// <summary>Administering the branch register.</summary>
    public const string AdminBranches = "/api/v1/admin/branches";

    /// <summary>The OpenAPI tag applied to the authentication surface.</summary>
    public const string AuthTag = "Authentication";

    /// <summary>The OpenAPI tag applied to the session inventory.</summary>
    public const string SessionTag = "Sessions";

    /// <summary>The OpenAPI tag applied to the administrative surface.</summary>
    public const string AdminTag = "Administration";
}
