using Microsoft.Extensions.Logging;

namespace Tailor360.Platform.Persistence;

/// <summary>
/// Source-generated log messages for the persistence layer. Generated messages cost nothing when the
/// level is disabled, which matters for the dispatcher because it logs on every poll.
/// </summary>
internal static partial class PersistenceLog
{
    [LoggerMessage(EventId = 1001, Level = LogLevel.Information,
        Message = "Acquired the migration advisory lock.")]
    public static partial void MigrationLockAcquired(ILogger logger);

    [LoggerMessage(EventId = 1002, Level = LogLevel.Information,
        Message = "Released the migration advisory lock.")]
    public static partial void MigrationLockReleased(ILogger logger);

    [LoggerMessage(EventId = 1003, Level = LogLevel.Information,
        Message = "Applying {Count} migration(s) to schema {Schema}.")]
    public static partial void ApplyingMigrations(ILogger logger, int count, string schema);

    [LoggerMessage(EventId = 1010, Level = LogLevel.Information,
        Message = "Outbox dispatcher {Owner} claimed {Count} message(s).")]
    public static partial void OutboxClaimed(ILogger logger, string owner, int count);

    [LoggerMessage(EventId = 1011, Level = LogLevel.Warning,
        Message = "Outbox message {MessageId} failed on attempt {Attempt} and will be retried.")]
    public static partial void OutboxRetry(ILogger logger, Guid messageId, int attempt, Exception exception);

    [LoggerMessage(EventId = 1012, Level = LogLevel.Error,
        Message = "Outbox message {MessageId} exhausted its attempts and was dead-lettered.")]
    public static partial void OutboxDeadLettered(ILogger logger, Guid messageId);

    [LoggerMessage(EventId = 1020, Level = LogLevel.Information,
        Message = "Feature flag cache invalidated by a change notification.")]
    public static partial void FeatureFlagsInvalidated(ILogger logger);
}
