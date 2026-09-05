using Microsoft.Extensions.Logging;

namespace Tailor360.Modules.Identity.Infrastructure.Email;

/// <summary>
/// Source-generated log messages for outbound email.
/// </summary>
/// <remarks>
/// <para>
/// Not one of these carries a recipient address, a subject, a body or a token. A message identifier is
/// the template key and nothing more, which is enough to tell a support engineer that recovery messages
/// are failing without telling them whose.
/// </para>
/// <para>
/// The module's event identifiers are allocated 2301-2313 to <c>IdentityLog</c>, 2320-2331 to
/// <c>AuthenticationLog</c> and 2340 upwards here. They were reused once, and the symptom was an
/// operator alerting on one identifier and receiving two unrelated events, one of them on the sign-in
/// path — so the ranges are written down rather than remembered.
/// </para>
/// </remarks>
internal static partial class IdentityEmailLog
{
    [LoggerMessage(EventId = 2340, Level = LogLevel.Debug,
        Message = "Delivered a {Tag} message.")]
    public static partial void Delivered(ILogger logger, string tag);

    [LoggerMessage(EventId = 2341, Level = LogLevel.Error,
        Message = "A {Tag} message could not be delivered to the configured relay.")]
    public static partial void DeliveryFailed(ILogger logger, string tag, Exception exception);

    [LoggerMessage(EventId = 2342, Level = LogLevel.Warning,
        Message = "The outbound mail queue is full; a {Tag} message was dropped. " +
                  "The relay is not keeping up or is not answering.")]
    public static partial void QueueFull(ILogger logger, string tag);

    [LoggerMessage(EventId = 2343, Level = LogLevel.Information,
        Message = "Outbound mail is set to collect in memory; nothing will be delivered.")]
    public static partial void CollectingOnly(ILogger logger);
}
