namespace Tailor360.Modules.Orders.Domain.Drafts;

/// <summary>
/// What a garment section says about its measurements while the draft is still open.
/// </summary>
/// <remarks>
/// <para>
/// <strong>An intent is a plan, not evidence.</strong> Measurements themselves belong to Customers and are
/// never held here; this only records what Reception decided at the counter, so that the
/// <strong>Measurements needed</strong> queue can be built from it and so that confirmation can refuse a
/// garment nobody ever measured (<c>docs/IMPLEMENTATION_PLAN.md</c> #32b,
/// <c>docs/prd/state-transitions.md</c> section 2.1).
/// </para>
/// <para>
/// An estimate needs none of this: <c>state-transitions.md</c> section 2.1 says measurements are not required
/// to issue one. Only a confirmation reads the intent, which is why an undecided garment is an ordinary state
/// of a draft rather than something to refuse while somebody is still typing.
/// </para>
/// </remarks>
public enum MeasurementIntent
{
    /// <summary>Nobody has decided yet. The default a garment is added with.</summary>
    Undecided = 0,

    /// <summary>The customer is being measured at the counter now.</summary>
    TakeNow = 1,

    /// <summary>
    /// The garment goes on the <strong>Measurements needed</strong> queue for Measurement Staff to open on
    /// their own device.
    /// </summary>
    TakeLater = 2,

    /// <summary>
    /// A confirmed measurement version is being reused; <c>MeasurementVersionId</c> names it.
    /// </summary>
    ReuseVersion = 3,
}
