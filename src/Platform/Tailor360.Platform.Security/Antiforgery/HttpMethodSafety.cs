using Microsoft.AspNetCore.Http;

namespace Tailor360.Platform.Security.Antiforgery;

/// <summary>
/// Which request methods change nothing and therefore need no cross-site defence. The list is the one
/// RFC 9110 calls safe, and it is deliberately a closed list: an unrecognised method is treated as
/// unsafe so that a new verb cannot arrive without the checks.
/// </summary>
public static class HttpMethodSafety
{
    /// <summary>True when the method is defined to be read-only and needs no anti-forgery token.</summary>
    public static bool IsSafe(string method)
        => HttpMethods.IsGet(method)
            || HttpMethods.IsHead(method)
            || HttpMethods.IsOptions(method)
            || HttpMethods.IsTrace(method);
}
