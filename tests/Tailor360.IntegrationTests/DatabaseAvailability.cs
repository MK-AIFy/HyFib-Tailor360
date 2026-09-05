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

    /// <summary>True when a usable PostgreSQL instance was found.</summary>
    public static bool IsAvailable => AvailableLazy.Value;

    /// <summary>True when the run is in continuous integration, where a skip is not acceptable.</summary>
    public static bool IsContinuousIntegration =>
        string.Equals(Environment.GetEnvironmentVariable("CI"), "true", StringComparison.OrdinalIgnoreCase);

    /// <summary>The connection string to use, or null when none is configured.</summary>
    public static string? ConnectionString =>
        Environment.GetEnvironmentVariable(ConnectionStringVariable) is { Length: > 0 } value ? value : null;

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
