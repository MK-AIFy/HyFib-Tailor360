using Tailor360.Platform.Abstractions.Auditing;

namespace Tailor360.Modules.Billing.Application.Abstractions;

/// <summary>
/// Billing's own audit trail: an entry staged through it is tracked by the same change tracker as the
/// invoice, payment, session or batch it describes, and committed by the same save
/// (<see href="https://github.com/MK-AIFy/HyFib-Tailor360/issues/179">#179</see>). Its own port rather
/// than <see cref="IAuditWriter"/> because the host composes every module at once and one interface
/// would resolve to whichever module registered last.
/// </summary>
public interface IBillingAuditWriter : IAuditWriter;
