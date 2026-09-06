using System.Collections.Frozen;

namespace Tailor360.Platform.Security.FieldVisibility;

/// <summary>
/// The fields of one view that one caller may be shown, and the builder that projects a response
/// through them.
/// </summary>
/// <remarks>
/// A mask is computed once per response from the caller's effective permissions, so a custom role that
/// an administrator invented behaves exactly as a shipped one does: the mask never asks what role
/// anybody holds, only what permissions they hold.
/// </remarks>
public sealed class FieldMask
{
    private readonly FrozenSet<string> _visible;

    internal FieldMask(ResponseView view, IReadOnlyList<string> visible)
    {
        View = view;
        VisibleFields = visible;
        _visible = visible.ToFrozenSet(StringComparer.Ordinal);
    }

    /// <summary>The view this mask belongs to.</summary>
    public ResponseView View { get; }

    /// <summary>The fields the caller may be shown, in declaration order.</summary>
    public IReadOnlyList<string> VisibleFields { get; }

    /// <summary>True when the caller may not be shown any field of the view at all.</summary>
    public bool IsEmpty => _visible.Count == 0;

    /// <summary>True when the caller may be shown this field.</summary>
    public bool Allows(string fieldName) => _visible.Contains(fieldName);

    /// <summary>Starts building a response body through this mask.</summary>
    public MaskedPayload Build() => new(this);
}

/// <summary>
/// A response body under construction, which can only contain fields the view declares and the caller
/// may see.
/// </summary>
/// <remarks>
/// <para>
/// The two behaviours are deliberately different. A field the caller may not see is <b>dropped in
/// silence</b>, because that is the whole purpose: the handler computes the order once and each caller
/// receives their own share of it. A field the <b>view does not declare</b> throws, because that is not
/// a caller being refused something — it is a field nobody has ever approved, on its way into a response.
/// Dropping it quietly would turn a typo into a missing value and an unapproved field into a shipped one,
/// depending only on which mistake was made.
/// </para>
/// </remarks>
public sealed class MaskedPayload
{
    private readonly FieldMask _mask;
    private readonly Dictionary<string, object?> _values = new(StringComparer.Ordinal);

    internal MaskedPayload(FieldMask mask) => _mask = mask;

    /// <summary>
    /// Sets a field, if the caller may see it.
    /// </summary>
    /// <exception cref="InvalidOperationException">The view declares no field of that name.</exception>
    public MaskedPayload Set(string fieldName, object? value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fieldName);

        if (!_mask.View.Declares(fieldName))
        {
            throw new InvalidOperationException(
                $"View '{_mask.View.Key}' declares no field '{fieldName}'. Add it to the view, to "
                + "docs/security/field-visibility.md and to the role sets there, in one change — a field "
                + "that reaches a response without being declared has been approved by nobody.");
        }

        if (_mask.Allows(fieldName))
        {
            _values[fieldName] = value;
        }

        return this;
    }

    /// <summary>The body, in the view's declaration order.</summary>
    public IReadOnlyDictionary<string, object?> ToPayload()
    {
        var ordered = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var field in _mask.View.Fields)
        {
            if (_values.TryGetValue(field.Name, out var value))
            {
                ordered[field.Name] = value;
            }
        }

        return ordered;
    }
}
