using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Tailor360.Modules.Reporting.Api;

/// <summary>
/// The Reporting module's HTTP surface, covering read models, projections, reconciliation, scheduled reports and governed exports.
/// Every endpoint added here must declare an authorisation policy through
/// <c>RequirePermission</c> or an explicit anonymous justification (architecture rule ARCH-007).
/// </summary>
public static class ReportingEndpoints
{
    /// <summary>The route prefix for this module. The major API version is part of the path (D18).</summary>
    public const string GroupPrefix = "/api/v1/reporting";

    /// <summary>The OpenAPI tag applied to this module's operations.</summary>
    public const string OpenApiTag = "Reporting";

    /// <summary>Maps the module's endpoint group.</summary>
    /// <param name="endpoints">The route builder supplied by the host.</param>
    public static IEndpointRouteBuilder MapReportingEndpoints(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        _ = endpoints.MapGroup(GroupPrefix)
            .WithTags(OpenApiTag);

        return endpoints;
    }
}
