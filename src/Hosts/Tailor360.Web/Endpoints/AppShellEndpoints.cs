using System.Text;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.FileProviders;
using Tailor360.Web.Middleware;

namespace Tailor360.Web.Endpoints;

/// <summary>
/// Serves the progressive web application's shell, stamping each response's content-security-policy
/// nonce onto the scripts the shell loads.
/// </summary>
/// <remarks>
/// <para>
/// The shell used to be served by <c>MapFallbackToFile</c>, straight off disk. That cannot work under
/// the enforcing policy of #53: <c>script-src 'nonce-…' 'strict-dynamic'</c> executes the module script
/// only if it carries this response's nonce, and a static file is the same bytes for every response.
/// So the shell is read once, split at the tags that need the attribute, and rejoined per request
/// around a fresh nonce. Everything else the client loads — the JavaScript chunks, the stylesheet, the
/// icons — is still served by the static-file middleware, because those are loaded <em>by</em> the
/// nonced script and the policy admits them on that basis.
/// </para>
/// <para>
/// <b>Why the preload links are stamped too.</b> <c>'strict-dynamic'</c> does not extend to a
/// <c>&lt;link rel="modulepreload"&gt;</c> written in the markup: the preload is a document-initiated
/// request, so the browser checks it against <c>script-src</c> on its own and, with no host source left
/// in that directive, refuses it. Refusing a preload does not break the application — the module is
/// fetched again when it is imported — but it fills the console with violations and throws away the
/// warm-up the build emitted them for.
/// </para>
/// <para>
/// <b>The response is never stored.</b> A cached shell carries the nonce it was rendered with, while a
/// revalidation would answer with a fresh policy header; the two would disagree and every script on the
/// page would be refused. <c>Cache-Control: no-store</c> is what keeps the document and the header that
/// authorises it travelling together. The shell is two kilobytes and the assets it names are
/// content-hashed and cached for a year, so this costs one small request per navigation.
/// </para>
/// </remarks>
public static class AppShellEndpoints
{
    /// <summary>The file, relative to the web root, that holds the shell.</summary>
    public const string ShellFileName = "index.html";

    /// <summary>The canonical path the shell is served at.</summary>
    public const string CanonicalPath = "/";

    /// <summary>
    /// Rewrites an explicit request for <c>/index.html</c> to the canonical path, so that it is served
    /// by the shell endpoint rather than by the static-file middleware.
    /// </summary>
    /// <remarks>
    /// The service worker precaches the shell by name and answers navigations from that copy, so an
    /// un-stamped <c>/index.html</c> would not merely be a second way to reach the same page: it would
    /// be the copy the installed application actually runs, with no nonce on its scripts and therefore
    /// nothing on screen. Rewriting is preferable to hiding the file from the static-file provider,
    /// which would make the same request a 404 and fail the service worker's install step instead.
    /// </remarks>
    /// <param name="app">The application pipeline.</param>
    /// <returns>The same pipeline.</returns>
    public static IApplicationBuilder UseAppShellRewrite(this IApplicationBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        return app.Use(async (context, next) =>
        {
            if (context.Request.Path.Equals("/" + ShellFileName, StringComparison.OrdinalIgnoreCase))
            {
                context.Request.Path = CanonicalPath;
            }

            await next(context);
        });
    }

    /// <summary>
    /// Maps the shell as the last route to be tried, so a deep link opened from a scanner or a message
    /// resolves in the client router.
    /// </summary>
    /// <param name="endpoints">The route builder.</param>
    /// <param name="environment">The environment, for the web root the shell is read from.</param>
    /// <returns>The endpoint, so the caller can declare its policies.</returns>
    public static IEndpointConventionBuilder MapAppShellFallback(
        this IEndpointRouteBuilder endpoints,
        IWebHostEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        ArgumentNullException.ThrowIfNull(environment);

        var shell = new AppShellSource(environment.WebRootFileProvider);

        // The same pattern and the same two methods MapFallbackToFile maps, and the same request
        // delegate shape, so the shell stays out of the API description exactly as it was: the `nonfile`
        // constraint is what keeps a mistyped asset path a 404 rather than a page of HTML with a 200.
        var builder = endpoints.MapMethods(
            "{*path:nonfile}",
            [HttpMethods.Get, HttpMethods.Head],
            (HttpContext context) => shell.WriteAsync(context));

        builder.Add(endpoint =>
        {
            if (endpoint is RouteEndpointBuilder route)
            {
                route.Order = int.MaxValue;
            }
        });

        return builder;
    }
}

/// <summary>
/// The shell as it is read from disk: the file, watched for change, and the parsed document it last
/// produced.
/// </summary>
/// <remarks>
/// Parsing once and rejoining per request is what keeps the fallback cheap. The file is re-read when
/// its length or modification time changes, which is what makes a developer's rebuild visible without
/// restarting the host, and what makes a deployment that replaces the file take effect.
/// </remarks>
/// <param name="fileProvider">The web root.</param>
internal sealed class AppShellSource(IFileProvider fileProvider)
{
    private readonly Lock _gate = new();
    private AppShellDocument? _document;
    private long _length = -1;
    private DateTimeOffset _lastModified;

    /// <summary>Writes the shell, or 404 when no client has been built into the web root.</summary>
    public async Task WriteAsync(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var document = Read();
        if (document is null)
        {
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }

        var nonce = SecurityHeadersMiddleware.NonceOf(context)
            ?? throw new InvalidOperationException(
                "The shell was rendered without a content-security-policy nonce, which means "
                + "SecurityHeadersMiddleware is no longer ahead of routing in the pipeline. Every "
                + "script in the shell would be refused by the browser.");

        context.Response.ContentType = "text/html; charset=utf-8";
        context.Response.Headers.CacheControl = "no-store";

        await context.Response.WriteAsync(document.Render(nonce), context.RequestAborted);
    }

    private AppShellDocument? Read()
    {
        var file = fileProvider.GetFileInfo(AppShellEndpoints.ShellFileName);
        if (!file.Exists || file.IsDirectory)
        {
            return null;
        }

        lock (_gate)
        {
            if (_document is not null && _length == file.Length && _lastModified == file.LastModified)
            {
                return _document;
            }

            using var stream = file.CreateReadStream();
            using var reader = new StreamReader(stream, Encoding.UTF8);

            _document = AppShellDocument.Parse(reader.ReadToEnd());
            _length = file.Length;
            _lastModified = file.LastModified;

            return _document;
        }
    }
}

/// <summary>
/// The shell document, split at every point a nonce attribute has to be inserted.
/// </summary>
/// <remarks>
/// Splitting rather than replacing per request means the scan for tags happens once and each response
/// is a concatenation. It is also what makes the rule testable: <see cref="NoncedTagCount"/> says how
/// many tags were found, so a build whose output stops matching — a bundler that emits its scripts
/// differently — fails a test instead of silently serving a page whose scripts are all refused.
/// </remarks>
public sealed partial class AppShellDocument
{
    private const string NonceAttributePrefix = " nonce=\"";
    private const string NonceAttributeSuffix = "\"";

    private readonly string[] _segments;

    private AppShellDocument(string[] segments) => _segments = segments;

    /// <summary>How many tags in the document are given the nonce.</summary>
    public int NoncedTagCount => _segments.Length - 1;

    /// <summary>Splits a shell document at the tags that need the nonce attribute.</summary>
    /// <param name="html">The shell as it was built.</param>
    /// <returns>The parsed document.</returns>
    public static AppShellDocument Parse(string html)
    {
        ArgumentNullException.ThrowIfNull(html);

        var segments = new List<string>();
        var cursor = 0;

        foreach (var tag in ScriptOrLinkTag().EnumerateMatches(html))
        {
            var text = html.AsSpan(tag.Index, tag.Length);
            if (!NeedsNonce(text, out var nameLength))
            {
                continue;
            }

            // Immediately after "<script" or "<link", which is a position no attribute value can
            // contain and where an attribute is always legal.
            var insertAt = tag.Index + 1 + nameLength;
            segments.Add(html[cursor..insertAt]);
            cursor = insertAt;
        }

        segments.Add(html[cursor..]);
        return new AppShellDocument([.. segments]);
    }

    /// <summary>Renders the document for one response.</summary>
    /// <param name="nonce">The response's nonce.</param>
    /// <returns>The shell with the nonce stamped on every script it loads.</returns>
    public string Render(string nonce)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(nonce);

        if (_segments.Length == 1)
        {
            return _segments[0];
        }

        var attribute = NonceAttributePrefix + nonce + NonceAttributeSuffix;
        var builder = new StringBuilder(_segments.Sum(segment => segment.Length)
            + (NoncedTagCount * attribute.Length));

        for (var index = 0; index < _segments.Length; index++)
        {
            if (index > 0)
            {
                builder.Append(attribute);
            }

            builder.Append(_segments[index]);
        }

        return builder.ToString();
    }

    /// <summary>
    /// True for a tag the browser fetches as script: every <c>&lt;script&gt;</c>, and the
    /// <c>&lt;link&gt;</c> elements that preload one.
    /// </summary>
    private static bool NeedsNonce(ReadOnlySpan<char> tag, out int nameLength)
    {
        if (tag[1..].StartsWith("script", StringComparison.OrdinalIgnoreCase))
        {
            nameLength = "script".Length;
            return true;
        }

        nameLength = "link".Length;

        return tag.Contains("modulepreload", StringComparison.OrdinalIgnoreCase)
            || (tag.Contains("rel=\"preload\"", StringComparison.OrdinalIgnoreCase)
                && tag.Contains("as=\"script\"", StringComparison.OrdinalIgnoreCase));
    }

    [GeneratedRegex(
        "<(?:script|link)\\b[^>]*>",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
        matchTimeoutMilliseconds: 1000)]
    private static partial Regex ScriptOrLinkTag();
}
