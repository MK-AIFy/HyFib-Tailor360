using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;
using Tailor360.Platform.Abstractions.Time;
using Tailor360.Platform.Persistence.Conventions;
using Tailor360.Platform.Persistence.Migrating;

namespace Tailor360.Platform.Persistence.Outbox;

/// <summary>
/// Claims and delivers outbox messages, from every module's outbox in turn.
/// </summary>
/// <remarks>
/// <para>
/// Two dispatcher instances are the expected deployment, so every step is written for that: claims are
/// taken with <c>FOR UPDATE SKIP LOCKED</c> so instances do not block each other, and only the oldest
/// undelivered message of an aggregate is ever eligible, which is what preserves per-aggregate order
/// without a global lock.
/// </para>
/// <para>
/// <strong>One table per module, read one module at a time.</strong> Each module owns an
/// <c>outbox_messages</c> table in its own schema, because that is the only arrangement in which a
/// module's write and its event commit together (ADR-0008 section 4.1, issue #77). The cycle therefore
/// walks the registered module contexts rather than reading one shared table, and a module with
/// nothing to send costs one indexed query that returns no rows.
/// </para>
/// </remarks>
/// <param name="scopeFactory">Creates a scope per module cycle.</param>
/// <param name="registry">The registered module contexts, which is also the list of outboxes.</param>
/// <param name="clock">The clock.</param>
/// <param name="options">Dispatcher configuration.</param>
/// <param name="logger">Logger.</param>
public sealed partial class OutboxDispatcher(
    IServiceScopeFactory scopeFactory,
    ModuleContextRegistry registry,
    IClock clock,
    IOptions<OutboxOptions> options,
    ILogger<OutboxDispatcher> logger)
{
    /// <summary>
    /// Claims up to one batch from every module and delivers it. Returns how many messages were
    /// processed in total, so the caller can poll immediately when a batch was full and back off when
    /// every module was empty.
    /// </summary>
    /// <param name="owner">The dispatcher instance's name, written into the lease.</param>
    /// <param name="cancellationToken">Cancels the cycle.</param>
    /// <returns>How many messages were processed.</returns>
    public async Task<int> RunCycleAsync(string owner, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(owner);

        var processed = 0;

        foreach (var module in registry.Registrations)
        {
            cancellationToken.ThrowIfCancellationRequested();
            processed += await RunModuleCycleAsync(module, owner, cancellationToken);
        }

        return processed;
    }

    /// <summary>Claims and delivers one module's batch.</summary>
    /// <param name="module">The module whose outbox to read.</param>
    /// <param name="owner">The dispatcher instance's name.</param>
    /// <param name="cancellationToken">Cancels the cycle.</param>
    /// <returns>How many of that module's messages were processed.</returns>
    public async Task<int> RunModuleCycleAsync(
        ModuleContextRegistration module,
        string owner,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(module);
        ArgumentException.ThrowIfNullOrWhiteSpace(owner);

        var schema = SafeSchema(module.Schema);

        using var scope = scopeFactory.CreateScope();
        var context = (ModuleDbContext)scope.ServiceProvider.GetRequiredService(module.ContextType);

        var claimed = await ClaimAsync(context, schema, owner, cancellationToken);
        if (claimed.Count == 0)
        {
            return 0;
        }

        PersistenceLog.OutboxClaimed(logger, owner, claimed.Count);

        var handlers = scope.ServiceProvider.GetServices<IOutboxMessageHandler>()
            .ToLookup(handler => handler.EventType, StringComparer.Ordinal);

        foreach (var delivery in claimed)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await DeliverAsync(
                scope, context, schema, handlers[delivery.EventType], delivery, owner, cancellationToken);
        }

        return claimed.Count;
    }

    /// <summary>
    /// Checks a schema name before it is interpolated into a statement.
    /// </summary>
    /// <remarks>
    /// The names come from <c>AddModuleContext</c> and are compile-time constants, not input, so this
    /// is not defending against a caller. It is defending against the day one of them is built from
    /// configuration: a schema reaches SQL by string interpolation because an identifier cannot be a
    /// parameter, and the check is what makes that safe to keep writing.
    /// </remarks>
    private static string SafeSchema(string schema)
        => SchemaName().IsMatch(schema)
            ? schema
            : throw new InvalidOperationException(
                $"'{schema}' is not a usable schema name. A module schema is lower-case letters, "
                + "digits and underscores, and the dispatcher interpolates it into its claim statement.");

    [GeneratedRegex("^[a-z_][a-z0-9_]{0,62}$")]
    private static partial Regex SchemaName();

    private async Task<IReadOnlyList<OutboxDelivery>> ClaimAsync(
        ModuleDbContext context,
        string schema,
        string owner,
        CancellationToken cancellationToken)
    {
        // Two properties have to hold at once, and both are enforced inside this one statement.
        //
        // Ordering: only the oldest undelivered message of an aggregate is a candidate (rn = 1), so a
        // later message cannot overtake an earlier one for the same aggregate however many dispatchers
        // are running. Different aggregates proceed in parallel, which is where the throughput comes from.
        //
        // Exclusivity: every eligibility condition — not yet processed, not dead-lettered, due, and not
        // already leased — appears in the WHERE clause of the locking SELECT *and* of the UPDATE.
        // That repetition is the point. A CTE is evaluated against the statement's snapshot, so if the
        // conditions lived only in a separate CTE, a dispatcher whose statement began before a rival
        // claim committed would take the row lock after that commit and still see its stale "unleased"
        // view, claiming a message another dispatcher was already handling. Both would then find no
        // inbox row and both would run the handler. Repeating the conditions where the row is locked
        // and updated forces PostgreSQL to re-check them against the latest row version, so the second
        // dispatcher claims nothing.
        var sql = $"""
            WITH ranked AS (
                SELECT id,
                       row_number() OVER (PARTITION BY aggregate_id ORDER BY occurred_at, id) AS rn
                  FROM {schema}.outbox_messages
                 WHERE processed_at IS NULL
                   AND dead_lettered_at IS NULL
            ),
            candidates AS (
                SELECT m.id
                  FROM {schema}.outbox_messages m
                  JOIN ranked r ON r.id = m.id AND r.rn = 1
                 WHERE m.processed_at IS NULL
                   AND m.dead_lettered_at IS NULL
                   AND m.available_at <= now()
                   AND (m.lease_expires_at IS NULL OR m.lease_expires_at < now())
                 ORDER BY m.occurred_at
                 LIMIT @batch
                 FOR UPDATE OF m SKIP LOCKED
            )
            UPDATE {schema}.outbox_messages m
               SET lease_owner = @owner,
                   lease_expires_at = now() + (@lease_seconds * interval '1 second'),
                   attempt_count = m.attempt_count + 1
              FROM candidates c
             WHERE m.id = c.id
               AND m.processed_at IS NULL
               AND m.dead_lettered_at IS NULL
               AND m.available_at <= now()
               AND (m.lease_expires_at IS NULL OR m.lease_expires_at < now())
            RETURNING m.id, m.aggregate_id, m.event_type, m.schema_version, m.payload,
                      m.occurred_at, m.correlation_id, m.attempt_count;
            """;

        var connection = (NpgsqlConnection)context.Database.GetDbConnection();
        await context.Database.OpenConnectionAsync(cancellationToken);

        try
        {
            await using var command = connection.CreateCommand();
            command.CommandText = sql;
            command.Parameters.Add(new NpgsqlParameter("batch", options.Value.BatchSize));
            command.Parameters.Add(new NpgsqlParameter("owner", owner));
            command.Parameters.Add(new NpgsqlParameter("lease_seconds", options.Value.LeaseDuration.TotalSeconds));

            var deliveries = new List<OutboxDelivery>();
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                deliveries.Add(new OutboxDelivery(
                    reader.GetGuid(0),
                    reader.GetGuid(1),
                    reader.GetString(2),
                    reader.GetInt32(3),
                    reader.GetString(4),
                    reader.GetFieldValue<DateTimeOffset>(5),
                    reader.IsDBNull(6) ? null : reader.GetString(6),
                    reader.GetInt32(7)));
            }

            return deliveries;
        }
        finally
        {
            await context.Database.CloseConnectionAsync();
        }
    }

    private async Task DeliverAsync(
        IServiceScope scope,
        ModuleDbContext producer,
        string schema,
        IEnumerable<IOutboxMessageHandler> handlers,
        OutboxDelivery delivery,
        string owner,
        CancellationToken cancellationToken)
    {
        // The claim is held open for as long as the handlers take. A handler that outlives the lease
        // would otherwise let a second dispatcher claim the same message and run the same handler
        // concurrently, which the inbox row cannot prevent because it is written only afterwards.
        await using var renewal = OutboxLeaseRenewal.Start(
            scopeFactory, delivery.MessageId, owner, options.Value.LeaseDuration,
            producer.GetType(), schema);

        try
        {
            foreach (var handler in handlers)
            {
                await RunHandlerAsync(scope, handler, delivery, cancellationToken);
            }

            // Conditional on still holding the lease. Marking a message processed after losing the
            // lease would hide work another dispatcher is doing, or has yet to do.
            var complete = $$"""
                UPDATE {{schema}}.outbox_messages
                   SET processed_at = {0}, lease_owner = NULL, lease_expires_at = NULL,
                       last_error = NULL
                 WHERE id = {1} AND lease_owner = {2}
                """;

            var completed = await producer.Database.ExecuteSqlRawAsync(
                complete, [clock.UtcNow, delivery.MessageId, owner], cancellationToken);

            if (completed == 0)
            {
                PersistenceLog.OutboxLeaseLost(logger, delivery.MessageId, owner);
            }
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            await FailAsync(producer, schema, delivery, exception, cancellationToken);
        }
    }

    /// <summary>
    /// Runs one handler, and commits its writes with the inbox row that records it ran.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <strong>The consuming module's context, not the producing one.</strong> A handler writes into
    /// its own module's schema, and the inbox row is only worth anything if it commits with what it
    /// records. Writing it on another context was the second half of #77: a crash between the handler's
    /// save and the inbox save left the effect applied with no row to say so, and redelivery ran it
    /// again — which is the at-most-once effect the inbox is supposed to buy.
    /// </para>
    /// <para>
    /// A handler therefore <strong>does not save</strong>; it stages its writes on the context it was
    /// given and this commits them. That is stated on <see cref="IOutboxMessageHandler"/>, because a
    /// handler that saved on its own would be back to two transactions without anything failing.
    /// </para>
    /// </remarks>
    private async Task RunHandlerAsync(
        IServiceScope scope,
        IOutboxMessageHandler handler,
        OutboxDelivery delivery,
        CancellationToken cancellationToken)
    {
        var consumer = ConsumerContext(scope, handler);

        var alreadyHandled = await consumer.InboxMessages.AnyAsync(
            entry => entry.MessageId == delivery.MessageId && entry.HandlerName == handler.HandlerName,
            cancellationToken);

        if (alreadyHandled)
        {
            return;
        }

        var strategy = consumer.Database.CreateExecutionStrategy();

        await strategy.ExecuteAsync(async () =>
        {
            await using var transaction = await consumer.Database.BeginTransactionAsync(cancellationToken);

            await handler.HandleAsync(delivery, cancellationToken);

            consumer.InboxMessages.Add(new Entities.InboxMessage
            {
                MessageId = delivery.MessageId,
                HandlerName = handler.HandlerName,
                ProcessedAt = clock.UtcNow,
            });

            await consumer.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        });
    }

    private ModuleDbContext ConsumerContext(IServiceScope scope, IOutboxMessageHandler handler)
    {
        var registration = registry.Registrations
            .FirstOrDefault(module => string.Equals(
                module.Schema, handler.Schema, StringComparison.Ordinal))
            ?? throw new InvalidOperationException(
                $"The outbox handler '{handler.HandlerName}' records itself in schema "
                + $"'{handler.Schema}', which no registered module context owns. A handler's inbox row "
                + "has to commit with the writes it records, so it must name the schema it writes to.");

        return (ModuleDbContext)scope.ServiceProvider.GetRequiredService(registration.ContextType);
    }

    private async Task FailAsync(
        ModuleDbContext producer,
        string schema,
        OutboxDelivery delivery,
        Exception exception,
        CancellationToken cancellationToken)
    {
        // The message text is kept, never the payload: a failure diagnostic must not become a second
        // copy of personal data in a column nobody thinks of as personal data.
        var error = Truncate(exception.GetType().Name + ": " + exception.Message, 2000);

        if (delivery.AttemptCount >= options.Value.MaximumAttempts)
        {
            PersistenceLog.OutboxDeadLettered(logger, delivery.MessageId);

            var deadLetter = $$"""
                UPDATE {{schema}}.outbox_messages
                   SET dead_lettered_at = {0}, lease_owner = NULL, lease_expires_at = NULL,
                       last_error = {1}
                 WHERE id = {2}
                """;

            await producer.Database.ExecuteSqlRawAsync(
                deadLetter, [clock.UtcNow, error, delivery.MessageId], cancellationToken);
            return;
        }

        PersistenceLog.OutboxRetry(logger, delivery.MessageId, delivery.AttemptCount, exception);

        var delay = NextDelay(delivery.AttemptCount);

        var retry = $$"""
            UPDATE {{schema}}.outbox_messages
               SET available_at = {0}, lease_owner = NULL, lease_expires_at = NULL,
                   last_error = {1}
             WHERE id = {2}
            """;

        await producer.Database.ExecuteSqlRawAsync(
            retry, [clock.UtcNow + delay, error, delivery.MessageId], cancellationToken);
    }

    /// <summary>
    /// Exponential backoff with jitter. The jitter matters when a downstream provider recovers: without
    /// it every message that failed during the outage would retry in the same instant and knock it over
    /// again.
    /// </summary>
    private TimeSpan NextDelay(int attemptCount)
    {
        var exponent = Math.Min(attemptCount, 10);
        var baseDelay = TimeSpan.FromTicks(options.Value.RetryBaseDelay.Ticks * (long)Math.Pow(2, exponent - 1));
        var capped = baseDelay > options.Value.RetryMaximumDelay ? options.Value.RetryMaximumDelay : baseDelay;
        var jitter = System.Security.Cryptography.RandomNumberGenerator.GetInt32(0, (int)capped.TotalMilliseconds + 1);
        return capped + TimeSpan.FromMilliseconds(jitter / 2.0);
    }

    private static string Truncate(string value, int maxLength)
        => value.Length <= maxLength ? value : value[..maxLength];
}
