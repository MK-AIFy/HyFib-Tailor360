using Serilog.Core;
using Serilog.Events;

namespace Tailor360.Platform.Observability.Logging;

/// <summary>
/// A Serilog enricher that replaces the value of any log property whose name is on the sensitive list.
/// Applied to every sink, so a secret cannot be logged by an accidental structured-logging call.
/// </summary>
public sealed class RedactingEnricher : ILogEventEnricher
{
    /// <inheritdoc />
    public void Enrich(LogEvent logEvent, ILogEventPropertyFactory propertyFactory)
    {
        ArgumentNullException.ThrowIfNull(logEvent);
        ArgumentNullException.ThrowIfNull(propertyFactory);

        List<string>? sensitive = null;
        foreach (var property in logEvent.Properties)
        {
            if (LogRedaction.IsSensitive(property.Key))
            {
                (sensitive ??= []).Add(property.Key);
            }
        }

        if (sensitive is null)
        {
            return;
        }

        foreach (var name in sensitive)
        {
            logEvent.AddOrUpdateProperty(new LogEventProperty(name, new ScalarValue(LogRedaction.Placeholder)));
        }
    }
}
