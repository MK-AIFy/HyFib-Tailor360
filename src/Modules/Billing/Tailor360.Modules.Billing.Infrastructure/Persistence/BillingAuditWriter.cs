using Tailor360.Modules.Billing.Application.Abstractions;
using Tailor360.Platform.Abstractions.Auditing;
using Tailor360.Platform.Abstractions.Identifiers;
using Tailor360.Platform.Abstractions.Time;
using Tailor360.Platform.Persistence.Auditing;

namespace Tailor360.Modules.Billing.Infrastructure.Persistence;

/// <summary>Billing's audit trail over its own context: the entry commits with the change it describes.</summary>
/// <param name="context">The module's context.</param>
/// <param name="auditContext">Who is acting.</param>
/// <param name="clock">The clock.</param>
/// <param name="idGenerator">The identifier generator.</param>
public sealed class BillingAuditWriter(
    BillingDbContext context,
    IAuditContext auditContext,
    IClock clock,
    IIdGenerator idGenerator)
    : AuditWriter<BillingDbContext>(context, auditContext, clock, idGenerator), IBillingAuditWriter;
