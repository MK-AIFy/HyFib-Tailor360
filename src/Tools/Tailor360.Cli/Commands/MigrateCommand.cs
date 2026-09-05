using System.CommandLine;
using Microsoft.Extensions.DependencyInjection;
using Tailor360.Platform.Persistence.Migrating;

namespace Tailor360.Cli.Commands;

/// <summary>
/// Applies outstanding database migrations. Migrations are applied by this command rather than by the
/// application at start-up, so that a rolling deployment cannot have two versions racing to change the
/// schema, and so that the migrator runs as a role holding schema rights the application does not have.
/// </summary>
public static class MigrateCommand
{
    /// <summary>Builds the command.</summary>
    public static Command Create()
    {
        var dryRun = new Option<bool>("--dry-run")
        {
            Description = "List the migrations that would be applied without applying them.",
        };

        var command = new Command("migrate", "Apply outstanding database migrations.")
        {
            dryRun,
        };

        command.SetAction(async (parseResult, cancellationToken) =>
        {
            using var host = CliHost.Build();
            using var scope = host.Services.CreateScope();
            var runner = scope.ServiceProvider.GetRequiredService<MigrationRunner>();

            if (parseResult.GetValue(dryRun))
            {
                var state = await runner.GetStateAsync(cancellationToken);

                if (state.PendingBySchema.Count == 0)
                {
                    Console.WriteLine("The database is up to date with this build.");
                }

                foreach (var (schema, migrations) in state.PendingBySchema)
                {
                    Console.WriteLine($"{schema}: {migrations.Count} migration(s) would be applied");
                    foreach (var migration in migrations)
                    {
                        Console.WriteLine($"  {migration}");
                    }
                }

                if (state.DatabaseIsAhead)
                {
                    Console.WriteLine(
                        "The database also carries migrations this build does not know. That is expected " +
                        "during a rollback to an earlier release and is not an error.");
                }

                return ExitCodes.Success;
            }

            var outcomes = await runner.MigrateAsync(cancellationToken);
            foreach (var outcome in outcomes)
            {
                Console.WriteLine(outcome.AlreadyUpToDate
                    ? $"{outcome.Schema}: already up to date"
                    : $"{outcome.Schema}: applied {outcome.Applied.Count} migration(s)");
            }

            return ExitCodes.Success;
        });

        return command;
    }
}
