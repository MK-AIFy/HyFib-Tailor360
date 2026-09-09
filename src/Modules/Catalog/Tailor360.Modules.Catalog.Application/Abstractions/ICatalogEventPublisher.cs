using Tailor360.Platform.Abstractions.Events;

namespace Tailor360.Modules.Catalog.Application.Abstractions;

/// <summary>
/// This module's event publisher, over this module's outbox.
/// </summary>
/// <remarks>
/// A port of the module's own rather than <see cref="IEventPublisher"/> itself: that interface is
/// shared and the web host composes every module into one container, so a single registration of it
/// would leave whichever module registered last writing everybody's events into its own schema. Each
/// module declares one of these and binds it to a publisher over its own context (issue #77), so a
/// published event commits with the change that caused it and with nothing else.
/// </remarks>
public interface ICatalogEventPublisher : IEventPublisher;
