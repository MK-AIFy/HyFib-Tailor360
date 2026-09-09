namespace Tailor360.Platform.Security.FieldVisibility;

/// <summary>
/// What each kind of surface may never carry, in one place.
/// </summary>
/// <remarks>
/// <para>
/// The forbidden set used to be a constant on the module that happened to declare the first view
/// needing it — <c>OrdersResponseViews.WithheldFromTheWorkshop</c> — which worked while every view was
/// a workshop view and stopped working the moment a second kind existed. A rule owned by a module is a
/// rule the next module writes out again, slightly differently, which is the failure
/// <c>docs/security/field-visibility.md</c> section 2 is entirely about.
/// </para>
/// <para>
/// So the sets live here, keyed by the surface they are a property of, and
/// <see cref="ResponseView"/>'s constructor refuses a view that does not withhold its surface's set.
/// A view may withhold more; it may not withhold less. That is what makes "no workshop surface carries
/// a price" a thing that cannot be written rather than a thing five declarations happen to agree on.
/// </para>
/// </remarks>
public static class SurfaceRules
{
    /// <summary>
    /// What no workshop surface may carry: the customer's contact details and notes, and anything
    /// about money.
    /// </summary>
    /// <remarks>
    /// From <c>docs/nfr/data-classification.md</c> section 2: "a job card shows the customer's name and job
    /// number and never their phone number, <b>which is why a Tailor can be shown a job card at
    /// all</b>". Measurements and media are absent from this set deliberately — the sheet is measurements
    /// and the job card carries the reference photographs — and both are gated on their own permission
    /// instead.
    /// </remarks>
    public const FieldClassification ForbiddenOnAWorkshopSurface =
        FieldClassification.CustomerContact
        | FieldClassification.CustomerNotes
        | FieldClassification.Pricing
        | FieldClassification.PaymentState;

    /// <summary>
    /// What no counter surface may carry: the figures a garment is cut to, images, anything about
    /// money, and how fast a named member of staff works.
    /// </summary>
    /// <remarks>
    /// A counter surface may carry contact details, which is the whole difference between the two: the
    /// screen exists so that somebody can be telephoned when their blouse is ready. What it is still
    /// not is a measurement sheet, an invoice or a staff report — each of those is another view, with
    /// another permission, and in two cases another module.
    /// </remarks>
    public const FieldClassification ForbiddenOnACounterSurface =
        FieldClassification.Measurement
        | FieldClassification.Media
        | FieldClassification.Pricing
        | FieldClassification.PaymentState
        | FieldClassification.StaffPerformance;

    /// <summary>The classes a view on this surface must withhold, at a minimum.</summary>
    /// <param name="surface">The surface.</param>
    /// <returns>The forbidden classes.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The surface is not one this type knows.</exception>
    public static FieldClassification ForbiddenOn(ViewSurface surface) => surface switch
    {
        ViewSurface.Workshop => ForbiddenOnAWorkshopSurface,
        ViewSurface.Counter => ForbiddenOnACounterSurface,

        // A new surface is a new decision about what may never appear on it, and defaulting to
        // "nothing is forbidden" would make adding one the quietest way to bypass this whole file.
        _ => throw new ArgumentOutOfRangeException(
            nameof(surface),
            surface,
            "No forbidden set is declared for this surface. Adding a surface means deciding what it "
            + "may never carry, here and in docs/security/field-visibility.md, in the same change."),
    };
}
