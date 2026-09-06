using System.CommandLine;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Tailor360.Modules.Identity.Application.Access;
using Tailor360.Platform.Persistence.Contexts;

namespace Tailor360.Cli.Commands;

/// <summary>
/// Creates or refreshes the reference data every installation needs. It is idempotent and safe to run
/// in production, and it is the only seeding command that is; anything synthetic belongs to
/// <c>seed-synthetic</c>, which refuses to run there at all.
/// </summary>
public static class InitReferenceDataCommand
{
    /// <summary>How many future months of audit partitions are kept ahead.</summary>
    public const int AuditPartitionsAhead = 3;

    /// <summary>Builds the command.</summary>
    public static Command Create()
    {
        var organisation = new Option<Guid>("--organisation")
        {
            Description =
                "The organisation to seed roles for. Defaults to the single-organisation identifier.",
            DefaultValueFactory = _ => OrganisationDefaults.OrganisationId,
        };

        var command = new Command(
            "init-reference-data",
            "Create or refresh the reference data every installation needs. Idempotent and production-safe.")
        {
            organisation,
        };

        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var organisationId = parseResult.GetValue(organisation);

            using var host = CliHost.Build();
            using var scope = host.Services.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<PlatformDbContext>();
            var seeder = scope.ServiceProvider.GetRequiredService<IIdentityReferenceDataSeeder>();

            Console.WriteLine($"Environment: {EnvironmentGuard.CurrentEnvironment}");

            // Partitions are created ahead of time rather than on first write, so that the first audit
            // entry after midnight on the first of a month is not the thing that discovers a missing
            // partition.
            for (var month = 0; month <= AuditPartitionsAhead; month++)
            {
                await context.Database.ExecuteSqlAsync(
                    $"SELECT platform.ensure_audit_partition(now() + ({month} * interval '1 month'))",
                    cancellationToken);
            }

            Console.WriteLine(
                $"Audit partitions ensured for the current month and the next {AuditPartitionsAhead}.");

            var roles = await seeder.SeedSystemRolesAsync(organisationId, cancellationToken);

            Console.WriteLine($"Organisation: {organisationId}");
            Console.WriteLine(
                $"System roles: {roles.RolesCreated} created, {roles.RolesUpdated} updated, "
                + $"{roles.RolesUnchanged} already current.");
            Console.WriteLine(
                $"Default grants: {roles.PermissionsGranted} added, {roles.PermissionsRevoked} removed "
                + "to match docs/security/permission-matrix.md.");
            Console.WriteLine(
                "The default service catalogue is added by issue #29; this command grows with it and "
                + "stays idempotent.");

            return ExitCodes.Success;
        });

        return command;
    }
}
