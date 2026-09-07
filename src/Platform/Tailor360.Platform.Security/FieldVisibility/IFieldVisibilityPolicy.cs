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
}
