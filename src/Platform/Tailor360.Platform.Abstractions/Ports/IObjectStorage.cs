namespace Tailor360.Platform.Abstractions.Ports;

/// <summary>
/// The object store behind every rendered document and stored image (#155): put, read back as a stream,
/// and ask whether a key exists. There is deliberately no delete and no listing: a module owns its prefix
/// (<c>docs/architecture/module-ownership.md</c> MO-4), a statutory document is never removed by the
/// application, and retention is an operator's job with its own tooling. Keys are opaque —
/// <c>documents/&lt;uuid&gt;</c> — never a display number or a name (<c>docs/architecture/conventions.md</c>
/// section 3.5), and no URL to an object is ever handed out: every read goes through an endpoint that
/// re-authorises the caller (<c>CLAUDE.md</c> section 4 rule 9).
/// </summary>
public interface IObjectStorage
{
    /// <summary>Stores an object under a key, replacing whatever the key held.</summary>
    /// <param name="key">The opaque key, prefixed by the owning module's prefix.</param>
    /// <param name="content">The bytes; read from its current position to the end.</param>
    /// <param name="contentType">The media type the object is served as.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    Task PutAsync(string key, Stream content, string contentType, CancellationToken cancellationToken = default);

    /// <summary>Opens an object for reading, or answers null when the key holds nothing.</summary>
    /// <param name="key">The key.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>A readable stream the caller disposes, or null.</returns>
    Task<Stream?> OpenReadAsync(string key, CancellationToken cancellationToken = default);

    /// <summary>True when the key holds an object.</summary>
    /// <param name="key">The key.</param>
    /// <param name="cancellationToken">Cancels the probe.</param>
    Task<bool> ExistsAsync(string key, CancellationToken cancellationToken = default);
}
