using System.Collections.Frozen;

namespace Tailor360.Platform.Security.FieldVisibility;

/// <summary>
/// One named response shape, its fields, and the classes of data it is not permitted to carry.
/// </summary>
/// <remarks>
/// <para>
/// A view is declared once and projected everywhere. The alternative — each handler deciding for itself
/// which fields to leave out — is the failure this type exists to prevent: it works on the day it is
/// written, and drifts the first time somebody adds a field to a payload in a hurry, on a busy day,
/// without opening the document that says what a Tailor may see.
/// </para>
/// <para>
/// The constructor refuses a view that carries a field of a class it withholds. That is the invariant
/// worth having: "a job card cannot carry a price" is enforced when the catalogue is built, before any
/// request is served, rather than checked per response.
/// </para>
/// </remarks>
public sealed class ResponseView
{
    private readonly FrozenDictionary<string, ViewField> _byName;

    /// <summary>Declares a view.</summary>
    /// <param name="key">Stable dotted key, for example <c>orders.job_card</c>.</param>
    /// <param name="module">The module that owns the view.</param>
    /// <param name="surface">Who reads it, which is what decides the classes it may never carry.</param>
    /// <param name="purpose">What the view is for, in operator language.</param>
    /// <param name="requiredPermission">The permission an endpoint returning this view must demand.</param>
    /// <param name="withheld">Classes of data this view must never carry.</param>
    /// <param name="fields">The fields it may carry.</param>
    /// <exception cref="ArgumentException">
    /// A field repeats a name, a field carries a class the view withholds, or the view withholds less
    /// than its surface forbids.
    /// </exception>
    public ResponseView(
        string key,
        string module,
        ViewSurface surface,
        string purpose,
        string requiredPermission,
        FieldClassification withheld,
        IReadOnlyList<ViewField> fields)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentException.ThrowIfNullOrWhiteSpace(module);
        ArgumentException.ThrowIfNullOrWhiteSpace(purpose);
        ArgumentException.ThrowIfNullOrWhiteSpace(requiredPermission);
        ArgumentNullException.ThrowIfNull(fields);

        if (fields.Count == 0)
        {
            throw new ArgumentException($"View '{key}' declares no fields.", nameof(fields));
        }

        var forbidden = SurfaceRules.ForbiddenOn(surface);
        var missing = forbidden & ~withheld;
        if (missing != FieldClassification.None)
        {
            throw new ArgumentException(
                $"View '{key}' is a {surface} surface and does not withhold {missing}. What a surface "
                + "may never carry is a property of the surface, not of whoever declared the view: "
                + "withhold at least SurfaceRules.ForbiddenOn(ViewSurface." + surface + "), or declare "
                + "a different surface and say why in docs/security/field-visibility.md.",
                nameof(withheld));
        }

        var offending = fields.Where(field => (field.Classification & withheld) != 0).ToArray();
        if (offending.Length > 0)
        {
            throw new ArgumentException(
                $"View '{key}' withholds {withheld} and declares "
                + string.Join(", ", offending.Select(f => $"'{f.Name}' ({f.Classification})"))
                + ". A withheld class is not something to project away per request; it is something the "
                + "view may not carry at all.",
                nameof(fields));
        }

        var byName = new Dictionary<string, ViewField>(StringComparer.Ordinal);
        foreach (var field in fields)
        {
            if (!byName.TryAdd(field.Name, field))
            {
                throw new ArgumentException(
                    $"View '{key}' declares field '{field.Name}' more than once.", nameof(fields));
            }
        }

        Key = key;
        Module = module;
        Surface = surface;
        Purpose = purpose;
        RequiredPermission = requiredPermission;
        Withheld = withheld;
        Fields = [.. fields];
        _byName = byName.ToFrozenDictionary(StringComparer.Ordinal);
    }

    /// <summary>Stable dotted key.</summary>
    public string Key { get; }

    /// <summary>The module that owns the view.</summary>
    public string Module { get; }

    /// <summary>Who reads it, which is what decides the classes it may never carry.</summary>
    public ViewSurface Surface { get; }

    /// <summary>What the view is for.</summary>
    public string Purpose { get; }

    /// <summary>The permission an endpoint returning this view must demand.</summary>
    public string RequiredPermission { get; }

    /// <summary>Classes of data this view may never carry.</summary>
    public FieldClassification Withheld { get; }

    /// <summary>The fields it may carry, in declaration order.</summary>
    public IReadOnlyList<ViewField> Fields { get; }

    /// <summary>True when the view declares a field of this name.</summary>
    public bool Declares(string fieldName) => _byName.ContainsKey(fieldName);

    /// <summary>The field of this name, or null.</summary>
    public ViewField? Field(string fieldName) => _byName.GetValueOrDefault(fieldName);

    /// <summary>
    /// The fields a caller holding <paramref name="permissions"/> may be shown, in declaration order.
    /// Empty when the caller cannot reach the view at all.
    /// </summary>
    public IReadOnlyList<string> VisibleTo(IReadOnlySet<string> permissions)
    {
        ArgumentNullException.ThrowIfNull(permissions);

        return permissions.Contains(RequiredPermission) ? FieldsFor(permissions) : [];
    }

    /// <summary>
    /// The fields a caller holding <paramref name="permissions"/> may be shown once they have reached
    /// the view, in declaration order — the per-field gates alone, without the view's own.
    /// </summary>
    /// <remarks>
    /// It exists for the command that answers with the record it just changed. Such an endpoint demands
    /// its own permission — <c>customers.update</c>, not <c>customers.read</c> — and refusing to show
    /// the caller the change they were authorised to make would be a refusal of their own write. What
    /// still applies is every <em>field</em> gate, which is what this returns; reaching the view at all
    /// was decided by the endpoint. Callers go through
    /// <see cref="IFieldVisibilityPolicy.MaskForReached"/>, which will not stand a permission in that
    /// the caller does not actually hold.
    /// </remarks>
    /// <param name="permissions">The caller's effective permissions.</param>
    /// <returns>The visible field names, in declaration order.</returns>
    public IReadOnlyList<string> FieldsFor(IReadOnlySet<string> permissions)
    {
        ArgumentNullException.ThrowIfNull(permissions);

        return
        [
            .. Fields
                .Where(field => field.RequiredPermission is null
                                || permissions.Contains(field.RequiredPermission))
                .Select(field => field.Name),
        ];
    }
}

/// <summary>A module's contribution to the response-view catalogue.</summary>
public interface IResponseViewSource
{
    /// <summary>The views this module owns.</summary>
    IReadOnlyCollection<ResponseView> Views { get; }
}
