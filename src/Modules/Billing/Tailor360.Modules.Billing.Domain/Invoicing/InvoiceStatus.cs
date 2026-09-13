namespace Tailor360.Modules.Billing.Domain.Invoicing;

/// <summary>Where an invoice is in its life. Posted is reached by E09-F02-2; nothing here moves a row there.</summary>
public enum InvoiceStatus
{
    /// <summary>Being prepared; nothing about it is authoritative.</summary>
    Draft = 0,

    /// <summary>Numbered and frozen.</summary>
    Posted = 1,

    /// <summary>A draft the cashier abandoned; its garment jobs may be invoiced again.</summary>
    Discarded = 2,
}

/// <summary>What Billing knows an order to be, from the facts Orders published.</summary>
public enum OrderFactStatus
{
    /// <summary>Confirmed and open: a draft invoice may be created for it.</summary>
    Confirmed = 0,

    /// <summary>Cancelled: no new invoice.</summary>
    Cancelled = 1,
}
