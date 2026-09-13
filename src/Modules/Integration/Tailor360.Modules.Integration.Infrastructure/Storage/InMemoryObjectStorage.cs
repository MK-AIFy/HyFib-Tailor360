using System.Collections.Concurrent;
using Tailor360.Platform.Abstractions.Ports;

namespace Tailor360.Modules.Integration.Infrastructure.Storage;

/// <summary>
/// The collecting adapter: every object kept in memory for the life of the process. What the test host and
/// a developer without the compose stack run against, and what a test inspects to see what was stored.
/// Never the production adapter — the environment guard sees to that.
/// </summary>
public sealed class InMemoryObjectStorage : IObjectStorage
{
    private readonly ConcurrentDictionary<string, (byte[] Bytes, string ContentType)> _objects = new(StringComparer.Ordinal);

    /// <summary>The keys stored so far, for a test to look at.</summary>
    public IReadOnlyCollection<string> Keys => [.. _objects.Keys];

    /// <inheritdoc />
    public async Task PutAsync(string key, Stream content, string contentType, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentNullException.ThrowIfNull(content);

        using var buffer = new MemoryStream();
        await content.CopyToAsync(buffer, cancellationToken);
        _objects[key] = (buffer.ToArray(), contentType);
    }

    /// <inheritdoc />
    public Task<Stream?> OpenReadAsync(string key, CancellationToken cancellationToken = default)
        => Task.FromResult<Stream?>(_objects.TryGetValue(key, out var stored) ? new MemoryStream(stored.Bytes, writable: false) : null);

    /// <inheritdoc />
    public Task<bool> ExistsAsync(string key, CancellationToken cancellationToken = default)
        => Task.FromResult(_objects.ContainsKey(key));

    /// <summary>The media type an object was stored as, for a test.</summary>
    public string? ContentTypeOf(string key) => _objects.TryGetValue(key, out var stored) ? stored.ContentType : null;
}
