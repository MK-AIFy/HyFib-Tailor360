using Tailor360.Platform.Abstractions.Events;

namespace Tailor360.Modules.Orders.Application.Abstractions;

/// <summary>
/// This module's event publisher, over this module's outbox.
/// </summary>
/// <remarks>
/// <para>
/// A port of the module's own rather than <see cref="IEventPublisher"/> itself, because that interface is shared
/// and the web host composes every module into one container: a single registration of it would leave whichever
/// module registered last writing everybody's events, into its own schema. Each module declares one of these and
/// binds it to a publisher over its own context, so a module can only ever write to its own outbox (issue #77).
/// </para>
/// <para>
/// It is declared here, in <c>Application</c>, because <c>Infrastructure</c> implements it and the composition
/// root binds the two — the shape <c>src/Modules/CLAUDE.md</c> section 5 describes and
/// <c>docs/platform/outbox.md</c> explains.
/// </para>
/// <para>
/// <strong>It shares this module's context with the module's stores</strong>, both being scoped, so an event
/// published here is committed by the next save on any of them and discarded if that save rolls back. That is the
/// whole guarantee, and it is why the publisher is bound to a context rather than resolved from thin air.
/// </para>
/// </remarks>
public interface IOrdersEventPublisher : IEventPublisher;
