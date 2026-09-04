using System.CommandLine;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Tailor360.Platform.Abstractions.Auditing;
using Tailor360.Platform.Abstractions.Time;
using Tailor360.Platform.Persistence.Contexts;
using Tailor360.Platform.Persistence.Entities;

namespace Tailor360.Cli.Commands;

/// <summary>
/// Reads and changes feature flag values. Like the outbox replay, a flag change is an operational act
/// with user-visible consequences, so it demands a reason and is audited.
/// </summary>
public static class FlagsCommand
{
    /// <summary>Builds the command.</summary>
    public static Command Create()
    {
        var command = new Command("flags", "Read and change feature flag values.")
        {
            CreateListCommand(),
            CreateSetCommand(),
        };

        return command;
    }

    private static Command CreateListCommand()
    {
        var command = new Command("list", "List every configured flag value.");

        command.SetAction(async (_, cancellationToken) =>
        {
            using var host = CliHost.Build();
            using var scope = host.Services.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<PlatformDbContext>();

            var flags = await context.FeatureFlags.AsNoTracking()
                .OrderBy(f => f.Key).ThenBy(f => f.ScopeType)
                .ToListAsync(cancellationToken);

            if (flags.Count == 0)
            {
                Console.WriteLine("No flag values are configured. Every flag therefore evaluates to off.");
                return ExitCodes.Success;
            }

            Console.WriteLine($"{"KEY",-40} {"SCOPE",-14} {"ENABLED",-8} VERSION");
            foreach (var flag in flags)
            {
                var scopeLabel = flag.ScopeType == FeatureFlagScopes.Branch
                    ? $"branch {flag.ScopeId}"
                    : "organisation";
                Console.WriteLine($"{flag.Key,-40} {scopeLabel,-14} {flag.Enabled,-8} {flag.Version}");
            }

            return ExitCodes.Success;
        });

        return command;
    }

    private static Command CreateSetCommand()
    {
        var key = new Argument<string>("key") { Description = "The flag key." };
        var value = new Argument<bool>("enabled") { Description = "Whether the feature is on." };

        var scope = new Option<string>("--scope")
        {
            Description = "organisation or branch.",
            DefaultValueFactory = _ => FeatureFlagScopes.Organisation,
        };

        var branch = new Option<Guid?>("--branch")
        {
            Description = "The branch the value applies to, when the scope is branch.",
        };

        var reason = new Option<string>("--reason")
        {
            Description = "Why the value is being changed. Recorded in the audit trail.",
            Required = true,
        };

        var command = new Command("set", "Set a flag value.") { key, value, scope, branch, reason };

        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var flagKey = parseResult.GetValue(key)!;
            var enabled = parseResult.GetValue(value);
            var scopeType = parseResult.GetValue(scope)!;
            var branchId = parseResult.GetValue(branch);
            var why = parseResult.GetValue(reason) ?? string.Empty;

            if (!FlagScopeSelection.TryResolve(scopeType, branchId, out var scopeId, out var error))
            {
                Console.Error.WriteLine(error);
                return ExitCodes.Failure;
            }

            using var host = CliHost.Build();
            using var hostScope = host.Services.CreateScope();
            var context = hostScope.ServiceProvider.GetRequiredService<PlatformDbContext>();
            var audit = hostScope.ServiceProvider.GetRequiredService<IAuditWriter>();
            var clock = hostScope.ServiceProvider.GetRequiredService<IClock>();

            var existing = await context.FeatureFlags.SingleOrDefaultAsync(
                f => f.Key == flagKey && f.ScopeType == scopeType && f.ScopeId == scopeId,
                cancellationToken);

            var before = existing?.Enabled;

            if (existing is null)
            {
                context.FeatureFlags.Add(new FeatureFlag
                {
                    Key = flagKey,
                    ScopeType = scopeType,
                    ScopeId = scopeId,
                    Enabled = enabled,
                    Version = 1,
                    UpdatedAt = clock.UtcNow,
                    Reason = why,
                });
            }
            else
            {
                existing.Enabled = enabled;
                existing.Version += 1;
                existing.UpdatedAt = clock.UtcNow;
                existing.Reason = why;
            }

            await audit.WriteAsync(
                new AuditEntry(
                    "platform.feature_flag.changed",
                    "FeatureFlag",
                    scopeId,
                    $"Flag '{flagKey}' set to {enabled} for {scopeType}.",
                    why,
                    before,
                    enabled),
                cancellationToken);

            await context.SaveChangesAsync(cancellationToken);

            Console.WriteLine($"Flag '{flagKey}' is now {(enabled ? "on" : "off")} for {scopeType}.");
            Console.WriteLine(
                "Other nodes pick the change up within the configured propagation bound (30 seconds by default).");
            return ExitCodes.Success;
        });

        return command;
    }
}
