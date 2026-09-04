using System.Globalization;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Tailor360.Platform.Security.Authorisation;

namespace Tailor360.Web.Configuration;

/// <summary>
/// The named rate-limit policies endpoints choose from. One catalogue rather than per-endpoint numbers
/// keeps the limits reviewable, and the partition key is the authenticated user where there is one so
/// that a whole shop behind a single broadband connection is not throttled as one caller.
/// </summary>
public static class RateLimitPolicies
{
    /// <summary>Sign-in, recovery and other credential endpoints. Deliberately strict.</summary>
    public const string Authentication = "auth";

    /// <summary>Ordinary reads.</summary>
    public const string Read = "read";

    /// <summary>State-changing requests.</summary>
    public const string Write = "write";

    /// <summary>
    /// Barcode scanning. The shop floor scans in bursts as a rack is worked through, so the limit is
    /// high and short-windowed rather than low and long-windowed.
    /// </summary>
    public const string Scan = "scan";

    /// <summary>Report and export generation, which is expensive per call.</summary>
    public const string Export = "export";

    /// <summary>Registers every policy and the shared rejection behaviour.</summary>
    public static RateLimiterOptions AddTailor360Policies(this RateLimiterOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
        options.OnRejected = static (context, cancellationToken) =>
        {
            if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
            {
                context.HttpContext.Response.Headers.RetryAfter =
                    ((int)retryAfter.TotalSeconds).ToString(CultureInfo.InvariantCulture);
            }

            return ValueTask.CompletedTask;
        };

        Add(options, Authentication, permitLimit: 10, window: TimeSpan.FromMinutes(1), partitionByUser: false);
        Add(options, Read, permitLimit: 300, window: TimeSpan.FromMinutes(1), partitionByUser: true);
        Add(options, Write, permitLimit: 120, window: TimeSpan.FromMinutes(1), partitionByUser: true);
        Add(options, Scan, permitLimit: 240, window: TimeSpan.FromMinutes(1), partitionByUser: true);
        Add(options, Export, permitLimit: 10, window: TimeSpan.FromMinutes(5), partitionByUser: true);

        return options;
    }

    private static void Add(
        RateLimiterOptions options,
        string policyName,
        int permitLimit,
        TimeSpan window,
        bool partitionByUser)
    {
        options.AddPolicy(policyName, context => RateLimitPartition.GetFixedWindowLimiter(
            PartitionKey(context, policyName, partitionByUser),
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = permitLimit,
                Window = window,
                QueueLimit = 0,
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
            }));
    }

    private static string PartitionKey(HttpContext context, string policyName, bool partitionByUser)
    {
        if (partitionByUser)
        {
            var user = context.RequestServices.GetService(typeof(ICurrentUser)) as ICurrentUser;
            if (user is { IsAuthenticated: true })
            {
                return $"{policyName}:u:{user.PrincipalId}";
            }
        }

        // Falls back to the remote address, which the forwarded-headers configuration has already
        // resolved through the trusted reverse proxy only.
        return $"{policyName}:ip:{context.Connection.RemoteIpAddress}";
    }
}
