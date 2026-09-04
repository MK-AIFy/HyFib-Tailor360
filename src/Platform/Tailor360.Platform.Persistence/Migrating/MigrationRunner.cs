using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace Tailor360.Platform.Persistence.Migrating;

/// <summary>
/// Applies migrations for every module under a single database-wide advisory lock, so that two
/// instances starting at once cannot both try to change the schema. The lock is held for the whole run
/// rather than per context, because a half-migrated database is the state hardest to recover from.
/// </summary>
/// <param name="services">The service provider used to resolve each context.</param>
/// <param name="registry">The ordered context registry.</param>
/// <param name="logger">Logger.</param>
public sealed class MigrationRunner(
    IServiceProvider services,
    ModuleContextRegistry registry,
    ILogger<MigrationRunner> logger)
{
    /// <summary>
    /// The advisory lock key. An arbitrary but fixed number; it only has to be the same in every
    /// process that migrates this database.
    /// </summary>
    public const long AdvisoryLockKey = 736001;

    /// <summary>Applies every outstanding migration.</summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>What was applied, per module.</returns>
    public async Task<IReadOnlyList<MigrationOutcome>> MigrateAsync(CancellationToken cancellationToken = default)
    {
        var outcomes = new List<MigrationOutcome>();

        using var scope = services.CreateScope();
        var lockContext = ResolveContext(scope.ServiceProvider, registry.Registrations[0].ContextType);
        var connection = lockContext.Database.GetDbConnection();

        await lockContext.Database.OpenConnectionAsync(cancellationToken);
        try
        {
            await ExecuteAsync(connection, "SELECT pg_advisory_lock(@key)", cancellationToken);
            PersistenceLog.MigrationLockAcquired(logger);

            foreach (var registration in registry.Registrations)
            {
                using var contextScope = services.CreateScope();
                var context = ResolveContext(contextScope.ServiceProvider, registration.ContextType);

                var pending = (await context.Database.GetPendingMigrationsAsync(cancellationToken)).ToArray();
                if (pending.Length == 0)
                {
                    outcomes.Add(new MigrationOutcome(registration.Schema, [], AlreadyUpToDate: true));
                    continue;
                }

                PersistenceLog.ApplyingMigrations(logger, pending.Length, registration.Schema);

                await context.Database.MigrateAsync(cancellationToken);
                outcomes.Add(new MigrationOutcome(registration.Schema, pending, AlreadyUpToDate: false));
            }
        }
        finally
        {
            await ExecuteAsync(connection, "SELECT pg_advisory_unlock(@key)", CancellationToken.None);
            await lockContext.Database.CloseConnectionAsync();
            PersistenceLog.MigrationLockReleased(logger);
        }

        return outcomes;
    }

    /// <summary>
    /// Checks that the database carries every migration this build knows about. Migrations the build
    /// does not know about are tolerated: that is the state during a rollback to release N while the
    /// database is still at N+1, and refusing to start then would turn a rollback into an outage.
    /// Comparing the two sets for equality would do exactly that, so the check is one-directional.
    /// </summary>
    public async Task<MigrationState> GetStateAsync(CancellationToken cancellationToken = default)
    {
        var pendingBySchema = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
        var unknownBySchema = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);

        foreach (var registration in registry.Registrations)
        {
            using var scope = services.CreateScope();
            var context = ResolveContext(scope.ServiceProvider, registration.ContextType);

            var known = context.Database.GetMigrations().ToHashSet(StringComparer.Ordinal);
            var applied = (await context.Database.GetAppliedMigrationsAsync(cancellationToken))
                .ToHashSet(StringComparer.Ordinal);

            var pending = known.Except(applied, StringComparer.Ordinal)
                .OrderBy(m => m, StringComparer.Ordinal).ToArray();
            var unknown = applied.Except(known, StringComparer.Ordinal)
                .OrderBy(m => m, StringComparer.Ordinal).ToArray();

            if (pending.Length > 0)
            {
                pendingBySchema[registration.Schema] = pending;
            }

            if (unknown.Length > 0)
            {
                unknownBySchema[registration.Schema] = unknown;
            }
        }

        return new MigrationState(pendingBySchema, unknownBySchema);
    }

    private static DbContext ResolveContext(IServiceProvider provider, Type contextType)
        => (DbContext)provider.GetRequiredService(contextType);

    private static async Task ExecuteAsync(
        System.Data.Common.DbConnection connection,
        string sql,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;

        var parameter = new NpgsqlParameter("key", AdvisoryLockKey);
        command.Parameters.Add(parameter);

        await command.ExecuteScalarAsync(cancellationToken);
    }
}

/// <summary>What a migration run did to one module.</summary>
/// <param name="Schema">The module schema.</param>
/// <param name="Applied">The migrations applied.</param>
/// <param name="AlreadyUpToDate">True when nothing needed applying.</param>
public sealed record MigrationOutcome(string Schema, IReadOnlyList<string> Applied, bool AlreadyUpToDate);

/// <summary>How the database compares with this build.</summary>
/// <param name="PendingBySchema">Migrations this build knows that the database does not have.</param>
/// <param name="UnknownBySchema">Migrations the database has that this build does not know.</param>
public sealed record MigrationState(
    IReadOnlyDictionary<string, IReadOnlyList<string>> PendingBySchema,
    IReadOnlyDictionary<string, IReadOnlyList<string>> UnknownBySchema)
{
    /// <summary>True when the build can serve: nothing it knows about is missing from the database.</summary>
    public bool CanServe => PendingBySchema.Count == 0;

    /// <summary>True when the database is ahead of this build, which happens during a rollback.</summary>
    public bool DatabaseIsAhead => UnknownBySchema.Count > 0;
}
