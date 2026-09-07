using System.Collections.Frozen;

namespace Tailor360.Platform.Security.FieldVisibility;

/// <summary>
/// Every response view the application declares, composed from the modules that own them.
/// </summary>
/// <remarks>
/// It is the field-level counterpart of <see cref="Permissions.PermissionCatalogue"/> and is built the
/// same way, for the same reason: one composed list, duplicates refused, and a document held equal to it
/// by test, so that what an owner approved and what a response contains cannot be two different things.
/// </remarks>
public sealed class ResponseViewCatalogue
{
    private readonly FrozenDictionary<string, ResponseView> _byKey;

    /// <summary>Composes the catalogue from every registered source.</summary>
    /// <exception cref="InvalidOperationException">Two sources declared the same view key.</exception>
    public ResponseViewCatalogue(IEnumerable<IResponseViewSource> sources)
    {
        ArgumentNullException.ThrowIfNull(sources);

        var all = new Dictionary<string, ResponseView>(StringComparer.Ordinal);
        foreach (var view in sources.SelectMany(source => source.Views))
        {
            if (!all.TryAdd(view.Key, view))
            {
                throw new InvalidOperationException(
                    $"Response view '{view.Key}' is declared by more than one module "
                    + $"('{all[view.Key].Module}' and '{view.Module}').");
            }
        }

        _byKey = all.ToFrozenDictionary(StringComparer.Ordinal);
    }

    /// <summary>Every known view, ordered by key.</summary>
    public IReadOnlyCollection<ResponseView> All
        => [.. _byKey.Values.OrderBy(view => view.Key, StringComparer.Ordinal)];

    /// <summary>Looks a view up by key.</summary>
    public ResponseView? Find(string key) => _byKey.GetValueOrDefault(key);

    /// <summary>The view of this key.</summary>
    /// <exception cref="InvalidOperationException">No such view is declared.</exception>
    public ResponseView Require(string key)
        => Find(key) ?? throw new InvalidOperationException(
            $"No response view '{key}' is declared. The views the application declares are: "
            + string.Join(", ", _byKey.Keys.Order(StringComparer.Ordinal)) + ".");
}
