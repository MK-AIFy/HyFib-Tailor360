using Microsoft.Extensions.Hosting;

namespace Tailor360.Cli.Commands;

/// <summary>
/// Decides whether a synthetic-data command may run. Seeding synthetic data into production would put
/// fixed identifiers and test identities into live records, and no operator flag can make that safe, so
/// the refusal is unconditional and there is deliberately no override switch.
/// </summary>
public static class EnvironmentGuard
{
    /// <summary>
    /// The environment name this process is running in, resolved the same way the host resolves it so
    /// that the guard and the configuration can never disagree about which environment this is.
    /// </summary>
    public static string CurrentEnvironment => CliHost.ResolveEnvironmentName();

    /// <summary>True when the process is running in production.</summary>
    public static bool IsProduction =>
        string.Equals(CurrentEnvironment, Environments.Production, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Writes the refusal message and returns the exit code used when a synthetic-data command is
    /// invoked in production.
    /// </summary>
    public static int RefuseSyntheticDataInProduction(TextWriter error)
    {
        ArgumentNullException.ThrowIfNull(error);

        error.WriteLine("Refusing to write synthetic data: the environment is Production.");
        error.WriteLine(
            "Synthetic seeding uses fixed identifiers and test identities and is limited to development " +
            "and automated tests. There is no override. Use 'init-reference-data' to initialise the " +
            "reference data a production installation needs.");
        return ExitCodes.RefusedInProduction;
    }
}

/// <summary>Process exit codes, so that a script can branch on the reason rather than on the message.</summary>
public static class ExitCodes
{
    /// <summary>The command completed.</summary>
    public const int Success = 0;

    /// <summary>The command failed.</summary>
    public const int Failure = 1;

    /// <summary>The command was refused because it would write synthetic data to production.</summary>
    public const int RefusedInProduction = 3;

    /// <summary>The command was refused because it was run with the wrong database role.</summary>
    public const int WrongDatabaseRole = 4;
}
