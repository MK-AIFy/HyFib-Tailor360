namespace Tailor360.Modules.Billing.Domain.Tax;

/// <summary>The four components a GST tax code may carry (<c>docs/architecture/conventions.md</c> section 1.3).</summary>
public enum TaxComponentKind
{
    /// <summary>Central GST, on an intra-state supply.</summary>
    Cgst = 0,

    /// <summary>State GST, on an intra-state supply.</summary>
    Sgst = 1,

    /// <summary>Integrated GST, on an inter-state supply.</summary>
    Igst = 2,

    /// <summary>Compensation cess, where the code carries one.</summary>
    Cess = 3,
}
