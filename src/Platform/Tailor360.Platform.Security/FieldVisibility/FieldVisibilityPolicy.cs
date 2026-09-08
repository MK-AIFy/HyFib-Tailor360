using Tailor360.Platform.Security.Authorisation;

namespace Tailor360.Platform.Security.FieldVisibility;

/// <summary>
/// The default field-visibility policy: the caller's own permissions decide what the response carries.
/// </summary>
/// <param name="catalogue">The declared views.</param>
/// <param name="currentUser">The caller.</param>
public sealed class FieldVisibilityPolicy(ResponseViewCatalogue catalogue, ICurrentUser currentUser)
    : IFieldVisibilityPolicy
{
    /// <inheritdoc />
    public FieldMask MaskFor(string viewKey) => MaskFor(viewKey, currentUser.Permissions);

    /// <inheritdoc />
    public FieldMask MaskFor(string viewKey, IReadOnlySet<string> permissions)
    {
        ArgumentNullException.ThrowIfNull(permissions);

        var view = catalogue.Require(viewKey);
        return new FieldMask(view, view.VisibleTo(permissions));
    }

    /// <inheritdoc />
    public FieldMask MaskForReached(string viewKey, string reachedBy)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reachedBy);

        var view = catalogue.Require(viewKey);

        if (!currentUser.HasPermission(reachedBy))
        {
            throw new InvalidOperationException(
                $"A mask for '{viewKey}' was asked for on the basis of '{reachedBy}', which this caller "
                + "does not hold. The permission stood in for the view's own must be the one the "
                + "endpoint demanded, so that reach is never asserted by a handler on a caller's "
                + "behalf.");
        }

        return new FieldMask(view, view.FieldsFor(currentUser.Permissions));
    }
}
