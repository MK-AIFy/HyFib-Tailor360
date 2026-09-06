using System.Globalization;
using Npgsql;

namespace Tailor360.IntegrationTests;

/// <summary>
/// Decides whether database-backed integration tests can run here. Three environments have to be
/// served: a developer machine with Docker, a cloud coding session with a PostgreSQL instance but no
/// Docker daemon, and continuous integration. A test that cannot run is skipped with a visible reason
/// locally, but under <c>CI=true</c> the same condition is a failure, so nothing merges unverified.
/// </summary>
public static class DatabaseAvailability
{
    /// <summary>The connection string an environment may supply instead of a container.</summary>
    public const string ConnectionStringVariable = "TAILOR360_TEST_DATABASE_URL";

    /// <summary>The object-storage endpoint an environment may supply instead of a container.</summary>
    public const string ObjectStorageVariable = "TAILOR360_TEST_S3_ENDPOINT";

    private static readonly Lazy<bool> AvailableLazy = new(Probe);
    private static readonly Lazy<string> NamespaceLazy = new(BuildNamespace);

    /// <summary>True when a usable PostgreSQL instance was found.</summary>
    public static bool IsAvailable => AvailableLazy.Value;

    /// <summary>True when the run is in continuous integration, where a skip is not acceptable.</summary>
    public static bool IsContinuousIntegration =>
        string.Equals(Environment.GetEnvironmentVariable("CI"), "true", StringComparison.OrdinalIgnoreCase);

    /// <summary>The connection string to use, or null when none is configured.</summary>
    public static string? ConnectionString =>
        Environment.GetEnvironmentVariable(ConnectionStringVariable) is { Length: > 0 } value ? value : null;

    /// <summary>
    /// The prefix every database this run creates is named under.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Every fixture here creates its databases with <c>DROP DATABASE ... WITH (FORCE)</c>, which
    /// terminates <em>another process's</em> live backends on a database of that name. With fixed names
    /// two runs against one cluster therefore destroy each other, and the failures land inside a
    /// collection fixture's <c>InitializeAsync</c> — a whole collection reporting red for a reason that
    /// has nothing to do with what it tests.
    /// </para>
    /// <para>
    /// The prefix is built from two things. The configured database name, so that
    /// <c>TAILOR360_TEST_DATABASE_URL</c> — the one knob a continuous-integration job has — isolates a
    /// run completely; and the process identifier, so that two runs sharing one connection string still
    /// do not collide. It is kept short because PostgreSQL truncates an identifier at 63 bytes and the
    /// per-fixture suffixes are appended to it.
    /// </para>
    /// </remarks>
    public static string DatabaseNamespace => NamespaceLazy.Value;

    /// <summary>
    /// Why the database-backed tests are being skipped, for the runner to display. A constant because
    /// the test framework needs it at attribute-construction time.
    /// </summary>
    public const string SkipMessage =
        "No PostgreSQL instance is reachable. Set TAILOR360_TEST_DATABASE_URL or start the compose " +
        "stack. Under CI=true this condition fails the run instead of skipping.";

    /// <summary>Why the database-backed tests are being skipped.</summary>
    public static string SkipReason => SkipMessage;

    /// <summary>
    /// Throws when the database is unavailable in continuous integration. Called from a fixture so that
    /// the run fails loudly rather than reporting a green build over skipped coverage.
    /// </summary>
    public static void EnsureAvailableInContinuousIntegration()
    {
        if (IsContinuousIntegration && !IsAvailable)
        {
            throw new InvalidOperationException(
                "Integration tests require a database when CI=true, but none was reachable. " + SkipReason);
        }
    }

    private static string BuildNamespace()
    {
        var configured = ConnectionString;
        var baseName = "tailor360";

        if (configured is not null)
        {
            try
            {
                var database = new NpgsqlConnectionStringBuilder(configured).Database;
                if (!string.IsNullOrWhiteSpace(database))
                {
                    baseName = database;
                }
            }
            catch (ArgumentException)
            {
                // An unparseable connection string is the Probe's problem to report, not this one's.
            }
        }

        var trimmed = baseName.Length > 16 ? baseName[..16] : baseName;
        return string.Create(CultureInfo.InvariantCulture, $"{trimmed}_{Environment.ProcessId}");
    }

    private static bool Probe()
    {
        var connectionString = ConnectionString;
        if (connectionString is null)
        {
            return false;
        }

        try
        {
            using var connection = new NpgsqlConnection(connectionString);
            connection.Open();
            using var command = new NpgsqlCommand("select 1", connection);
            return Convert.ToInt32(command.ExecuteScalar(), CultureInfo.InvariantCulture) == 1;
        }
        catch (NpgsqlException)
        {
            return false;
        }
        catch (TimeoutException)
        {
            return false;
        }
    }
}
