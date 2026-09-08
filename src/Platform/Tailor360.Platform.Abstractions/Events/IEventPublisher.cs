namespace Tailor360.Platform.Abstractions.Events;

/// <summary>
/// Publishes an integration event into the caller's unit of work.
/// </summary>
/// <remarks>
/// <para>
/// <strong>The event is written by the same save that writes the change it describes.</strong> The
/// implementation adds a row to the module's own <c>outbox_messages</c> table on the module's own
/// context, so committing the aggregate commits the event and rolling the aggregate back discards it.
/// Nothing in the application sends an event to a consumer; the worker delivers from the row
/// afterwards.
/// </para>
/// <para>
/// <strong>There is no <c>await</c> here, and that is the contract rather than an oversight.</strong>
/// An asynchronous publish invites a second round trip, and a second round trip on a second connection
/// is how this became two transactions in the first place
/// (<see href="https://github.com/MK-AIFy/HyFib-Tailor360/issues/77">#77</see>). Adding a tracked row
/// costs nothing and cannot fail on its own; the failure that matters belongs to the save.
/// </para>
/// <para>
/// A module resolves <em>its own</em> publisher, through a port its <c>Application</c> project
/// declares — the publisher is bound to one module's context, so it can only ever write to that
/// module's outbox. That is what keeps publishing inside the schema boundary ARCH-005 draws.
/// </para>
/// </remarks>
public interface IEventPublisher
{
    /// <summary>
    /// Adds an event to the current unit of work. It is committed by the next save on the same
    /// context, and discarded with it if that save is rolled back.
    /// </summary>
    /// <param name="integrationEvent">The fact to publish.</param>
    void Publish(IIntegrationEvent integrationEvent);
}
