namespace Tailor360.Modules.Billing.Application.Invoicing;

/// <summary>What the business configures about invoices (#154), bound from <c>Billing:Invoices</c>.</summary>
public sealed class InvoiceOptions
{
    /// <summary>The configuration section.</summary>
    public const string SectionName = "Billing:Invoices";

    /// <summary>
    /// How long after posting an invoice may still be cancelled, or null for no limit. The value is a
    /// product decision that has no source yet (<c>docs/prd/assumptions-and-open-decisions.md</c>, OD-23):
    /// until it is decided, no window is enforced, and the register says so rather than this code inventing
    /// one.
    /// </summary>
    public TimeSpan? CancellationWindow { get; set; }
}
