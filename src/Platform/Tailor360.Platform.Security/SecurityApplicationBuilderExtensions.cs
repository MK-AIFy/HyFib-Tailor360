using Microsoft.AspNetCore.Builder;
using Tailor360.Platform.Security.Antiforgery;

namespace Tailor360.Platform.Security;

/// <summary>The two request-pipeline steps that make the cookie session safe to use from a browser.</summary>
/// <remarks>
/// They are separate calls rather than one because they belong at different points in the pipeline and
/// the order is the design, not an accident.
/// <list type="number">
/// <item><description>
/// The origin check goes early — before rate limiting and before authentication — so that a cross-site
/// attempt is refused on a header comparison and never reaches the session lookup or consumes a
/// rate-limit permit that belongs to the person being attacked.
/// </description></item>
/// <item><description>
/// Anti-forgery validation goes after authentication, because the request token is bound to the signed-in
/// account: validating before the principal exists would compare it against an anonymous caller and
/// every authenticated request would fail.
/// </description></item>
/// </list>
/// </remarks>
public static class SecurityApplicationBuilderExtensions
{
    /// <summary>
    /// Refuses state-changing requests a browser reports as coming from another site. Place immediately
    /// after <c>UseRouting</c>.
    /// </summary>
    public static IApplicationBuilder UseTailor360OriginChecks(this IApplicationBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);
        return app.UseMiddleware<OriginValidationMiddleware>();
    }

    /// <summary>
    /// Requires an anti-forgery token on every state-changing request, sign-in included. Place after
    /// <c>UseAuthentication</c> and before <c>UseAuthorization</c>.
    /// </summary>
    public static IApplicationBuilder UseTailor360Antiforgery(this IApplicationBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);
        return app.UseMiddleware<AntiforgeryEnforcementMiddleware>();
    }
}
