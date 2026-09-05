using System.Globalization;
using Microsoft.Extensions.Configuration;
using Serilog;
using Serilog.Events;

namespace Tailor360.Platform.Observability.Logging;

/// <summary>Builds the logging configuration every host shares.</summary>
public static class SerilogConfiguration
{
    /// <summary>
    /// Applies structured console logging with redaction to a logger configuration. Console output is
    /// the transport because a container platform, a compose deployment and a developer machine all
    /// collect stdout, which keeps one logging path rather than three.
    /// </summary>
    /// <param name="loggerConfiguration">The configuration supplied by the host.</param>
    /// <param name="configuration">Application configuration, read for the <c>Serilog</c> section.</param>
    /// <param name="applicationName">The value stamped on every event as <c>Application</c>.</param>
    public static LoggerConfiguration Apply(
        this LoggerConfiguration loggerConfiguration,
        IConfiguration configuration,
        string applicationName)
    {
        ArgumentNullException.ThrowIfNull(loggerConfiguration);
        ArgumentNullException.ThrowIfNull(configuration);

        return loggerConfiguration
            .ReadFrom.Configuration(configuration)
            .Enrich.FromLogContext()
            .Enrich.With<RedactingEnricher>()
            .Enrich.WithProperty("Application", applicationName)
            .MinimumLevel.Information()
            .MinimumLevel.Override("Microsoft.AspNetCore", LogEventLevel.Warning)
            .MinimumLevel.Override("Microsoft.EntityFrameworkCore.Database.Command", LogEventLevel.Warning)
            .WriteTo.Console(formatProvider: CultureInfo.InvariantCulture);
    }
}
