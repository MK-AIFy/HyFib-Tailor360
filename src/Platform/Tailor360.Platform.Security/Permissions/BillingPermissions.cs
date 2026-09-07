namespace Tailor360.Platform.Security.Permissions;

/// <summary>
/// Permissions owned by the Billing and Payments module: price lists, invoices, credit notes,
/// receipts, payments, allocations, refunds, reversals and the cashier session.
/// </summary>
/// <remarks>
/// <para>
/// Everything that can move money after the fact is flagged: cancelling a posted invoice, overriding a
/// price, refunding, reversing, allocating by hand and approving a dispatch exception. That is the set
/// <c>docs/prd/raci.md</c> rows 18, 19 and 24 mark as needing step-up and a reason.
/// </para>
/// <para>
/// <c>payments.open_session</c> and <c>payments.close_session</c> are audit actions, not permissions.
/// The permission that gates both is <see cref="Session"/>, exactly as raci row 5 states.
/// </para>
/// </remarks>
public static class BillingPermissions
{
    /// <summary>Draft an invoice from a delivered or deliverable order.</summary>
    public const string CreateInvoice = "billing.create_invoice";

    /// <summary>Amend a draft invoice before it is posted.</summary>
    public const string UpdateInvoice = "billing.update_invoice";

    /// <summary>Post an invoice, which numbers it and makes it immutable.</summary>
    public const string PostInvoice = "billing.post_invoice";

    /// <summary>Cancel a posted invoice by issuing its compensating record.</summary>
    public const string CancelInvoice = "billing.cancel_invoice";

    /// <summary>Post a credit note against a posted invoice.</summary>
    public const string PostCreditNote = "billing.post_credit_note";

    /// <summary>Print or reprint a payment receipt.</summary>
    public const string PrintReceipt = "billing.print_receipt";

    /// <summary>Draft a price-list version.</summary>
    public const string ManagePriceLists = "billing.manage_price_lists";

    /// <summary>Publish a price-list version.</summary>
    public const string PublishPriceList = "billing.publish_price_list";

    /// <summary>Override a calculated price or apply a discount above the configured threshold.</summary>
    public const string OverridePrice = "billing.override_price";

    /// <summary>Approve a single-use dispatch exception for an unpaid order.</summary>
    public const string ApproveDispatchException = "billing.approve_dispatch_exception";

    /// <summary>Record a payment or an advance.</summary>
    public const string RecordPayment = "payments.record";

    /// <summary>Record the intent of a provider-mediated payment before calling the provider.</summary>
    public const string RecordPaymentIntent = "payments.record_intent";

    /// <summary>Allocate a payment to invoices under the automatic rule.</summary>
    public const string AllocatePayment = "payments.allocate";

    /// <summary>Allocate a payment to invoices by hand, against the automatic rule.</summary>
    public const string AllocateManual = "payments.allocate_manual";

    /// <summary>Refund a payment.</summary>
    public const string Refund = "payments.refund";

    /// <summary>Reverse a recorded payment that never cleared.</summary>
    public const string Reverse = "payments.reverse";

    /// <summary>Open and close a cashier session with its denomination count.</summary>
    public const string Session = "payments.session";

    /// <summary>Every permission in this group, in declaration order.</summary>
    public static IReadOnlyCollection<Permission> All { get; } =
    [
        new(CreateInvoice, "Draft an invoice for an order.", PermissionModules.Billing),
        new(UpdateInvoice, "Amend a draft invoice before it is posted.", PermissionModules.Billing),
        new(PostInvoice, "Post an invoice, numbering it and making it immutable.",
            PermissionModules.Billing),
        new(CancelInvoice, "Cancel a posted invoice through its compensating record.",
            PermissionModules.Billing, RequiresMfa: true, RequiresStepUp: true, RequiresReason: true),
        new(PostCreditNote, "Post a credit note against a posted invoice.",
            PermissionModules.Billing, RequiresMfa: true, RequiresReason: true),
        new(PrintReceipt, "Print or reprint a payment receipt.", PermissionModules.Billing),
        new(ManagePriceLists, "Draft a price-list version.", PermissionModules.Billing, PermissionScope.Organisation),
        new(PublishPriceList, "Publish a price-list version.",
            PermissionModules.Billing, PermissionScope.Organisation,
            RequiresMfa: true, RequiresStepUp: true, RequiresReason: true),
        new(OverridePrice, "Override a calculated price or discount above the threshold.",
            PermissionModules.Billing, RequiresMfa: true, RequiresStepUp: true, RequiresReason: true),
        new(ApproveDispatchException, "Approve a single-use dispatch exception for an unpaid order.",
            PermissionModules.Billing, RequiresMfa: true, RequiresStepUp: true, RequiresReason: true),
        new(RecordPayment, "Record a payment or an advance.", PermissionModules.Billing),
        new(RecordPaymentIntent, "Record the intent of a provider-mediated payment.",
            PermissionModules.Billing),
        new(AllocatePayment, "Allocate a payment to invoices under the automatic rule.",
            PermissionModules.Billing),
        new(AllocateManual, "Allocate a payment to invoices by hand.",
            PermissionModules.Billing, RequiresMfa: true, RequiresStepUp: true, RequiresReason: true),
        new(Refund, "Refund a payment.",
            PermissionModules.Billing, RequiresMfa: true, RequiresStepUp: true, RequiresReason: true),
        new(Reverse, "Reverse a recorded payment that never cleared.",
            PermissionModules.Billing, RequiresMfa: true, RequiresStepUp: true, RequiresReason: true),
        new(Session, "Open and close a cashier session with its denomination count.",
            PermissionModules.Billing, RequiresMfa: true),
    ];
}
