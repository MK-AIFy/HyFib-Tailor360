using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Serilog;
using Tailor360.Platform.Abstractions.Health;
using Tailor360.Platform.Observability.Health;
using Tailor360.Platform.Observability.Logging;
using Tailor360.Platform.Observability.Telemetry;
using Tailor360.Platform.Persistence;
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
builder.Services.AddTailor360Observability(
    typeof(Program).Assembly.GetName().Version?.ToString() ?? "0.0.0");

builder.Services.AddSingleton<HeartbeatService>();
builder.Services.AddHostedService<OutboxDispatcherService>();
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
