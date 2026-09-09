namespace Tailor360.Platform.Security.FieldVisibility;

/// <summary>
/// Who a response view is written for, which decides what it may never carry.
/// </summary>
/// <remarks>
/// <para>
/// The distinction exists because the four workshop-forbidden classes are a property of the
/// <em>workshop</em>, not of every screen the software draws. The sentence behind them is one
/// sentence in <c>docs/nfr/data-classification.md</c> section 2 — "a job card shows the customer's
/// name and job number and never their phone number, <b>which is why a Tailor can be shown a job card
/// at all</b>" — and it is an argument about a card that is printed and left on a bench, not about a
/// counter clerk answering the telephone.
/// </para>
/// <para>
/// Reading the rule as "no view carries contact details" would have made the customer record
/// undeclarable, which is exactly what had happened: until #26 the record's payload masked its contact
/// fields by hand, because declaring a view for it would have failed the assertion that every view
/// withholds <see cref="FieldClassification.CustomerContact"/>. The rule was right and its scope was
/// wrong, so the scope is now written down instead of being implied by there being only workshop
/// views.
/// </para>
/// </remarks>
public enum ViewSurface
{
    /// <summary>
    /// A surface the people making the garment read: a job card, a work queue, a measurement sheet.
    /// It is printed, carried, and left where anybody in the workshop can read it, so it carries the
    /// customer's name and nothing else about the person, and never a price or a payment state.
    /// </summary>
    Workshop = 0,

    /// <summary>
    /// A surface the people serving the customer read, where the point of the screen is the customer:
    /// the record, the search results a duplicate is spotted in. It may carry contact details — to the
    /// callers permitted them, field by field — because ringing somebody to say their blouse is ready
    /// is what the screen is for.
    /// </summary>
    Counter = 1,
}
