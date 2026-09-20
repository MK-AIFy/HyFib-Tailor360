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

    /// <summary>
    /// The bucket an object lives in from upload until issue #593 promotes it (Media). Separate from
    /// <see cref="MediaBucket"/> so an unvalidated, unscanned upload is never in the same bucket as a
    /// promoted, Ready one — a bucket-level policy difference a shared bucket could not express.
    /// </summary>
    [Required]
    [MinLength(3)]
    public string QuarantineBucket { get; set; } = "tailor360-media-quarantine";

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
    /// The bulkhead bounding concurrent calls into object storage. The limit is per host, not per adapter
    /// instance: <c>docs/architecture/resilience-policies.md</c> records why the worker and the web host
    /// carry different values.
    /// </summary>
    public ObjectStorageBulkheadOptions Bulkhead { get; set; } = new();

    /// <summary>The circuit breaker that stops a failing dependency from being retried call after call.</summary>
    public ObjectStorageBreakerOptions Breaker { get; set; } = new();

    /// <summary>
    /// The per-call timeout the breaker and the bulkhead both wrap. A call that has not finished by this
    /// point is a failure for the breaker's purposes, whatever it eventually returns.
    /// </summary>
    [Range(typeof(TimeSpan), "00:00:01", "00:05:00")]
    public TimeSpan CallTimeout { get; set; } = TimeSpan.FromSeconds(10);

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

        if (key.StartsWith("exports/", StringComparison.Ordinal))
        {
            return ExportsBucket;
        }

        return key.StartsWith("quarantine/", StringComparison.Ordinal) ? QuarantineBucket : MediaBucket;
    }
}

/// <summary>
/// The bulkhead's own settings, bound from <c>ObjectStorage:Bulkhead</c>. Sourced from
/// <c>docs/nfr/capacity-and-performance.md</c> section 2.4, not invented: 2 concurrent calls in the worker
/// (the same figure as the worker's <c>MediaProcessing</c> decode bulkhead), 6 in the web host (the same
/// figure as the concurrent-image-upload row). Each host's own <c>appsettings.json</c> sets the value that
/// applies to it; this default is the smaller, safer of the two.
/// </summary>
public sealed class ObjectStorageBulkheadOptions
{
    /// <summary>The most calls into object storage this process makes at once. Any more are rejected, not queued.</summary>
    [Range(1, 100)]
    public int MaxConcurrentCalls { get; set; } = 2;
}

/// <summary>
/// The circuit breaker's own settings, bound from <c>ObjectStorage:Breaker</c>. Unlike the bulkhead figures,
/// neither default here is sourced from an existing document — <c>docs/architecture/resilience-policies.md</c>
/// section 1 and <c>docs/prd/assumptions-and-open-decisions.md</c> **OD-27** record both as proposed, to be
/// confirmed, rather than presenting them as settled.
/// </summary>
public sealed class ObjectStorageBreakerOptions
{
    /// <summary>Consecutive failures, of any kind the timeout or the adapter itself can produce, before the breaker opens.</summary>
    [Range(1, 50)]
    public int FailureThreshold { get; set; } = 5;

    /// <summary>How long the breaker stays open before it lets one call through to test the dependency.</summary>
    [Range(typeof(TimeSpan), "00:00:01", "00:10:00")]
    public TimeSpan BreakDuration { get; set; } = TimeSpan.FromSeconds(30);
}
