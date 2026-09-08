namespace Tailor360.Platform.Security.FieldVisibility;

/// <summary>
/// Computes the field mask for a response view, from the caller's effective permissions.
/// </summary>
/// <remarks>
/// Handlers depend on this rather than on the catalogue directly, so that a mask is always derived from
/// a caller and never assembled by hand at a call site. It is the one place a response's shape is
/// decided, which is what makes "the Tailor's job card carries no contact details" a property of the
/// system rather than a property of whoever wrote the last endpoint.
/// </remarks>
public interface IFieldVisibilityPolicy
{
    /// <summary>The mask for the current caller.</summary>
    /// <exception cref="InvalidOperationException">No such view is declared.</exception>
    FieldMask MaskFor(string viewKey);

    /// <summary>
    /// The mask for a stated permission set. Used where the caller is not the reader — a worker
    /// rendering for a requester, an export generated under a declared scope — and by the tests that
    /// hold the approved role sets equal to the code.
    /// </summary>
    /// <exception cref="InvalidOperationException">No such view is declared.</exception>
    FieldMask MaskFor(string viewKey, IReadOnlySet<string> permissions);

    /// <summary>
    /// The mask for a caller an endpoint has already authorised to reach this view by a permission
    /// other than the view's own — a command answering with the record it just changed.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>POST /customers</c> demands <c>customers.create</c> and <c>PUT /customers/{id}</c> demands
    /// <c>customers.update</c>; neither demands <c>customers.read</c>, which is what the record view
    /// requires. Computing an ordinary mask there would answer a successful write with an empty body,
    /// and answering with the whole record regardless would be the endpoint deciding its own field set,
    /// which is the arrangement this whole mechanism exists to remove. So the reach is stated, and the
    /// field gates still apply.
    /// </para>
    /// <para>
    /// It cannot be used to fabricate reach. <paramref name="reachedBy"/> is checked against the
    /// caller's own permissions, so a call site can only stand in a permission the caller actually
    /// holds — which is to say, the one the endpoint already demanded of them.
    /// </para>
    /// </remarks>
    /// <param name="viewKey">The view.</param>
    /// <param name="reachedBy">
    /// The permission the endpoint demanded, which the caller must hold.
    /// </param>
    /// <exception cref="InvalidOperationException">No such view is declared.</exception>
    /// <exception cref="InvalidOperationException">
    /// The caller does not hold <paramref name="reachedBy"/>.
    /// </exception>
    FieldMask MaskForReached(string viewKey, string reachedBy);
}
