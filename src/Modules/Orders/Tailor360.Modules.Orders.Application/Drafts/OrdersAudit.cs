using Tailor360.Platform.Abstractions.Auditing;

namespace Tailor360.Modules.Orders.Application.Drafts;

/// <summary>
/// Writes the Orders module's audit entries.
/// </summary>
/// <remarks>
/// <para>
/// The module's first audit helper — Orders has none today. It follows <c>CustomerAudit</c> and
/// <c>MeasurementTemplateAudit</c> rather than <c>BillingAudit</c>: those inject the plain,
/// non-generic <see cref="IAuditWriter"/>, which is bound once, platform-wide, to
/// <c>AuditWriter&lt;PlatformDbContext&gt;</c>, and commit the entry through its own separate
/// <see cref="IAuditWriter.SaveAsync"/> after the module's own store has already committed the change —
/// two commits, in that order, which is the house rule: <strong>save the change first, then record
/// it</strong>. A module builds its own <c>IBillingAuditWriter</c>-shaped port only when it needs the
/// entry to ride the same transaction as its own save; Orders' draft path does not.
/// </para>
/// <para>
/// <strong>Nothing personal reaches the trail.</strong> A customer identifier is not personal data on
/// its own (CLAUDE.md section 4 rule 8), but the summary text and the reason a caller supplies are
/// operator-facing and must never carry a name, a telephone number or a measurement.
/// </para>
/// </remarks>
public static class OrdersAudit
{
    /// <summary>The entity type an order draft is recorded against in the audit trail.</summary>
    public const string DraftEntity = "orders.order_draft";

    /// <summary>Records one change and commits the entry.</summary>
    /// <param name="audit">The platform's audit writer.</param>
    /// <param name="action">The action constant.</param>
    /// <param name="entityId">The record.</param>
    /// <param name="summary">What happened, in words, naming no personal data.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>A task that completes when the entry is committed.</returns>
    public static async Task RecordAsync(
        IAuditWriter audit,
        string action,
        Guid entityId,
        string summary,
        CancellationToken cancellationToken)
    {
        await audit.WriteAsync(
            new AuditEntry(action, DraftEntity, entityId, summary),
            cancellationToken);

        await audit.SaveAsync(cancellationToken);
    }
}
