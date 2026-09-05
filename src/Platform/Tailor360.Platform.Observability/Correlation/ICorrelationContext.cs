namespace Tailor360.Platform.Observability.Correlation;

/// <summary>
/// The correlation identifier for the current unit of work. It flows from the client through the
/// request, into the outbox message a command produces, and on into the worker that dispatches it, so
/// one identifier ties a user action to every downstream effect.
/// </summary>
public interface ICorrelationContext
{
    /// <summary>The correlation identifier for the current request or job.</summary>
    string CorrelationId { get; }
}
