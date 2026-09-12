using Tailor360.Platform.Abstractions.Events;

namespace Tailor360.Modules.Billing.Application.Abstractions;

/// <summary>
/// Billing's own outbox publisher: an event published through it is a row in <c>billing.outbox_messages</c>,
/// tracked by the same change tracker as the invoice and committed by the same save
/// (<c>docs/platform/outbox.md</c>). Its own port rather than <see cref="IEventPublisher"/> because the host
/// composes every module at once and one interface would resolve to whichever module registered last.
/// </summary>
public interface IBillingEventPublisher : IEventPublisher;
