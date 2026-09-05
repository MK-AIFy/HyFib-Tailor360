using Tailor360.Platform.Abstractions.Ports;

namespace Tailor360.Modules.Identity.Application.Abstractions;

/// <summary>
/// Hands a rendered message off for delivery without waiting for it. Handlers enqueue; something else
/// sends.
/// </summary>
/// <remarks>
/// This indirection is not tidiness, it is the anti-enumeration control. A request to recover an
/// account must take the same time whether or not the account exists, and an SMTP conversation with a
/// relay takes anything from a few milliseconds to a few seconds. If the handler awaited the send, the
/// response time would say plainly which addresses are registered, and no amount of identical wording
/// would hide it. So the send happens after the response, and the only thing on the request path is a
/// bounded enqueue that costs the same either way.
/// <para>
/// The shipped queue is in-process, which means a message can be lost if the host stops between the
/// enqueue and the send. That is the correct trade for this release — the person simply asks again —
/// and #47 replaces it with the transactional outbox, at which point delivery survives a restart.
/// </para>
/// </remarks>
public interface IEmailDispatchQueue
{
    /// <summary>
    /// Queues one message. Returns <see langword="false"/> when the queue is full, which the caller
    /// treats as a delivery failure to log — never as a reason to answer the caller differently.
    /// </summary>
    bool Enqueue(EmailMessage message);
}
