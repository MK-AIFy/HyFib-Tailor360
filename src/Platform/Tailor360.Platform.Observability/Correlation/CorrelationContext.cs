namespace Tailor360.Platform.Observability.Correlation;

/// <summary>A mutable correlation context, set once per request or job.</summary>
public sealed class CorrelationContext : ICorrelationContext
{
    /// <summary>The header the client may supply and that every response echoes.</summary>
    public const string HeaderName = "X-Correlation-Id";

    /// <summary>The maximum length accepted from a client, to keep a hostile value out of the logs.</summary>
    public const int MaxLength = 64;

    /// <inheritdoc />
    public string CorrelationId { get; private set; } = string.Empty;

    /// <summary>Sets the correlation identifier for this context.</summary>
    public void Set(string correlationId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(correlationId);
        CorrelationId = correlationId;
    }

    /// <summary>
    /// Returns a safe correlation identifier: the caller's value when it is short and made only of
    /// characters that cannot forge a log line, otherwise a freshly generated one.
    /// </summary>
    public static string SanitiseOrCreate(string? candidate)
    {
        if (string.IsNullOrWhiteSpace(candidate) || candidate.Length > MaxLength)
        {
            return Guid.CreateVersion7().ToString("n");
        }

        foreach (var c in candidate)
        {
            if (!char.IsAsciiLetterOrDigit(c) && c != '-' && c != '_')
            {
                return Guid.CreateVersion7().ToString("n");
            }
        }

        return candidate;
    }
}
