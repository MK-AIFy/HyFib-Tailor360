namespace Tailor360.Platform.Abstractions.Ports;

/// <summary>
/// The only sanctioned way to call an external HTTP endpoint. Centralising outbound calls is what makes
/// the server-side request forgery policy enforceable: the implementation resolves the host, rejects
/// private, loopback, link-local and metadata addresses, pins the allowed scheme and port, caps the
/// response size and applies a timeout and retry budget. Architecture rule ARCH-016 forbids constructing
/// <c>HttpClient</c> directly outside this abstraction.
/// </summary>
public interface IOutboundHttp
{
    /// <summary>
    /// Sends a request to a destination that has been registered in the outbound policy under
    /// <paramref name="policyName"/>. Unregistered destinations are rejected before any socket is opened.
    /// </summary>
    Task<Results.Result<OutboundHttpResponse>> SendAsync(
        string policyName,
        HttpRequestMessage request,
        CancellationToken cancellationToken = default);
}

/// <summary>A bounded response from an external call.</summary>
/// <param name="StatusCode">The HTTP status code returned.</param>
/// <param name="Body">The response body, truncated to the policy's maximum size.</param>
/// <param name="Truncated">True when the body exceeded the policy's maximum size.</param>
public sealed record OutboundHttpResponse(int StatusCode, string Body, bool Truncated);
