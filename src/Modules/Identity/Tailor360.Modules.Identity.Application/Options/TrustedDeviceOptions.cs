using System.ComponentModel.DataAnnotations;
using Tailor360.Modules.Identity.Domain.Sessions;

namespace Tailor360.Modules.Identity.Application.Options;

/// <summary>
/// Whether a counter device may be remembered so that later sign-ins from it need only the password,
/// and for how long.
/// </summary>
/// <remarks>
/// Remembering a device weakens sign-in on purpose, for the one case where not weakening it drives
/// people to route around the control altogether: a receptionist signing in twenty times a shift on a
/// shared tablet. Two things keep it bounded. It skips the challenge but never counts as a strong
/// authentication, so every step-up endpoint still asks; and the domain caps the memory at thirty days
/// whatever is configured here.
/// </remarks>
public sealed class TrustedDeviceOptions
{
    /// <summary>The configuration section these options bind to.</summary>
    public const string SectionName = "Identity:TrustedDevices";

    /// <summary>True when a device may be remembered at all. Off unless the owner turns it on.</summary>
    public bool Enabled { get; set; }

    /// <summary>How long a device is remembered, capped by the domain at thirty days.</summary>
    [Range(typeof(TimeSpan), "1.00:00:00", "30.00:00:00")]
    public TimeSpan Lifetime { get; set; } = TimeSpan.FromDays(30);

    /// <summary>True when the configured lifetime is one the domain will accept.</summary>
    public bool IsUsable
        => Lifetime > TimeSpan.Zero && Lifetime <= TimeSpan.FromDays(TrustedDevice.MaximumLifetimeDays);
}
