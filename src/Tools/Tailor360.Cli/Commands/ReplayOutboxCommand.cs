using System.CommandLine;
using Microsoft.Extensions.DependencyInjection;
using Tailor360.Platform.Abstractions.Outbox;

namespace Tailor360.Cli.Commands;

/// <summary>
/// Returns dead-lettered outbox messages to the queue. This is an operator action with a real effect on
/// consumers, so it demands a reason and writes an audit entry.
/// </summary>
/// <remarks>
/// The work itself lives in <c>IOutboxAdministration</c>, which the administration endpoint calls as
/// well, so a console replay and an endpoint replay do the same thing to the same rows and write the
/// same entry. This command is the argument parsing and nothing else.
/// </remarks>
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

            if (id is not null && replayAll)
            {
                Console.Error.WriteLine("Supply --id <guid> or --dead-letter, not both.");
                return ExitCodes.Failure;
            }

            using var host = CliHost.Build();
            using var scope = host.Services.CreateScope();
            var outbox = scope.ServiceProvider.GetRequiredService<IOutboxAdministration>();

            if (id is null)
            {
                var drained = await outbox.ReplayAllAsync(why, cancellationToken: cancellationToken);
                Console.WriteLine($"Replayed {drained} message(s).");
                return ExitCodes.Success;
            }

            var result = await outbox.ReplayAsync(id.Value, why, cancellationToken: cancellationToken);

            if (result.IsFailure)
            {
                Console.Error.WriteLine(result.Error.Message);
                return ExitCodes.Failure;
            }

            Console.WriteLine(
                $"Replayed {result.Value.EventType} message {result.Value.Id} " +
                $"after {result.Value.AttemptCount} failed attempt(s).");
            return ExitCodes.Success;
        });

        return command;
    }
}
