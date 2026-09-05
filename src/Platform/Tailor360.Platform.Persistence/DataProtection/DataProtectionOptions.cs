using System.ComponentModel.DataAnnotations;

namespace Tailor360.Platform.Persistence.DataProtection;

/// <summary>
/// How the ASP.NET Core data-protection key ring behaves. The ring encrypts the anti-forgery token
/// pair and every other protected payload, so if it is lost the running application starts rejecting
/// the tokens it issued a moment earlier.
/// </summary>
/// <remarks>
/// There is deliberately no setting for <em>where</em> the ring is stored. It is always the
/// <c>platform.data_protection_keys</c> table (Section 4.4): the file-system default cannot work here,
/// because the container images run with a read-only root file system and because a second web replica
/// would generate its own keys and reject the first replica's tokens. Making the location configurable
/// would only offer a way to reintroduce both faults.
/// </remarks>
public sealed class DataProtectionOptions
{
    /// <summary>The configuration section these options bind to.</summary>
    public const string SectionName = "Security:DataProtection";

    /// <summary>
    /// The name every host in this deployment shares. Payloads protected by one host are readable by
    /// another only when the application names agree, so the web host and the worker must use the same
    /// value; changing it invalidates every outstanding protected payload at once.
    /// </summary>
    [Required]
    [StringLength(100, MinimumLength = 3)]
    public string ApplicationName { get; set; } = "HyFib.Tailor360";

    /// <summary>
    /// How long a newly created key is used to protect new payloads before the ring rolls to a fresh
    /// one. Ninety days is the rotation period Section 4.4 sets. Old keys stay in the ring and keep
    /// decrypting what they protected, so rotation is not a sign-out event.
    /// </summary>
    [Range(typeof(TimeSpan), "7.00:00:00", "365.00:00:00")]
    public TimeSpan KeyLifetime { get; set; } = TimeSpan.FromDays(90);
}
