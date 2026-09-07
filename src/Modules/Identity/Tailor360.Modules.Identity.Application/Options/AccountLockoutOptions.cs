using System.ComponentModel.DataAnnotations;
using Tailor360.Modules.Identity.Domain.Lockout;

namespace Tailor360.Modules.Identity.Application.Options;

/// <summary>
/// The configurable part of the progressive lockout. The defaults are the ones issue #23 sets: five
/// tolerated failures, then a lockout doubling from one minute to a thirty-minute ceiling.
/// </summary>
/// <remarks>
/// The name says "account" because ASP.NET Core Identity ships a <c>LockoutOptions</c> of its own and
/// this is not it: that one configures the framework's own user store, which this module does not use.
/// </remarks>
public sealed class AccountLockoutOptions
{
    /// <summary>The configuration section these options bind to.</summary>
    public const string SectionName = "Identity:Lockout";

    /// <summary>How many consecutive failures are tolerated before any lockout applies.</summary>
    [Range(1, 20)]
    public int Threshold { get; set; } = 5;

    /// <summary>The lockout applied at the first failure past the threshold.</summary>
    [Range(typeof(TimeSpan), "00:00:15", "01:00:00")]
    public TimeSpan BaseDuration { get; set; } = TimeSpan.FromMinutes(1);

    /// <summary>The ceiling the doubling stops at.</summary>
    [Range(typeof(TimeSpan), "00:01:00", "24:00:00")]
    public TimeSpan MaximumDuration { get; set; } = TimeSpan.FromMinutes(30);

    /// <summary>The domain policy these options describe.</summary>
    public LockoutPolicy ToPolicy()
    {
        var created = LockoutPolicy.Create(Threshold, BaseDuration, MaximumDuration);
        return created.IsSuccess ? created.Value : LockoutPolicy.Default;
    }

    /// <summary>True when the configured values make a usable policy.</summary>
    public bool IsUsable => LockoutPolicy.Create(Threshold, BaseDuration, MaximumDuration).IsSuccess;
}
