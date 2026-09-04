using Microsoft.Extensions.DependencyInjection;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Tailor360.Platform.Observability.Correlation;

namespace Tailor360.Platform.Observability.Telemetry;

/// <summary>Registers tracing, metrics and correlation.</summary>
public static class ObservabilityServiceCollectionExtensions
{
    /// <summary>
    /// Registers OpenTelemetry tracing and metrics for the application. Instrumentation is always
    /// collected; it is only exported when an OTLP endpoint is configured, so a deployment without a
    /// telemetry backend pays no export cost and leaks nothing over the network.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="serviceVersion">The build version reported as a resource attribute.</param>
    public static IServiceCollection AddTailor360Observability(
        this IServiceCollection services,
        string serviceVersion)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddOptions<ObservabilityOptions>()
            .BindConfiguration(ObservabilityOptions.SectionName)
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddScoped<CorrelationContext>();
        services.AddScoped<ICorrelationContext>(sp => sp.GetRequiredService<CorrelationContext>());

        var options = new ObservabilityOptions();
        services.AddOpenTelemetry()
            .ConfigureResource(resource => resource
                .AddService(Tailor360Diagnostics.SourceName, serviceVersion: serviceVersion))
            .WithTracing(tracing =>
            {
                tracing
                    .AddSource(Tailor360Diagnostics.SourceName)
                    .AddAspNetCoreInstrumentation(instrumentation =>
                    {
                        // Health probes are called every few seconds by the orchestrator and would
                        // otherwise dominate the trace volume without telling anyone anything.
                        instrumentation.Filter = context =>
                            !context.Request.Path.StartsWithSegments("/health", StringComparison.Ordinal);
                        instrumentation.RecordException = true;
                    })
                    .AddHttpClientInstrumentation();
            })
            .WithMetrics(metrics => metrics
                .AddMeter(Tailor360Diagnostics.SourceName)
                .AddAspNetCoreInstrumentation()
                .AddHttpClientInstrumentation()
                .AddRuntimeInstrumentation());

        services.ConfigureOpenTelemetryTracerProvider((sp, builder) =>
        {
            var configured = sp.GetRequiredService<
                Microsoft.Extensions.Options.IOptions<ObservabilityOptions>>().Value;
            if (configured.ExportsTelemetry)
            {
                builder.AddOtlpExporter(exporter => exporter.Endpoint = new Uri(configured.OtlpEndpoint));
            }
        });

        services.ConfigureOpenTelemetryMeterProvider((sp, builder) =>
        {
            var configured = sp.GetRequiredService<
                Microsoft.Extensions.Options.IOptions<ObservabilityOptions>>().Value;
            if (configured.ExportsTelemetry)
            {
                builder.AddOtlpExporter(exporter => exporter.Endpoint = new Uri(configured.OtlpEndpoint));
            }
        });

        _ = options;
        return services;
    }
}
