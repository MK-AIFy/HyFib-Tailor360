using System.Globalization;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Tailor360.Platform.Security.Authorisation;
using Tailor360.Platform.Security.Endpoints;

namespace Tailor360.Web.Configuration;

/// <summary>
/// The numbers behind the rate-limit policy catalogue. The names live in
/// <see cref="RateLimitPolicyNames"/>, where a module's endpoints can reach them; the limits live here,
/// because how much traffic a deployment tolerates is the host's decision and not a module's.
/// </summary>
/// <remarks>
/// <para>
/// One catalogue rather than per-endpoint numbers keeps the limits reviewable as a set. Each policy
/// describes a <em>shape</em> of traffic — an unauthenticated credential attempt, an ordinary read, a
/// burst of scans — so an endpoint chooses the shape it is and inherits whatever the shape currently
/// costs.
/// </para>
/// <para>
/// <b>Authenticated policies key on the account, not the address.</b> A whole shop reaches this system
/// through one broadband connection, so an address is a dozen people; limiting them as one caller would
/// mean the busiest person on a Saturday throttles everybody else. The credential policies key on the
/// address instead, because before a session exists there is nothing else to key on — and that is
/// exactly the traffic where one address really is one caller worth bounding.
/// </para>
/// <para>
/// This is one of three controls and the coarsest. It bounds requests; it does not know a wrong
/// password from a right one. The per-account throttle in the Identity module and the progressive
/// lockout on the account are what bound guessing, and neither is replaceable by tuning these numbers.
/// </para>
/// </remarks>
public static class RateLimitPolicies
{
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

        // Sign-in and passkey assertion. Strict, and keyed on the address: nobody is signed in yet.
        Add(options, RateLimitPolicyNames.AuthenticationAnonymous, permits: 10, TimeSpan.FromMinutes(1), Partition.Address);

        // Answering a second factor. Keyed on the account, because the caller has already proved a
        // password and two people on one counter connection must not throttle each other.
        Add(options, RateLimitPolicyNames.MultiFactorChallenge, permits: 20, TimeSpan.FromMinutes(1), Partition.UserOrAddress);

        // Asking for or spending a recovery link. Tighter than sign-in, because each accepted request
        // may send a message to somebody who did not ask for it.
        Add(options, RateLimitPolicyNames.RecoveryAnonymous, permits: 5, TimeSpan.FromMinutes(15), Partition.Address);

        // Ordinary authenticated traffic. A progressive web application issues several requests per
        // interaction, so the read limit is generous by design.
        Add(options, RateLimitPolicyNames.DefaultUser, permits: 300, TimeSpan.FromMinutes(1), Partition.UserOrAddress);
        Add(options, RateLimitPolicyNames.Write, permits: 120, TimeSpan.FromMinutes(1), Partition.UserOrAddress);

        // The shop floor scans in bursts as a rack is worked through, so this is high and
        // short-windowed rather than low and long-windowed.
        Add(options, RateLimitPolicyNames.ScanBurst, permits: 240, TimeSpan.FromMinutes(1), Partition.UserOrAddress);

        // Report and export generation, which is expensive per call.
        Add(options, RateLimitPolicyNames.ExportHeavy, permits: 10, TimeSpan.FromMinutes(5), Partition.UserOrAddress);

        // Anonymous traffic that is not a credential endpoint — the anti-forgery token, the client
        // shell's own requests before anyone signs in.
        Add(options, RateLimitPolicyNames.DefaultIp, permits: 60, TimeSpan.FromMinutes(1), Partition.Address);

        return options;
    }

    private static void Add(
        RateLimiterOptions options,
        string policyName,
        int permits,
        TimeSpan window,
        Partition partition)
        => options.AddPolicy(policyName, context => RateLimitPartition.GetFixedWindowLimiter(
            PartitionKey(context, policyName, partition),
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = permits,
                Window = window,

                // Nothing queues. A caller past the limit is told so immediately with a Retry-After it
                // can obey; holding the request open instead would consume a connection to deliver the
                // same answer later.
                QueueLimit = 0,
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
            }));

    private static string PartitionKey(HttpContext context, string policyName, Partition partition)
    {
        if (partition is Partition.UserOrAddress
            && context.RequestServices.GetService(typeof(ICurrentUser)) is ICurrentUser { IsAuthenticated: true } user)
        {
            return $"{policyName}:u:{user.PrincipalId}";
        }

        // Falls back to the remote address, which the forwarded-headers configuration has already
        // resolved through the trusted reverse proxy only.
        return $"{policyName}:ip:{context.Connection.RemoteIpAddress}";
    }

    /// <summary>What a policy counts against.</summary>
    private enum Partition
    {
        /// <summary>The client address. The only key available before a session exists.</summary>
        Address = 0,

        /// <summary>The signed-in account, falling back to the address when there is none.</summary>
        UserOrAddress = 1,
    }
}
