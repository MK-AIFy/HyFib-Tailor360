using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Tailor360.Modules.Identity.Api.Authentication;
using Tailor360.Modules.Identity.Api.Me;
using Tailor360.Modules.Identity.Api.Recovery;
using Tailor360.Modules.Identity.Api.Sessions;

namespace Tailor360.Modules.Identity.Api;

/// <summary>
/// The Identity module's HTTP surface: signing in, second factors, passkeys, recovery, the caller's own
/// profile and their session inventory.
/// </summary>
/// <remarks>
/// Four groups are mapped from one extension method, which is what the host composes (architecture rule
/// ARCH-006). They are separate groups because they are separate resources — see
/// <see cref="IdentityRoutes"/> for why authentication does not live under the module's own prefix —
/// and one method because a host must not have to know how many of them there are.
/// <para>
/// Every endpoint added here declares either a permission, an anonymous justification, or — for the
/// handful that act on the caller's own account and could not sensibly be gated on a permission — a
/// recorded self-service exposure. ARCH-007 accepts the first and the third as an authorisation policy;
/// the reasoning behind the third is in <see cref="SelfServiceEndpointExtensions"/>.
/// </para>
/// </remarks>
public static class IdentityEndpoints
{
    /// <summary>The route prefix for this module's administrative surface, which #25 fills in.</summary>
    public const string GroupPrefix = "/api/v1/identity";

    /// <summary>The OpenAPI tag applied to this module's administrative operations.</summary>
    public const string OpenApiTag = "Identity";

    /// <summary>Maps the module's endpoint groups.</summary>
    /// <param name="endpoints">The route builder supplied by the host.</param>
    public static IEndpointRouteBuilder MapIdentityEndpoints(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        endpoints.MapGroup(IdentityRoutes.Auth)
            .WithTags(IdentityRoutes.AuthTag)
            .MapAuthenticationEndpoints()
            .MapMultiFactorEnrolmentEndpoints()
            .MapPasskeyEndpoints()
            .MapRecoveryEndpoints();

        endpoints.MapGroup(IdentityRoutes.Me)
            .WithTags(IdentityRoutes.AuthTag)
            .MapMeEndpoints();

        endpoints.MapGroup(IdentityRoutes.Sessions)
            .WithTags(IdentityRoutes.SessionTag)
            .MapSessionEndpoints();

        return endpoints;
    }
}
