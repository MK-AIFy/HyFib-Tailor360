using Tailor360.Modules.Billing.Application.Abstractions;
using Tailor360.Platform.Abstractions.Time;
using Tailor360.Platform.Persistence.Outbox;

namespace Tailor360.Modules.Billing.Infrastructure.Persistence;

/// <summary>Billing's outbox publisher over its own context: the event commits with the invoice.</summary>
/// <param name="context">The module's context.</param>
/// <param name="clock">The clock.</param>
/// <param name="correlation">The request's correlation, stamped on the row.</param>
public sealed class BillingEventPublisher(
    BillingDbContext context,
    IClock clock,
    IOutboxCorrelation correlation)
    : ModuleEventPublisher<BillingDbContext>(context, clock, correlation), IBillingEventPublisher;
