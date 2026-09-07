using System.Collections.Concurrent;
using System.Reflection;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Tailor360.Platform.Persistence.Migrating;

/// <summary>
/// The version of the database shape a build carries: the timestamp of the newest migration across
/// every registered module schema.
/// </summary>
/// <remarks>
/// <para>
/// It answers one question for a client that has been running since before a deployment — "is the
/// server's data shape still the one my cached payloads were built against?" — which the build hash
/// cannot answer, because most deployments change code and not schema, and a client that discarded its
/// state on every deployment would discard it constantly.
/// </para>
/// <para>
/// <b>The value is the timestamp prefix, not the whole migration identifier.</b> A migration is named
/// <c>20260906170048_IdempotencyInFlightLease</c>; the name after the underscore is a feature the team
/// is working on, and <c>GET /api/version</c> is anonymous. The prefix orders exactly as the identifier
/// does and says nothing.
/// </para>
/// <para>
/// It is read from the assemblies, never from the database. A migration a build carries is a fact about
/// the build; asking the database instead would make an anonymous endpoint depend on a connection, and
/// would report the shape of whatever the database is mid-deployment rather than the shape this build
/// expects.
/// </para>
/// </remarks>
public static class SchemaVersion
{
    /// <summary>What is reported when a build carries no migrations at all.</summary>
    public const string None = "0";

    private static readonly ConcurrentDictionary<Assembly, string> Cache = new();

    /// <summary>
    /// The newest migration timestamp across every context in the registry, or <see cref="None"/> when
    /// none of them carries a migration.
    /// </summary>
    /// <param name="registry">The registered module contexts.</param>
    public static string Of(ModuleContextRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(registry);

        var newest = None;
        foreach (var registration in registry.Registrations)
        {
            var candidate = Cache.GetOrAdd(registration.ContextType.Assembly, NewestIn);
            if (string.CompareOrdinal(candidate, newest) > 0)
            {
                newest = candidate;
            }
        }

        return newest;
    }

    /// <summary>The newest migration timestamp declared in one assembly.</summary>
    /// <param name="assembly">The assembly holding a module's migrations.</param>
    private static string NewestIn(Assembly assembly)
    {
        var newest = None;

        // `GetCustomAttribute` rather than a name convention: the identifier a migration is applied
        // under is the one in its attribute, and a file renamed without its attribute would otherwise
        // report a version the database will never hold.
        foreach (var type in assembly.GetTypes())
        {
            var id = type.GetCustomAttribute<MigrationAttribute>()?.Id;
            if (id is null)
            {
                continue;
            }

            var timestamp = TimestampOf(id);
            if (string.CompareOrdinal(timestamp, newest) > 0)
            {
                newest = timestamp;
            }
        }

        return newest;
    }

    /// <summary>
    /// The ordered part of a migration identifier: everything before the first underscore, which the
    /// EF Core tooling writes as a 14-digit UTC timestamp.
    /// </summary>
    private static string TimestampOf(string migrationId)
    {
        var underscore = migrationId.IndexOf('_', StringComparison.Ordinal);
        var prefix = underscore < 0 ? migrationId : migrationId[..underscore];

        // A hand-written identifier that is not a timestamp is reported as no version rather than as a
        // version that sorts unpredictably against the generated ones.
        return prefix.Length == 14 && prefix.All(char.IsAsciiDigit) ? prefix : None;
    }
}
