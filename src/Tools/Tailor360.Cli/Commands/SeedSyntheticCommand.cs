using System.CommandLine;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
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
            var clock = scope.ServiceProvider.GetRequiredService<IClock>();

            Console.WriteLine($"Environment: {EnvironmentGuard.CurrentEnvironment}");

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
                "Customers, catalogue, orders and billing fixtures are added by their own issues and " +
                "consolidated into the shared fixture library by issue #61a.");

            return ExitCodes.Success;
        });

        return command;
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
