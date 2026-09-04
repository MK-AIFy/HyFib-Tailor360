using System.CommandLine;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Tailor360.Platform.Abstractions.Auditing;
using Tailor360.Platform.Persistence.Contexts;

namespace Tailor360.Cli.Commands;

/// <summary>
/// Returns dead-lettered outbox messages to the queue. This is an operator action with a real effect on
/// consumers, so it demands a reason and writes an audit entry. Issue #25 exposes the same operation
/// over HTTP behind step-up authorisation; until then it is deliberately console-only, because the
/// permission model that would guard an endpoint does not exist yet.
/// </summary>
public static class ReplayOutboxCommand
{
    /// <summary>Builds the command.</summary>
    public static Command Create()
    {
        var messageId = new Option<Guid?>("--id")
        {
            Description = "The identifier of a single message to replay.",
        };

        var deadLetter = new Option<bool>("--dead-letter")
        {
            Description = "Replay every dead-lettered message.",
        };

        var reason = new Option<string>("--reason")
        {
            Description = "Why the replay is being performed. Recorded in the audit trail.",
            Required = true,
        };

        var command = new Command("replay-outbox", "Return dead-lettered outbox messages to the queue.")
        {
            messageId,
            deadLetter,
            reason,
        };

        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var id = parseResult.GetValue(messageId);
            var replayAll = parseResult.GetValue(deadLetter);
            var why = parseResult.GetValue(reason) ?? string.Empty;

            if (id is null && !replayAll)
            {
                Console.Error.WriteLine("Supply either --id <guid> or --dead-letter.");
                return ExitCodes.Failure;
            }

            using var host = CliHost.Build();
            using var scope = host.Services.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<PlatformDbContext>();
            var audit = scope.ServiceProvider.GetRequiredService<IAuditWriter>();

            var replayed = id is null
                ? await context.Database.ExecuteSqlAsync(
                    $"""
                     UPDATE platform.outbox_messages
                        SET dead_lettered_at = NULL, attempt_count = 0, available_at = now(),
                            lease_owner = NULL, lease_expires_at = NULL
                      WHERE dead_lettered_at IS NOT NULL
                     """,
                    cancellationToken)
                : await context.Database.ExecuteSqlAsync(
                    $"""
                     UPDATE platform.outbox_messages
                        SET dead_lettered_at = NULL, attempt_count = 0, available_at = now(),
                            lease_owner = NULL, lease_expires_at = NULL
                      WHERE id = {id.Value} AND dead_lettered_at IS NOT NULL
                     """,
                    cancellationToken);

            await audit.WriteAsync(
                new AuditEntry(
                    "platform.outbox.replayed",
                    "OutboxMessage",
                    id ?? Guid.Empty,
                    $"Replayed {replayed} dead-lettered outbox message(s).",
                    why),
                cancellationToken);

            await context.SaveChangesAsync(cancellationToken);

            Console.WriteLine($"Replayed {replayed} message(s).");
            return ExitCodes.Success;
        });

        return command;
    }
}
