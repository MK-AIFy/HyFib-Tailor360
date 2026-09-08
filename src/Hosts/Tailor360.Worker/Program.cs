using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Serilog;
using Tailor360.Modules.Billing.Infrastructure;
using Tailor360.Modules.Catalog.Infrastructure;
using Tailor360.Modules.Custody.Infrastructure;
using Tailor360.Modules.Customers.Infrastructure;
using Tailor360.Modules.Identity.Infrastructure;
using Tailor360.Modules.Integration.Infrastructure;
using Tailor360.Modules.Inventory.Infrastructure;
using Tailor360.Modules.Media.Infrastructure;
using Tailor360.Modules.Notifications.Infrastructure;
using Tailor360.Modules.Orders.Infrastructure;
using Tailor360.Modules.Reporting.Infrastructure;
using Tailor360.Platform.Abstractions.Auditing;
using Tailor360.Platform.Abstractions.Health;
using Tailor360.Platform.Observability.Health;
using Tailor360.Platform.Observability.Logging;
using Tailor360.Platform.Observability.Telemetry;
using Tailor360.Platform.Persistence;
using Tailor360.Platform.Security.Background;
using Tailor360.Worker;
using Tailor360.Worker.Jobs;

var builder = WebApplication.CreateBuilder(args);

var secretsDirectory = builder.Configuration["Secrets:Directory"];
if (!string.IsNullOrWhiteSpace(secretsDirectory) && Directory.Exists(secretsDirectory))
{
    builder.Configuration.AddKeyPerFile(secretsDirectory, optional: true, reloadOnChange: true);
}

builder.Host.UseSerilog((context, _, loggerConfiguration) =>
    loggerConfiguration.Apply(context.Configuration, "tailor360-worker"));

builder.Services.AddOptions<WorkerOptions>()
    .BindConfiguration(WorkerOptions.SectionName)
    .ValidateDataAnnotations()
    .ValidateOnStart();

builder.Services.AddTailor360Platform();
builder.Services.AddTailor360Observability(builder.Configuration, typeof(Program).Assembly.GetName().Version?.ToString() ?? "0.0.0");

// Every module that owns a schema, because every module owns an outbox in it and the dispatcher
// delivers from all of them. A module composed in the web host and not here has a table nothing ever
// claims from: its events are written, committed, and never delivered — silently, because an empty
// claim query is what an idle outbox looks like. The web host and the command line already compose
// these two; the worker did not, which is a gap the shared outbox table used to hide.
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

// A job has no request and therefore no ambient user, so the principal it runs as is chosen from its
// [WorkerJob] declaration rather than presented. This registers that choice, and nothing else of the
// security stack: the worker serves no request to authenticate.
builder.Services.AddTailor360WorkerScopes();

// Deliberately not TryAdd. The platform registers the system audit context by default, which would
// attribute every row a job writes to "system" with no correlation; this one names the job, or the
// person the job is acting for, and must therefore be the later — and winning — registration.
builder.Services.AddScoped<IAuditContext, WorkerAuditContext>();

builder.Services.AddSingleton<HeartbeatService>();
builder.Services.AddHostedService<OutboxDispatcherService>();
builder.Services.AddHostedService<AuditPartitionMaintenanceService>();
builder.Services.AddSingleton<IHeartbeatMonitor>(sp => sp.GetRequiredService<HeartbeatService>());
builder.Services.AddHostedService(sp => sp.GetRequiredService<HeartbeatService>());

builder.Services.AddHealthChecks()
    .AddCheck("self", () => HealthCheckResult.Healthy(), tags: [HealthCheckTags.Live, HealthCheckTags.Startup])
    .AddCheck<HeartbeatHealthCheck>("heartbeat", tags: [HealthCheckTags.Ready]);

// The worker serves nothing but its probes, on a port compose and Kubernetes keep internal.
var healthPort = builder.Configuration.GetValue<int?>($"{WorkerOptions.SectionName}:HealthPort") ?? 8081;
builder.WebHost.UseUrls($"http://0.0.0.0:{healthPort}");

var app = builder.Build();

app.MapTailor360HealthEndpoints();

await app.RunAsync();
