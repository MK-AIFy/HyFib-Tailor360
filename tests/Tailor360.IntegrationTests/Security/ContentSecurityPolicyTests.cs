using System.Net;
using System.Text.RegularExpressions;
using Shouldly;
using Tailor360.Web.Endpoints;

namespace Tailor360.IntegrationTests.Security;

/// <summary>
/// The content security policy as a browser receives it, and the shell as it is rendered under it.
/// </summary>
/// <remarks>
/// <para>
/// The policy is <b>enforcing</b>. There is no report-only companion and no report endpoint: a
/// directive that is only observed is a directive that is not applied, and the plan's rule is that a
/// new directive may enter as report-only and is promoted in the pull request that needs it, not that
/// the policy as a whole waits.
/// </para>
/// <para>
/// The tests below are what "zero violations across the reference journeys" reduces to on the server
/// side: the policy contains no escape hatch, and every script the shell asks the browser to run
/// carries the nonce that authorises it. What a browser then does with it belongs to the cross-browser
/// run in #52, which is where a real engine is available.
/// </para>
/// </remarks>
[Collection(WebApplicationCollection.Name)]
[Trait("Category", "Integration")]
public sealed partial class ContentSecurityPolicyTests(WebApplicationFixture fixture)
{
    /// <summary>A shell shaped exactly as the client's build emits one.</summary>
    private const string BuiltShell = """
        <!doctype html>
        <html lang="en-IN">
          <head>
            <meta charset="utf-8" />
            <link rel="manifest" href="/manifest.webmanifest" />
            <title>HyFib Tailor360</title>
            <script type="module" crossorigin src="/assets/index-BwKB9gf5.js"></script>
            <link rel="modulepreload" crossorigin href="/assets/vendor-B-PC655C.js">
            <link rel="modulepreload" crossorigin href="/assets/vendor-router-BRrM_oZS.js">
            <link rel="stylesheet" crossorigin href="/assets/index-DmKq7hiG.css">
          </head>
          <body>
            <div id="root"></div>
          </body>
        </html>
        """;

    /// <summary>
    /// The policy carries no <c>unsafe-</c> source at all — not in <c>script-src</c>, where it would
    /// undo the nonce, and not in <c>style-src</c>, where it would admit the style attribute an
    /// injection writes.
    /// </summary>
    [Fact]
    public async Task ThePolicyContainsNoUnsafeSource()
    {
        var policy = await PolicyAsync();

        policy.ShouldNotContain("unsafe-inline");
        policy.ShouldNotContain("unsafe-eval");
        policy.ShouldNotContain("unsafe-hashes");
    }

    /// <summary>
    /// <c>script-src</c> names the nonce and <c>'strict-dynamic'</c> and no host source. With a host
    /// source present, any same-origin URL whose bytes a caller influenced — a streamed media object, a
    /// stored note echoed back — becomes a place to put a script.
    /// </summary>
    [Fact]
    public async Task ScriptsAreAdmittedByNonceRatherThanByOrigin()
    {
        var policy = await PolicyAsync();

        var scriptSource = Directive(policy, "script-src");
        scriptSource.ShouldContain("'strict-dynamic'");
        scriptSource.ShouldContain("'nonce-");
        scriptSource.ShouldNotContain("'self'");
        scriptSource.ShouldNotContain("http");
    }

    /// <summary>The directives that close the remaining injection routes.</summary>
    [Theory]
    [InlineData("default-src", "'self'")]
    [InlineData("base-uri", "'none'")]
    [InlineData("style-src", "'self'")]
    [InlineData("object-src", "'none'")]
    [InlineData("frame-src", "'none'")]
    [InlineData("frame-ancestors", "'none'")]
    [InlineData("form-action", "'self'")]
    [InlineData("connect-src", "'self'")]
    public async Task TheRemainingDirectivesAreClosed(string directive, string expected)
        => Directive(await PolicyAsync(), directive).ShouldBe(expected);

    /// <summary>
    /// The policy is enforced, not observed. A report-only header alongside it would mean the tightest
    /// half of the policy was the half nothing applied.
    /// </summary>
    [Fact]
    public async Task ThePolicyIsEnforcedAndNotReportOnly()
    {
        using var client = fixture.CreateClient();

        var response = await client.GetAsync(
            new Uri("/api/version", UriKind.Relative), TestContext.Current.CancellationToken);

        response.Headers.Contains("Content-Security-Policy").ShouldBeTrue();
        response.Headers.Contains("Content-Security-Policy-Report-Only").ShouldBeFalse(
            "A report-only policy alongside the enforcing one is how a directive ends up applying to "
            + "nothing while reading as though it applies to everything.");
    }

    /// <summary>
    /// The shell the client actually receives carries this response's nonce on every script the browser
    /// will fetch — the entry module and each preload link. Without it the page is blank under the
    /// policy above, so this is the test that would catch a bundler whose output stopped matching.
    /// </summary>
    /// <remarks>
    /// The built client is not committed, so a checkout that has not run the client build has no shell
    /// to serve and answers 404 — the state of a backend-only checkout, and the state this job runs in
    /// when the client was not built. Either answer is acceptable; serving un-nonced markup is not. The
    /// stamping rules themselves are asserted against a written-out document below, so they are covered
    /// whether or not a build is present.
    /// </remarks>
    [Theory]
    [InlineData("/")]
    [InlineData("/index.html")]
    [InlineData("/orders/12345")]
    public async Task TheShellCarriesTheResponsesOwnNonceOnEveryScript(string path)
    {
        using var client = fixture.CreateClient();

        var response = await client.GetAsync(new Uri(path, UriKind.Relative), TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBeOneOf(HttpStatusCode.OK, HttpStatusCode.NotFound);
        if (response.StatusCode is HttpStatusCode.NotFound)
        {
            return;
        }

        response.Content.Headers.ContentType?.MediaType.ShouldBe("text/html");

        // A cached shell keeps the nonce it was rendered with, while a revalidation answers with a
        // fresh policy header. The two would disagree and every script would be refused.
        response.Headers.CacheControl?.NoStore.ShouldBe(true);

        var nonce = NonceOf(response.Headers.GetValues("Content-Security-Policy").Single());
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        var tags = ScriptOrPreloadTag().Matches(body);

        tags.ShouldNotBeEmpty("The shell loads no script at all, which is not a shell.");

        var unstamped = tags
            .Select(tag => tag.Value)
            .Where(tag => !tag.Contains($"nonce=\"{nonce}\"", StringComparison.Ordinal))
            .ToList();

        unstamped.ShouldBeEmpty(
            "These tags carry no nonce, so the browser refuses them and the page stays blank:\n"
            + string.Join('\n', unstamped));
    }

    /// <summary>
    /// A path that names a file is not the shell. Without the constraint a mistyped asset would answer
    /// with a page of HTML and a 200, and the client would fail parsing JavaScript that is markup.
    /// </summary>
    [Fact]
    public async Task AMissingAssetIsNotAnsweredWithTheShell()
    {
        using var client = fixture.CreateClient();

        var response = await client.GetAsync(
            new Uri("/assets/does-not-exist.js", UriKind.Relative), TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        response.Content.Headers.ContentType?.MediaType.ShouldNotBe("text/html");
    }

    /// <summary>
    /// The document a build emits, stamped. This is the shape the client's bundler produces, written
    /// out rather than read from the web root so that the rule is asserted on every run.
    /// </summary>
    [Fact]
    public void ABuiltShellIsStampedOnItsEntryModuleAndItsPreloads()
    {
        var rendered = AppShellDocument.Parse(BuiltShell).Render("n0nc3");

        rendered.ShouldContain("<script nonce=\"n0nc3\" type=\"module\" crossorigin src=\"/assets/index-BwKB9gf5.js\">");
        rendered.ShouldContain("<link nonce=\"n0nc3\" rel=\"modulepreload\" crossorigin href=\"/assets/vendor-B-PC655C.js\">");
        rendered.ShouldContain("<link nonce=\"n0nc3\" rel=\"modulepreload\" crossorigin href=\"/assets/vendor-router-BRrM_oZS.js\">");

        // The stylesheet is admitted by style-src 'self' and needs nothing stamped on it.
        rendered.ShouldContain("<link rel=\"stylesheet\" crossorigin href=\"/assets/index-DmKq7hiG.css\">");

        // Nothing else moved.
        rendered.Replace(" nonce=\"n0nc3\"", string.Empty, StringComparison.Ordinal).ShouldBe(BuiltShell);
    }

    /// <summary>The parser finds every tag the browser fetches as script, and no others.</summary>
    [Fact]
    public void TheShellParserFindsEveryScriptAndPreload()
        => AppShellDocument.Parse(BuiltShell).NoncedTagCount.ShouldBe(3);

    /// <summary>
    /// A document with nothing to stamp renders unchanged. The shell of a backend-only checkout, or a
    /// maintenance page, must still be served rather than fail.
    /// </summary>
    [Fact]
    public void ADocumentWithNoScriptsRendersUnchanged()
    {
        const string html = "<!doctype html><html><body><p>Maintenance</p></body></html>";

        var document = AppShellDocument.Parse(html);

        document.NoncedTagCount.ShouldBe(0);
        document.Render("abc").ShouldBe(html);
    }

    /// <summary>
    /// A preload that is not a script is left alone. Stamping a stylesheet or a font preload with a
    /// script nonce would say nothing true about it.
    /// </summary>
    [Fact]
    public void AStylesheetOrFontLinkIsNotStamped()
    {
        const string html = """
            <link rel="stylesheet" href="/a.css">
            <link rel="preload" as="font" href="/a.woff2" crossorigin>
            """;

        AppShellDocument.Parse(html).NoncedTagCount.ShouldBe(0);
    }

    /// <summary>A script preload is stamped, because the browser fetches it against script-src.</summary>
    [Fact]
    public void AScriptPreloadIsStamped()
    {
        const string html = """<link rel="preload" as="script" href="/a.js">""";

        AppShellDocument.Parse(html).Render("n").ShouldBe(
            """<link nonce="n" rel="preload" as="script" href="/a.js">""");
    }

    private static string Directive(string policy, string name)
    {
        var directive = policy
            .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .SingleOrDefault(candidate => candidate.StartsWith(name + " ", StringComparison.Ordinal)
                || candidate == name);

        directive.ShouldNotBeNull($"The policy declares no '{name}' directive: {policy}");
        return directive[name.Length..].Trim();
    }

    private async Task<string> PolicyAsync()
    {
        using var client = fixture.CreateClient();

        var response = await client.GetAsync(
            new Uri("/api/version", UriKind.Relative), TestContext.Current.CancellationToken);

        return response.Headers.GetValues("Content-Security-Policy").Single();
    }

    private static string NonceOf(string policy)
    {
        var nonce = Directive(policy, "script-src")
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Single(source => source.StartsWith("'nonce-", StringComparison.Ordinal));

        return nonce["'nonce-".Length..].TrimEnd('\'');
    }

    [GeneratedRegex(
        "<(?:script|link[^>]*?modulepreload)\\b[^>]*>",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ScriptOrPreloadTag();
}
