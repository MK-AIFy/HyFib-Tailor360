using Tailor360.Modules.Catalog.Application.Abstractions;
using Tailor360.Platform.Abstractions.Time;
using Tailor360.Platform.Persistence.Outbox;

namespace Tailor360.Modules.Catalog.Infrastructure.Persistence;

/// <summary>
/// The Catalog module's event publisher, bound to the Catalog context.
/// </summary>
/// <remarks>
/// The type parameter is what decides which schema's <c>outbox_messages</c> a published event lands
/// in, and it is the same context the module's store holds — so the event and the change commit
/// together or not at all (issue #77).
/// </remarks>
/// <param name="context">The module's context, shared with the module's store.</param>
/// <param name="clock">The clock.</param>
/// <param name="correlation">The correlation identifier to stamp on the message.</param>
public sealed class CatalogEventPublisher(
    CatalogDbContext context,
    IClock clock,
    IOutboxCorrelation correlation)
    : ModuleEventPublisher<CatalogDbContext>(context, clock, correlation), ICatalogEventPublisher;
