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
    /// <param name="purpose">What the view is for, in operator language.</param>
    /// <param name="requiredPermission">The permission an endpoint returning this view must demand.</param>
    /// <param name="withheld">Classes of data this view must never carry.</param>
    /// <param name="fields">The fields it may carry.</param>
    /// <exception cref="ArgumentException">
    /// A field repeats a name, or carries a class the view withholds.
    /// </exception>
    public ResponseView(
        string key,
        string module,
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

        if (!permissions.Contains(RequiredPermission))
        {
            return [];
        }

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
