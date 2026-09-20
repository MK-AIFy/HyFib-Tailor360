using System.ComponentModel.DataAnnotations;

namespace Tailor360.Modules.Integration.Infrastructure.Scanning;

/// <summary>
/// Where the malware scanner lives, bound from <c>ClamAv</c>. With no host set, the fake scanner
/// serves — what the test host and a developer without the <c>scanner</c> compose profile get, and
/// what production must never get: <see cref="ClamAvOptionsValidator"/> refuses a start outside
/// Development without a host, the same guard <c>ObjectStorageOptionsValidator</c> applies to object
/// storage. Neither field here is a secret: clamd's <c>INSTREAM</c> protocol carries no credential.
/// </summary>
public sealed class ClamAvOptions
{
    /// <summary>The configuration section.</summary>
    public const string SectionName = "ClamAv";

    /// <summary>The clamd host, for example <c>clamav</c> inside the compose network; null for the fake scanner.</summary>
    public string? Host { get; set; }

    /// <summary>The clamd port. <c>infra/compose/docker-compose.yml</c>'s default is 3310.</summary>
    [Range(1, 65535)]
    public int Port { get; set; } = 3310;

    /// <summary>
    /// The per-scan timeout. A scan that has not answered by this point is treated as
    /// <see cref="Tailor360.Platform.Abstractions.Ports.MalwareScanOutcome.Unavailable"/>, the same
    /// posture <c>ObjectStorageOptions.CallTimeout</c> takes toward a hung call.
    /// </summary>
    [Range(typeof(TimeSpan), "00:00:01", "00:05:00")]
    public TimeSpan ScanTimeout { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>True when a host is configured and the real adapter serves.</summary>
    public bool IsConfigured => !string.IsNullOrWhiteSpace(Host);
}
