using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using Tailor360.Platform.Abstractions.Ports;
using Tailor360.Platform.Abstractions.Results;

namespace Tailor360.Modules.Identity.Infrastructure.Email;

/// <summary>
/// Keeps messages in memory instead of sending them. This is the sender tests assert against, and the
/// setting for an environment that must not write to real people.
/// </summary>
/// <remarks>
/// The collection is bounded, because a long-running host configured to collect would otherwise hold
/// every message it ever rendered. It keeps the most recent messages and drops the oldest.
/// <para>
/// A test reads <see cref="Messages"/> to assert what was sent — that a recovery request to an unknown
/// address produced nothing, that a completed reset produced an alert, that no body contains a code.
/// Production code never reads it.
/// </para>
/// </remarks>
/// <param name="logger">Logger, which records once that nothing will be delivered.</param>
public sealed class CollectedEmailSender(ILogger<CollectedEmailSender> logger) : IEmailSender
{
    /// <summary>How many messages are kept before the oldest are dropped.</summary>
    public const int Capacity = 200;

    private readonly ConcurrentQueue<EmailMessage> _messages = new();
    private int _announced;

    /// <summary>The messages collected, oldest first.</summary>
    public IReadOnlyCollection<EmailMessage> Messages => [.. _messages];

    /// <summary>Forgets everything collected so far.</summary>
    public void Clear() => _messages.Clear();

    /// <inheritdoc />
    public Task<Result> SendAsync(EmailMessage message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);

        if (Interlocked.Exchange(ref _announced, 1) == 0)
        {
            IdentityEmailLog.CollectingOnly(logger);
        }

        _messages.Enqueue(message);

        while (_messages.Count > Capacity && _messages.TryDequeue(out _))
        {
            // Keep the most recent messages only.
        }

        return Task.FromResult(Result.Success());
    }
}
