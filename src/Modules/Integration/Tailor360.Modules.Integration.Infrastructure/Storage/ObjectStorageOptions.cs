using System.ComponentModel.DataAnnotations;

namespace Tailor360.Modules.Integration.Infrastructure.Storage;

/// <summary>
/// Where the objects live, bound from <c>ObjectStorage</c>. The endpoint and the bucket are configuration;
/// the access and secret keys are secrets and arrive as files under <c>/run/secrets</c>
/// (<c>docs/platform/secrets.md</c>). With no endpoint set, the in-memory adapter serves — which is what
/// the test host and a developer without the compose stack get, and what production must never get:
/// <see cref="ObjectStorageOptionsValidator"/> refuses a start outside Development without an endpoint
/// and both keys.
/// </summary>
public sealed class ObjectStorageOptions
{
    /// <summary>The configuration section.</summary>
    public const string SectionName = "ObjectStorage";

    /// <summary>The S3-compatible endpoint, for example <c>http://minio:9000</c>; null for the in-memory adapter.</summary>
    [Url]
    public string? Endpoint { get; set; }

    /// <summary>The bucket the <c>documents/</c> prefix lives in (Billing), as the compose stack creates it.</summary>
    [Required]
    [MinLength(3)]
    public string DocumentsBucket { get; set; } = "tailor360-documents";

    /// <summary>The bucket the <c>exports/</c> prefix lives in (Reporting).</summary>
    [Required]
    [MinLength(3)]
    public string ExportsBucket { get; set; } = "tailor360-exports";

    /// <summary>The bucket every other prefix lives in (Media).</summary>
    [Required]
    [MinLength(3)]
    public string MediaBucket { get; set; } = "tailor360-media";

    /// <summary>The access key; a mounted secret.</summary>
    public string? AccessKey { get; set; }

    /// <summary>The secret key; a mounted secret.</summary>
    public string? SecretKey { get; set; }

    /// <summary>
    /// Path-style addressing. MinIO's client addresses objects by path always, so the adapter has nothing to
    /// read here today; the setting is kept because the compose files declare it and a virtual-hosted S3
    /// adapter would read it.
    /// </summary>
    public bool ForcePathStyle { get; set; } = true;

    /// <summary>True when the endpoint and both keys are present, which the real adapter needs.</summary>
    public bool HasCredentials => !string.IsNullOrWhiteSpace(AccessKey) && !string.IsNullOrWhiteSpace(SecretKey);

    /// <summary>The region the client signs for; S3 needs one, MinIO ignores it.</summary>
    public string Region { get; set; } = "ap-south-1";

    /// <summary>True when an endpoint is configured and the real adapter serves.</summary>
    public bool IsConfigured => !string.IsNullOrWhiteSpace(Endpoint);

    /// <summary>
    /// The bucket a key lives in, by its module prefix: one bucket per prefix is how the deployment keeps
    /// one module from reaching another's objects (<c>docs/architecture/module-ownership.md</c> MO-4).
    /// </summary>
    /// <param name="key">The object key.</param>
    public string BucketFor(string key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        if (key.StartsWith("documents/", StringComparison.Ordinal))
        {
            return DocumentsBucket;
        }

        return key.StartsWith("exports/", StringComparison.Ordinal) ? ExportsBucket : MediaBucket;
    }
}
