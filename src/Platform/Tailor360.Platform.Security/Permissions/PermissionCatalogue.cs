using System.Collections.Frozen;

namespace Tailor360.Platform.Security.Permissions;

/// <summary>
/// The catalogue of every permission the application understands. Modules contribute their own
/// permissions through <see cref="IPermissionSource"/>; this type composes them and rejects duplicates,
/// so two modules cannot claim the same key.
/// </summary>
public sealed class PermissionCatalogue
{
    private readonly FrozenDictionary<string, Permission> _byKey;

    /// <summary>Composes the catalogue from every registered source.</summary>
    /// <exception cref="InvalidOperationException">Two sources declared the same permission key.</exception>
    public PermissionCatalogue(IEnumerable<IPermissionSource> sources)
    {
        ArgumentNullException.ThrowIfNull(sources);

        var all = new Dictionary<string, Permission>(StringComparer.Ordinal);
        foreach (var permission in sources.SelectMany(source => source.Permissions))
        {
            if (!all.TryAdd(permission.Key, permission))
            {
                throw new InvalidOperationException(
                    $"Permission '{permission.Key}' is declared by more than one module " +
                    $"('{all[permission.Key].Module}' and '{permission.Module}').");
            }
        }

        _byKey = all.ToFrozenDictionary(StringComparer.Ordinal);
    }

    /// <summary>Every known permission, ordered by key.</summary>
    public IReadOnlyCollection<Permission> All => [.. _byKey.Values.OrderBy(p => p.Key, StringComparer.Ordinal)];

    /// <summary>Looks a permission up by key.</summary>
    public Permission? Find(string key) => _byKey.GetValueOrDefault(key);

    /// <summary>True when the key is a known permission.</summary>
    public bool Contains(string key) => _byKey.ContainsKey(key);
}

/// <summary>A module's contribution to the permission catalogue.</summary>
public interface IPermissionSource
{
    /// <summary>The permissions this module owns.</summary>
    IReadOnlyCollection<Permission> Permissions { get; }
}
