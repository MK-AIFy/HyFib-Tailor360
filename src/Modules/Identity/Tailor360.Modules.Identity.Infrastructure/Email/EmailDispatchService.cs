using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Tailor360.Platform.Abstractions.Ports;

namespace Tailor360.Modules.Identity.Infrastructure.Email;

/// <summary>
/// Drains the dispatch queue and sends what it finds.
/// </summary>
/// <remarks>
/// It runs one message at a time on purpose. Recovery and security-alert messages arrive at the rate
/// people ask for them, which in a tailoring shop is a handful a day, and a single reader means a relay
/// that is slow produces a queue rather than a hundred simultaneous connections to a relay that is
/// already struggling.
/// <para>
/// A send that fails is logged and the message is dropped rather than retried. Retrying an SMTP failure
/// well needs backoff, a dead-letter and idempotency, which is what the outbox already does and what
/// #47 brings this onto; a half-built retry here would mostly succeed at sending the same recovery
/// link three times.
/// </para>
/// </remarks>
/// <param name="queue">The queue to drain.</param>
/// <param name="sender">The configured sender.</param>
/// <param name="logger">Logger. Never receives an address, a subject or a body.</param>
public sealed class EmailDispatchService(
    ChannelEmailDispatchQueue queue,
    IEmailSender sender,
    ILogger<EmailDispatchService> logger) : BackgroundService
{
    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var message in queue.Reader.ReadAllAsync(stoppingToken))
        {
            var tag = message.Tag ?? "unknown";

            try
            {
                var sent = await sender.SendAsync(message, stoppingToken);
                if (sent.IsSuccess)
                {
                    IdentityEmailLog.Delivered(logger, tag);
                }
                else
                {
                    // A relay that answers with a refusal rather than by throwing — a bad credential, a
                    // rejected sender, a full mailbox — reaches here as a failed Result. Without this
                    // branch the message is taken off the queue and nothing is written anywhere, so a
                    // misconfigured relay looks exactly like a working one and the first evidence is a
                    // person who never received their password-reset mail.
                    IdentityEmailLog.DeliveryRejected(logger, tag, sent.Error.Code);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                // The host is stopping. Anything still queued is lost, which is the documented
                // limitation of an in-process queue; #47 moves this onto the outbox.
                return;
            }
#pragma warning disable CA1031 // A sender's failure must not stop the loop that drains the queue.
            catch (Exception exception)
#pragma warning restore CA1031
            {
                IdentityEmailLog.DeliveryFailed(logger, tag, exception);
            }
        }
    }
}
