namespace Tailor360.Modules.Identity.Api;

/// <summary>
/// Where this module's endpoints live.
/// </summary>
/// <remarks>
/// Authentication does not sit under <c>/api/v1/identity</c>, and that is deliberate rather than an
/// inconsistency. <c>/api/v1/auth/login</c>, <c>/api/v1/me</c> and <c>/api/v1/sessions</c> are about
/// the caller's relationship with the application, not about a resource the Identity module owns;
/// naming the module in the path would make the client's most basic calls depend on which module
/// happens to implement them today. <c>/api/v1/identity</c> stays for the administrative surface
/// #25 adds, which really is about the module's resources.
/// </remarks>
public static class IdentityRoutes
{
    /// <summary>Signing in, answering a challenge, passkeys, signing out and recovery.</summary>
    public const string Auth = "/api/v1/auth";

    /// <summary>The caller's own account, preferences and session facts.</summary>
    public const string Me = "/api/v1/me";

    /// <summary>The caller's own session and device inventory.</summary>
    public const string Sessions = "/api/v1/sessions";

    /// <summary>The OpenAPI tag applied to the authentication surface.</summary>
    public const string AuthTag = "Authentication";

    /// <summary>The OpenAPI tag applied to the session inventory.</summary>
    public const string SessionTag = "Sessions";
}
