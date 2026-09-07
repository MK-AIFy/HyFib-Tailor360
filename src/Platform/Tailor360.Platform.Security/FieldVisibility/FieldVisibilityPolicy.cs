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
}
