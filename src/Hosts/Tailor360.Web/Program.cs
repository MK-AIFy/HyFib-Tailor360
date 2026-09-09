using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Serilog;
using Tailor360.Modules.Billing.Api;
using Tailor360.Modules.Billing.Infrastructure;
using Tailor360.Modules.Catalog.Api;
using Tailor360.Modules.Catalog.Infrastructure;
using Tailor360.Modules.Custody.Api;
using Tailor360.Modules.Custody.Infrastructure;
using Tailor360.Modules.Customers.Api;
using Tailor360.Modules.Customers.Infrastructure;
using Tailor360.Modules.Identity.Api;
using Tailor360.Modules.Identity.Infrastructure;
using Tailor360.Modules.Integration.Api;
using Tailor360.Modules.Integration.Infrastructure;
using Tailor360.Modules.Inventory.Api;
using Tailor360.Modules.Inventory.Infrastructure;
using Tailor360.Modules.Media.Api;
using Tailor360.Modules.Media.Infrastructure;
using Tailor360.Modules.Notifications.Api;
using Tailor360.Modules.Notifications.Infrastructure;
using Tailor360.Modules.Orders.Api;
using Tailor360.Modules.Orders.Infrastructure;
using Tailor360.Modules.Reporting.Api;
using Tailor360.Modules.Reporting.Infrastructure;
using Tailor360.Platform.Abstractions.Auditing;
using Tailor360.Platform.Abstractions.Health;
using Tailor360.Platform.Observability.Correlation;
using Tailor360.Platform.Observability.Health;
using Tailor360.Platform.Observability.Logging;
using Tailor360.Platform.Observability.Telemetry;
using Tailor360.Platform.Persistence;
using Tailor360.Platform.Persistence.DataProtection;
using Tailor360.Platform.Security;
using Tailor360.Platform.Security.Endpoints;
using Tailor360.Web.Configuration;
using Tailor360.Web.Endpoints;
using Tailor360.Web.Middleware;
using Tailor360.Web.OpenApi;
using Tailor360.Web.Timeline;

var builder = WebApplication.CreateBuilder(args);

// Secrets are supplied as files (Docker and Kubernetes secrets) rather than environment variables, so
// that a process listing or a crash dump of the environment block cannot disclose them.
var secretsDirectory = builder.Configuration["Secrets:Directory"];
if (!string.IsNullOrWhiteSpace(secretsDirectory) && Directory.Exists(secretsDirectory))
{
    builder.Configuration.AddKeyPerFile(secretsDirectory, optional: true, reloadOnChange: true);
}

builder.Host.UseSerilog((context, _, loggerConfiguration) =>
    loggerConfiguration.Apply(context.Configuration, "tailor360-web"));

builder.Services.ConfigureOptions<ForwardedHeadersOptionsSetup>();

builder.Services.AddProblemDetails();

// The customer timeline is composed rather than owned: every module registers an ITimelineSource for
// the facts it holds and this merges them, which is why it lives in the host and not in Customers
// (docs/architecture/architecture-rules.md, ROD-02).
builder.Services.AddScoped<CustomerTimelineComposer>();
builder.Services.AddTailor360OpenApi();
builder.Services.AddRateLimiter(options => options.AddTailor360Policies());

// Validated at start-up rather than at the first request: a minimum client version that does not parse
// would otherwise present as "the handshake silently stopped refusing anything", which is invisible.
builder.Services.AddOptions<ClientCompatibilityOptions>()
    .Bind(builder.Configuration.GetSection(ClientCompatibilityOptions.SectionName))
    .ValidateDataAnnotations()
    .ValidateOnStart();

builder.Services.AddTailor360Platform();

// The key ring encrypts the anti-forgery token pair, so it has to be shared by every instance and to
// survive a restart. Persisting it in the database is what makes both true; the container images run
// with a read-only root file system, so the framework's own default could not work here anyway.
builder.Services.AddTailor360DataProtection(builder.Configuration);

builder.Services.AddTailor360Security();
builder.Services.AddTailor360Observability(builder.Configuration, BuildInformation.Version);

// Audit entries name the person who acted, not "system". The host composes this because it is the one
// place that sees both the session-backed caller and the request's correlation identifier.
builder.Services.RemoveAll<IAuditContext>();
builder.Services.AddScoped<IAuditContext, SessionAuditContext>();

// Readiness reports the database only. Object storage, the malware scanner and provider APIs are
// reported as degraded rather than unhealthy, because losing them disables a feature, not the instance.
builder.Services.AddHealthChecks()
    .AddCheck("self", () => Microsoft.Extensions.Diagnostics.HealthChecks.HealthCheckResult.Healthy(),
        tags: [HealthCheckTags.Live, HealthCheckTags.Startup]);

// Modules are composed only through their registration extensions (architecture rule ARCH-006).
builder.Services
    .AddIdentityModule(builder.Configuration)
    .AddCustomersModule(builder.Configuration)
    .AddCatalogModule(builder.Configuration)
    .AddMediaModule(builder.Configuration)
    .AddOrdersModule(builder.Configuration)
    .AddCustodyModule(builder.Configuration)
    .AddInventoryModule(builder.Configuration)
    .AddBillingModule(builder.Configuration)
    .AddReportingModule(builder.Configuration)
    .AddNotificationsModule(builder.Configuration)
    .AddIntegrationModule(builder.Configuration);

var app = builder.Build();

app.UseForwardedHeaders(app.Services.GetRequiredService<IOptions<ForwardedHeadersOptions>>().Value);
app.UseMiddleware<CorrelationMiddleware>();
app.UseMiddleware<SecurityHeadersMiddleware>();
app.UseSerilogRequestLogging();

if (!app.Environment.IsDevelopment())
{
    app.UseHsts();
}

app.UseExceptionHandler();
app.UseStatusCodePages();

// The shell is not a static file any more: it is rendered by an endpoint so that each response's
// content-security-policy nonce can be stamped onto the scripts it loads. This rewrite is what keeps
// the one path that would still have reached the file directly — the name the service worker precaches
// the shell under — going to that endpoint instead.
app.UseAppShellRewrite();
app.UseStaticFiles();

app.UseRouting();

// A client too old for this server is told so before it is charged a session lookup or a rate-limit
// permit, and before an endpoint reads a field it may no longer send.
app.UseTailor360ClientVersion();

// A cross-site attempt is refused on a header comparison, before it can reach the session lookup or
// spend a rate-limit permit that belongs to the person being attacked.
app.UseTailor360OriginChecks();

// Buffering for the small, self-selecting set of requests that carry an Idempotency-Key, so that the
// endpoint filter can fingerprint a body model binding has already read to its end.
app.UseTailor360IdempotencyKeys();

// The per-endpoint request timeouts of the catalogue. Registered here rather than per endpoint so that
// a route without a declared policy still runs under the default rather than under none.
app.UseRequestTimeouts();

app.UseAuthentication();

// Rate limiting runs after authentication so that the authenticated policies can key on the account.
// A whole shop reaches this system through one broadband connection, so keying on the address would
// throttle a dozen people as one caller; the credential policies key on the address anyway, because
// before a session exists there is nothing else to key on. Authenticating first costs one indexed
// lookup, and only for a request that presented a cookie.
app.UseRateLimiter();

// Anti-forgery runs after authentication because the request token is bound to the signed-in account,
// and before authorisation so that a forged request is refused without reaching an endpoint's policy.
// It covers sign-in, the multi-factor challenge, passkeys and recovery as well as ordinary commands.
app.UseTailor360Antiforgery();

// The resource an endpoint names is loaded here, between routing — which is what makes the route
// values available — and authorisation, which is the only position in the pipeline where a handler can
// decide against the row rather than against the caller's own claims.
app.UseTailor360ResourceScope();

app.UseAuthorization();

app.MapTailor360HealthEndpoints();
app.MapVersionEndpoint(app.Environment);
app.MapAntiForgeryEndpoint();

app.MapCustomerTimelineEndpoint();

app.MapIdentityEndpoints()
    .MapCustomersEndpoints()
    .MapCatalogEndpoints()
    .MapMediaEndpoints()
    .MapOrdersEndpoints()
    .MapCustodyEndpoints()
    .MapInventoryEndpoints()
    .MapBillingEndpoints()
    .MapReportingEndpoints()
    .MapNotificationsEndpoints()
    .MapIntegrationEndpoints();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi()
        .AllowAnonymousWithJustification(
            "The OpenAPI document describes the API surface and is mapped only in the Development " +
            "environment, where it is served to a developer machine. It is never mapped in staging or " +
            "production, so it discloses nothing to an unauthenticated caller of a deployed instance.",
            "#20")
        .InternalEndpoint(
            "The document cannot describe itself, and it is mapped in Development only. The published " +
            "contract is the committed copy at docs/api/openapi.v1.json, which is what clients and the " +
            "diff gate read.",
            "#53, docs/api/openapi-gates.md")
        .RequireRateLimiting(RateLimitPolicyNames.DefaultIp);
}

// An unknown path under /api or /health must be a 404, never the client shell. Once the built client
// is present in wwwroot the shell fallback would otherwise answer a mistyped API call with HTML and a
// 200, and the caller would fail somewhere far from the cause. These patterns are more specific than
// the shell fallback below, so they win.
app.MapFallback("/api/{**path}", () => Results.NotFound())
    .AllowAnonymousWithJustification(
        "Returns 404 for an unknown API route and nothing else. Answering here rather than falling " +
        "through to the client shell is what keeps a mistyped API call from receiving HTML with a 200.",
        "#20")
    .InternalEndpoint(
        "A catch-all that answers 404 and nothing else. It has no contract to publish, and publishing " +
        "one would document every path in the API as though it existed.",
        "#53, docs/api/openapi-gates.md")
    .RequireRateLimiting(RateLimitPolicyNames.DefaultIp);

app.MapFallback("/health/{**path}", () => Results.NotFound())
    .AllowAnonymousWithJustification(
        "Returns 404 for an unknown health path, so a probe misconfigured to the wrong path fails " +
        "loudly instead of receiving the client shell and reporting the instance healthy.",
        "#20")
    .InternalEndpoint(
        "A catch-all that answers 404 and nothing else. The probes it guards are operational routes " +
        "for the orchestrator and the uptime check, not part of the client contract.",
        "#53, docs/api/openapi-gates.md")
    .RequireRateLimiting(RateLimitPolicyNames.DefaultIp);

// The progressive web application is a single-page shell: any other path returns index.html so that a
// deep link opened from a scanner or a message resolves in the client router. It is rendered rather
// than served from disk, because the enforcing content-security-policy admits only the scripts carrying
// this response's nonce, and a file on disk is the same bytes for every response.
app.MapAppShellFallback(app.Environment)
    .AllowAnonymousWithJustification(
        "The application shell is an HTML document containing no data. It must load before a session " +
        "exists so that the sign-in screen can be shown, and so that a deep link opened from a scanner " +
        "or a message resolves in the client router. Every datum the shell then requests is authorised.",
        "#20")
    .InternalEndpoint(
        "The shell is an HTML document, not an API operation: it answers with markup rather than " +
        "with a payload, and no client calls it as an interface. What the shell then calls is the " +
        "documented surface.",
        "#53, docs/api/openapi-gates.md")
    .RequireRateLimiting(RateLimitPolicyNames.DefaultIp);

await app.RunAsync();
