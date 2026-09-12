using Tailor360.Platform.Abstractions.Auditing;

namespace Tailor360.Modules.Billing.Application;

/// <summary>Writes one audit entry and saves it. Every Billing command records itself through this.</summary>
internal static class BillingAudit
{
    /// <summary>The entity type of a tax configuration version in the audit trail.</summary>
    public const string TaxConfigurationEntity = "billing.tax_configuration_version";

    /// <summary>The entity type of a GST registration in the audit trail.</summary>
    public const string RegistrationEntity = "billing.gst_registration";

    /// <summary>The entity type of a price list in the audit trail.</summary>
    public const string PriceListEntity = "billing.price_list";

    /// <summary>The entity type of a price-list version in the audit trail.</summary>
    public const string PriceListVersionEntity = "billing.price_list_version";

    /// <summary>A calculation snapshot.</summary>
    public const string CalculationSnapshotEntity = "billing.calculation_snapshot";

    /// <summary>An invoice.</summary>
    public const string InvoiceEntity = "billing.invoice";

    /// <summary>A payment mode.</summary>
    public const string PaymentModeEntity = "billing.payment_mode";

    /// <summary>A cashier session.</summary>
    public const string CashierSessionEntity = "billing.cashier_session";

    /// <summary>A payment, with its allocations and its advance.</summary>
    public const string PaymentEntity = "billing.payment";

    /// <summary>A receipt.</summary>
    public const string ReceiptEntity = "billing.receipt";

    /// <summary>A refund.</summary>
    public const string RefundEntity = "billing.refund";

    public static async Task RecordAsync(
        IAuditWriter audit,
        string action,
        string entityType,
        Guid entityId,
        string summary,
        string? reason,
        object? before,
        object? after,
        CancellationToken cancellationToken)
    {
        await audit.WriteAsync(
            new AuditEntry(action, entityType, entityId, summary, reason, before, after),
            cancellationToken);

        await audit.SaveAsync(cancellationToken);
    }
}
