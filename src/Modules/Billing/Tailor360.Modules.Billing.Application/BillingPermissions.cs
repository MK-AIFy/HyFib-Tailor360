using Tailor360.Platform.Security.Permissions;

namespace Tailor360.Modules.Billing.Application;

/// <summary>
/// The permissions the Billing module owns, covering the pricing and tax engine, invoices, payments, receipts and cashier reconciliation.
/// The catalogue is composed at startup and rejects a key claimed by two modules, so every permission
/// has exactly one owner. Issue #24 populates this set together with the role matrix that carries it.
/// </summary>
public sealed class BillingPermissions : IPermissionSource
{
    /// <inheritdoc />
    public IReadOnlyCollection<Permission> Permissions => [];
}
