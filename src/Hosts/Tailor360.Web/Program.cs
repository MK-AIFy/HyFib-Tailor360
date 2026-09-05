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
builder.Services.AddOpenApi();
builder.Services.AddRateLimiter(options => options.AddTailor360Policies());

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

app.UseDefaultFiles();
app.UseStaticFiles();

app.UseRouting();

// A cross-site attempt is refused on a header comparison, before it can reach the session lookup or
// spend a rate-limit permit that belongs to the person being attacked.
app.UseTailor360OriginChecks();

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

app.UseAuthorization();

app.MapTailor360HealthEndpoints();
app.MapVersionEndpoint(app.Environment);
app.MapAntiForgeryEndpoint();

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
            "#20");
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
    .ExcludeFromDescription();

app.MapFallback("/health/{**path}", () => Results.NotFound())
    .AllowAnonymousWithJustification(
        "Returns 404 for an unknown health path, so a probe misconfigured to the wrong path fails " +
        "loudly instead of receiving the client shell and reporting the instance healthy.",
        "#20")
    .ExcludeFromDescription();

// The progressive web application is a single-page shell: any other path returns index.html so that a
// deep link opened from a scanner or a message resolves in the client router.
app.MapFallbackToFile("index.html")
    .AllowAnonymousWithJustification(
        "The application shell is a static HTML file containing no data. It must load before a session " +
        "exists so that the sign-in screen can be shown, and so that a deep link opened from a scanner " +
        "or a message resolves in the client router. Every datum the shell then requests is authorised.",
        "#20");

await app.RunAsync();
