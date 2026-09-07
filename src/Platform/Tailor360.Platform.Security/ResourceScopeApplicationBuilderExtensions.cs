using Microsoft.AspNetCore.Builder;
using Tailor360.Platform.Security.Authorisation;

namespace Tailor360.Platform.Security;

/// <summary>Places the resource resolution step in the request pipeline.</summary>
/// <remarks>
/// It belongs between <c>UseRouting</c> and <c>UseAuthorization</c>, and nowhere else. Earlier there is
/// no endpoint and no route value to read; later the authorisation middleware has already answered. A
/// host that omits it does not quietly skip the resource check — every endpoint that demands one refuses
/// every caller, and the refusal is logged as the defect it is.
/// </remarks>
public static class ResourceScopeApplicationBuilderExtensions
{
    /// <summary>
    /// Loads the resource an endpoint names, so that authorisation can be decided against it. Place
    /// after <c>UseRouting</c> and immediately before <c>UseAuthorization</c>.
    /// </summary>
    public static IApplicationBuilder UseTailor360ResourceScope(this IApplicationBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);
        return app.UseMiddleware<ResourceScopeResolutionMiddleware>();
    }
}
