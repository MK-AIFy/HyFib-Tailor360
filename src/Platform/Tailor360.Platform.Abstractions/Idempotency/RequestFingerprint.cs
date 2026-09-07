using System.Security.Cryptography;
using System.Text;

namespace Tailor360.Platform.Abstractions.Idempotency;

/// <summary>
/// The hash an idempotency record stores so that a client reusing one key for two different commands is
/// caught rather than answered with the first command's result.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why the path is part of it and not only the body.</b> A record is keyed by the route
/// <em>template</em>, because that is what a client can be told to send and what an operator can read.
/// Two confirmations of two different orders therefore share a key prefix: <c>POST
/// /api/v1/orders/{orderId}/confirm</c> is one route, and a client that sent one key for both would have
/// its second order answered with the first order's stored response and never confirmed. Hashing the
/// resolved path closes that: the second request has a different fingerprint, so it is refused as a
/// reused key instead of silently swallowed.
/// </para>
/// <para>
/// <b>Only the hash is ever stored.</b> Request bodies carry measurements, names and phone numbers, and
/// none of that may sit in a platform table for a week (<c>docs/nfr/data-classification.md</c>). A
/// SHA-256 over the bytes answers the only question the record needs to ask — "is this the same request
/// I already have?" — and answers nothing else.
/// </para>
/// </remarks>
public static class RequestFingerprint
{
    /// <summary>
    /// Fingerprints a request from the parts that decide what it does: the method, the resolved path,
    /// the query string and the body bytes.
    /// </summary>
    /// <param name="method">The HTTP method, for example <c>POST</c>.</param>
    /// <param name="path">The resolved request path, with route parameters substituted.</param>
    /// <param name="queryString">The query string, including its leading <c>?</c>, or empty.</param>
    /// <param name="body">The request body bytes, empty when there is no body.</param>
    public static string Of(string method, string path, string queryString, ReadOnlySpan<byte> body)
    {
        ArgumentNullException.ThrowIfNull(method);
        ArgumentNullException.ThrowIfNull(path);
        ArgumentNullException.ThrowIfNull(queryString);

        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);

        // The separator is a byte that cannot appear in any of the parts, so that a path ending where a
        // query begins cannot be confused with a different split of the same characters.
        AppendPart(hash, method.ToUpperInvariant());
        AppendPart(hash, path);
        AppendPart(hash, queryString);
        hash.AppendData(body);

        return Convert.ToHexStringLower(hash.GetHashAndReset());
    }

    /// <summary>Fingerprints a request body on its own, for callers that have nothing else to bind to.</summary>
    /// <param name="requestBody">The body as text; null is treated as empty.</param>
    public static string OfBody(string? requestBody)
        => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(requestBody ?? string.Empty)));

    private static void AppendPart(IncrementalHash hash, string value)
    {
        hash.AppendData(Encoding.UTF8.GetBytes(value));
        hash.AppendData([0x00]);
    }
}
