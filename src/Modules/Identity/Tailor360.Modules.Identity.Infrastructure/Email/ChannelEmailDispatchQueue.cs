using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Tailor360.Modules.Identity.Application.Abstractions;
using Tailor360.Platform.Abstractions.Ports;

namespace Tailor360.Modules.Identity.Infrastructure.Email;

/// <summary>
/// The in-process hand-off between a handler that has a message to send and the service that sends it.
/// </summary>
/// <remarks>
/// It is a bounded channel with <see cref="BoundedChannelFullMode.DropWrite"/>, which means a relay
/// that has stopped answering costs a fixed amount of memory and then starts refusing new messages
/// loudly, rather than growing until the host runs out. Dropping is the right failure for these
/// messages: a person whose recovery message was dropped asks again, and the alternative — blocking the
/// request thread on a queue — would hand an attacker a way to stall the sign-in path.
/// <para>
/// <b>This queue does not survive a restart.</b> A message enqueued and not yet sent when the host
/// stops is gone. That is an accepted limitation of this release, recorded here rather than in a
/// comment nobody finds: #47 moves delivery onto the transactional outbox, at which point a message is
/// committed with the change that caused it and redelivered after a restart.
/// </para>
/// </remarks>
/// <param name="options">Delivery configuration, which sets the bound.</param>
/// <param name="logger">Logger. Never receives an address, a subject or a body.</param>
public sealed class ChannelEmailDispatchQueue(
    IOptions<EmailDeliveryOptions> options,
    ILogger<ChannelEmailDispatchQueue> logger) : IEmailDispatchQueue
{
    private readonly Channel<EmailMessage> _channel = Channel.CreateBounded<EmailMessage>(
        new BoundedChannelOptions(
            (options ?? throw new ArgumentNullException(nameof(options))).Value.QueueCapacity)
        {
            SingleReader = true,
            SingleWriter = false,
            FullMode = BoundedChannelFullMode.DropWrite,
        });

    /// <summary>The messages waiting to be delivered.</summary>
    public ChannelReader<EmailMessage> Reader => _channel.Reader;

    /// <inheritdoc />
    public bool Enqueue(EmailMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);

        if (_channel.Writer.TryWrite(message))
        {
            return true;
        }

        IdentityEmailLog.QueueFull(logger, message.Tag ?? "unknown");
        return false;
    }
}
