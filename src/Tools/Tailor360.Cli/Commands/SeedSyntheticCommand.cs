using System.CommandLine;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Tailor360.Modules.Identity.Application.Access;
using Tailor360.Modules.Identity.Domain.Branches;
using Tailor360.Modules.Identity.Infrastructure.Persistence;
using Tailor360.Platform.Abstractions.Time;
using Tailor360.Platform.Persistence.Contexts;
using Tailor360.Platform.Persistence.Entities;

namespace Tailor360.Cli.Commands;

/// <summary>
/// Creates the deterministic dataset development and automated tests share: fixed identifiers, two
/// branches so that branch-scope rules can actually be exercised, and sample values per module as those
/// modules arrive. It refuses to run in production unconditionally, and there is no override flag —
/// fixed identifiers and test identities in live data is not a risk an operator can accept on the spot.
/// </summary>
public static class SeedSyntheticCommand
{
    /// <summary>The first synthetic branch. Fixed so that tests and fixtures can refer to it.</summary>
    public static Guid MainBranchId { get; } = Guid.Parse("0199a000-0000-7000-8000-000000000001");

    /// <summary>The second synthetic branch, which exists so branch scoping is testable.</summary>
    public static Guid SecondBranchId { get; } = Guid.Parse("0199a000-0000-7000-8000-000000000002");

    /// <summary>Builds the command.</summary>
    public static Command Create()
    {
        var command = new Command(
            "seed-synthetic",
            "Create the deterministic synthetic dataset. Development and test environments only.");

        command.SetAction(async (_, cancellationToken) =>
        {
            if (EnvironmentGuard.IsProduction)
            {
                return EnvironmentGuard.RefuseSyntheticDataInProduction(Console.Error);
            }

            using var host = CliHost.Build();
            using var scope = host.Services.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<PlatformDbContext>();
            var identity = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
            var clock = scope.ServiceProvider.GetRequiredService<IClock>();

            Console.WriteLine($"Environment: {EnvironmentGuard.CurrentEnvironment}");

            // The two branches come first: a branch assignment has to point at a branch, and the whole
            // point of seeding two is that branch scope can be exercised against a branch the actor is
            // not assigned to. The identifiers are fixed so that fixtures and the role walkthrough can
            // name them.
            await UpsertBranchAsync(
                identity, clock, MainBranchId, "MAIN", "Main Branch", cancellationToken);
            await UpsertBranchAsync(
                identity, clock, SecondBranchId, "SECOND", "Second Branch", cancellationToken);

            await identity.SaveChangesAsync(cancellationToken);

            // Upserting rather than inserting keeps the command repeatable, which is what makes it
            // usable as a test fixture as well as a development convenience.
            await UpsertFlagAsync(context, clock, "platform.diagnostics_panel", enabled: true,
                FeatureFlagScopes.Organisation, Guid.Empty, cancellationToken);
            await UpsertFlagAsync(context, clock, "platform.diagnostics_panel", enabled: false,
                FeatureFlagScopes.Branch, SecondBranchId, cancellationToken);

            await context.SaveChangesAsync(cancellationToken);

            Console.WriteLine("Synthetic feature flags seeded for the organisation and the second branch.");
            Console.WriteLine($"Branch identifiers: main {MainBranchId}, second {SecondBranchId}.");
            Console.WriteLine(
                "One representative user per role in each branch is created by the administration " +
                "screens of issue #25; docs/security/role-walkthrough.md scripts what to do with them.");
            Console.WriteLine(
                "Customers, catalogue, orders and billing fixtures are added by their own issues and " +
                "consolidated into the shared fixture library by issue #61a.");

            return ExitCodes.Success;
        });

        return command;
    }

    /// <summary>
    /// Creates a synthetic branch, or corrects its name if it drifted. Repeatable, like everything else
    /// in this command, so it doubles as a test fixture.
    /// </summary>
    private static async Task UpsertBranchAsync(
        IdentityDbContext context,
        IClock clock,
        Guid branchId,
        string code,
        string name,
        CancellationToken cancellationToken)
    {
        var now = clock.UtcNow;
        var existing = await context.Branches
            .SingleOrDefaultAsync(branch => branch.Id == branchId, cancellationToken);

        if (existing is not null)
        {
            var reconfigured = existing.Reconfigure(
                new BranchDetails(name, Branch.DefaultTimeZoneId), now, by: null);
            if (reconfigured.IsFailure)
            {
                throw new InvalidOperationException(
                    $"The synthetic branch {code} could not be refreshed: {reconfigured.Error.Message}");
            }

            return;
        }

        var opened = Branch.Open(
            branchId, OrganisationDefaults.OrganisationId, code, name, now, Branch.DefaultTimeZoneId);

        if (opened.IsFailure)
        {
            throw new InvalidOperationException(
                $"The synthetic branch {code} could not be created: {opened.Error.Message}");
        }

        context.Branches.Add(opened.Value);
    }

    private static async Task UpsertFlagAsync(
        PlatformDbContext context,
        IClock clock,
        string key,
        bool enabled,
        string scopeType,
        Guid scopeId,
        CancellationToken cancellationToken)
    {
        var existing = await context.FeatureFlags.SingleOrDefaultAsync(
            f => f.Key == key && f.ScopeType == scopeType && f.ScopeId == scopeId,
            cancellationToken);

        if (existing is null)
        {
            context.FeatureFlags.Add(new FeatureFlag
            {
                Key = key,
                ScopeType = scopeType,
                ScopeId = scopeId,
                Enabled = enabled,
                Version = 1,
                UpdatedAt = clock.UtcNow,
                Reason = "Synthetic development dataset.",
            });
            return;
        }

        existing.Enabled = enabled;
        existing.UpdatedAt = clock.UtcNow;
    }
}
