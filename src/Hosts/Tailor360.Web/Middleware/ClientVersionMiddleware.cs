using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using Tailor360.Platform.Abstractions.Versioning;
using Tailor360.Platform.Security.Endpoints;
using Tailor360.Web.Configuration;
using Tailor360.Web.Endpoints;

namespace Tailor360.Web.Middleware;

/// <summary>
/// Refuses a client build older than the minimum this server supports, with <c>426 Upgrade Required</c>
/// and the two versions the client needs to explain itself to the person using it.
/// </summary>
/// <remarks>
/// <para>
/// The alternative to refusing is worse than it looks. A client three releases old does not fail
/// visibly: it sends a field the server stopped reading, or omits one the server now requires, and the
/// person is told their input is invalid. The handshake turns that into one answer the client knows how
/// to act on — <c>docs/architecture/conventions.md</c> section 5.4.
/// </para>
/// <para>
/// <b>What it deliberately does not refuse.</b> A request with no <c>X-Client-Version</c> at all passes:
/// the health probes, the reverse proxy and the command-line tool are not the progressive web
/// application and have no build to compare. A value that does not parse passes for the reason given on
/// <see cref="ClientVersion"/>. Neither is a hole — the header is a courtesy from a cooperating client,
/// not an authorisation control, and anything that wanted to dodge the check could simply omit it. What
/// the check buys is that an honest client is told to update instead of being allowed to misbehave.
/// </para>
/// <para>
/// <c>GET /api/version</c> is exempt, and that exemption is the whole design. It is where the client
/// reads <c>minimumClient</c> from, so refusing it would leave a stale client unable to discover why it
/// is being refused — the update prompt would have nothing to say.
/// </para>
/// </remarks>
/// <param name="next">The next middleware.</param>
public sealed class ClientVersionMiddleware(RequestDelegate next)
{
    /// <summary>The header a client declares its build in.</summary>
    public const string HeaderName = "X-Client-Version";

    /// <summary>The problem code a refused client branches on.</summary>
    public const string ErrorCode = "client.upgrade-required";

    /// <summary>Runs the middleware.</summary>
    public async Task InvokeAsync(HttpContext context, IOptions<ClientCompatibilityOptions> options)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(options);

        if (IsRefused(context, options.Value.MinimumClientVersion, out var minimum))
        {
            var problem = ProblemResults.From(
                context,
                StatusCodes.Status426UpgradeRequired,
                ErrorCode,
                "A newer version of the application is required",
                "This device is running a build this server no longer supports. Reload the page to "
                + "collect the current one; anything typed but not yet saved should be noted down first.",

                // Reloading is what fixes it, not repeating the request, so the client must not retry.
                retryable: false,
                extensions: new Dictionary<string, object?>(StringComparer.Ordinal)
                {
                    ["minimumClient"] = minimum,
                    ["current"] = BuildInformation.Version,
                });

            await problem.ExecuteAsync(context);
            return;
        }

        await next(context);
    }

    /// <summary>
    /// Whether this request is from a client too old to answer. Separated from the middleware so the
    /// decision can be tested without a pipeline, and so the reasons to pass read as one list.
    /// </summary>
    /// <param name="context">The request.</param>
    /// <param name="configuredMinimum">The configured minimum supported client.</param>
    /// <param name="minimum">The parsed minimum, when one is configured.</param>
    internal static bool IsRefused(HttpContext context, string configuredMinimum, out string minimum)
    {
        minimum = configuredMinimum;

        // No minimum configured: nothing has been released that anything could be older than.
        if (!ClientVersion.TryParse(configuredMinimum, out var floor))
        {
            return false;
        }

        minimum = floor.ToString();

        // The endpoint that carries the answer is never refused by it.
        if (context.Request.Path.StartsWithSegments(VersionEndpoints.Path, StringComparison.Ordinal))
        {
            return false;
        }

        var declared = context.Request.Headers[HeaderName].ToString();
        return ClientVersion.TryParse(declared, out var client) && client < floor;
    }
}

/// <summary>Places the client-version handshake in the request pipeline.</summary>
public static class ClientVersionApplicationBuilderExtensions
{
    /// <summary>
    /// Refuses an unsupported client build. Placed after routing so that the refusal is logged against
    /// the endpoint it was aimed at, and before authentication so that a client too old to be answered
    /// is not first charged a session lookup.
    /// </summary>
    /// <param name="app">The application builder.</param>
    public static IApplicationBuilder UseTailor360ClientVersion(this IApplicationBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);
        return app.UseMiddleware<ClientVersionMiddleware>();
    }
}
