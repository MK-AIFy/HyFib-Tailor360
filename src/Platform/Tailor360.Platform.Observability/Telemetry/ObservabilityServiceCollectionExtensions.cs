using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
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
    /// <param name="configuration">
    /// Application configuration. The service name and sampling ratio are read from it eagerly,
    /// because a resource attribute and a sampler are fixed when the provider is built and cannot be
    /// supplied later from options.
    /// </param>
    /// <param name="serviceVersion">The build version reported as a resource attribute.</param>
    public static IServiceCollection AddTailor360Observability(
        this IServiceCollection services,
        IConfiguration configuration,
        string serviceVersion)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddOptions<ObservabilityOptions>()
            .BindConfiguration(ObservabilityOptions.SectionName)
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddScoped<CorrelationContext>();
        services.AddScoped<ICorrelationContext>(sp => sp.GetRequiredService<CorrelationContext>());

        // The resource identity and the sampler are baked into the provider at build time, so they are
        // read here rather than resolved from IOptions later. Reporting both hosts under one service
        // name would make their traces indistinguishable, and leaving the sampler unset would export
        // every trace regardless of the configured ratio.
        var startup = new ObservabilityOptions();
        configuration.GetSection(ObservabilityOptions.SectionName).Bind(startup);

        services.AddOpenTelemetry()
            .ConfigureResource(resource => resource
                .AddService(startup.ServiceName, serviceVersion: serviceVersion))
            .WithTracing(tracing =>
            {
                tracing
                    .SetSampler(new ParentBasedSampler(new TraceIdRatioBasedSampler(startup.TraceSamplingRatio)))
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
            var configured = sp.GetRequiredService<IOptions<ObservabilityOptions>>().Value;
            if (configured.ExportsTelemetry)
            {
                builder.AddOtlpExporter(exporter => exporter.Endpoint = new Uri(configured.OtlpEndpoint));
            }
        });

        services.ConfigureOpenTelemetryMeterProvider((sp, builder) =>
        {
            var configured = sp.GetRequiredService<IOptions<ObservabilityOptions>>().Value;
            if (configured.ExportsTelemetry)
            {
                builder.AddOtlpExporter(exporter => exporter.Endpoint = new Uri(configured.OtlpEndpoint));
            }
        });

        return services;
    }
}
