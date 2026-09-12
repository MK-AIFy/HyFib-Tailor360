using Microsoft.Extensions.Options;
using Minio;
using Minio.DataModel.Args;
using Minio.Exceptions;
using Tailor360.Platform.Abstractions.Ports;

namespace Tailor360.Modules.Integration.Infrastructure.Storage;

/// <summary>
/// The S3-compatible adapter over MinIO's own client (ADR-0014). The SDK is referenced here and nowhere
/// else (ARCH-009); the bucket is created by the deployment (the compose stack's <c>createbuckets</c>
/// service), never by the application, which holds no policy to do so.
/// </summary>
/// <remarks>
/// A read copies the object into memory before answering, because the client hands the body to a callback
/// for the duration of the call rather than returning a stream; a rendered document is a few hundred
/// kilobytes, and a caller that streams it on to a browser is served from that copy. Large media is
/// Media's concern and Media's adapter.
/// </remarks>
public sealed class MinioObjectStorage : IObjectStorage, IDisposable
{
    private readonly IMinioClient _client;
    private readonly ObjectStorageOptions _options;

    /// <summary>Builds the client from the options; refused when the endpoint or the credentials are missing.</summary>
    /// <param name="options">The bound options.</param>
    public MinioObjectStorage(IOptions<ObjectStorageOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var configured = options.Value;
        if (!configured.IsConfigured || !Uri.TryCreate(configured.Endpoint, UriKind.Absolute, out var endpoint))
        {
            throw new InvalidOperationException("ObjectStorage:Endpoint must be an absolute URL when the MinIO adapter is used.");
        }

        if (string.IsNullOrWhiteSpace(configured.AccessKey) || string.IsNullOrWhiteSpace(configured.SecretKey))
        {
            throw new InvalidOperationException(
                "ObjectStorage:AccessKey and ObjectStorage:SecretKey are mounted secrets and both must be present for the MinIO adapter.");
        }

        _options = configured;
        _client = new MinioClient()
            .WithEndpoint(endpoint.Host, endpoint.Port)
            .WithCredentials(configured.AccessKey, configured.SecretKey)
            .WithRegion(configured.Region)
            .WithSSL(endpoint.Scheme == Uri.UriSchemeHttps)
            .Build();
    }

    /// <inheritdoc />
    public async Task PutAsync(string key, Stream content, string contentType, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentNullException.ThrowIfNull(content);

        // The client wants the size up front; a seekable stream says it, anything else is buffered once.
        Stream body = content;
        MemoryStream? buffered = null;
        if (!content.CanSeek)
        {
            buffered = new MemoryStream();
            await content.CopyToAsync(buffered, cancellationToken);
            buffered.Position = 0;
            body = buffered;
        }

        try
        {
            await _client.PutObjectAsync(
                new PutObjectArgs()
                    .WithBucket(_options.BucketFor(key))
                    .WithObject(key)
                    .WithStreamData(body)
                    .WithObjectSize(body.Length - body.Position)
                    .WithContentType(contentType),
                cancellationToken);
        }
        finally
        {
            if (buffered is not null)
            {
                await buffered.DisposeAsync();
            }
        }
    }

    /// <inheritdoc />
    public async Task<Stream?> OpenReadAsync(string key, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        var buffer = new MemoryStream();
        try
        {
            await _client.GetObjectAsync(
                new GetObjectArgs()
                    .WithBucket(_options.BucketFor(key))
                    .WithObject(key)
                    .WithCallbackStream(async (stream, token) => await stream.CopyToAsync(buffer, token)),
                cancellationToken);
        }
        catch (ObjectNotFoundException)
        {
            await buffer.DisposeAsync();
            return null;
        }

        buffer.Position = 0;
        return buffer;
    }

    /// <inheritdoc />
    public async Task<bool> ExistsAsync(string key, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        try
        {
            await _client.StatObjectAsync(new StatObjectArgs().WithBucket(_options.BucketFor(key)).WithObject(key), cancellationToken);
            return true;
        }
        catch (ObjectNotFoundException)
        {
            return false;
        }
    }

    /// <inheritdoc />
    public void Dispose() => _client.Dispose();
}
