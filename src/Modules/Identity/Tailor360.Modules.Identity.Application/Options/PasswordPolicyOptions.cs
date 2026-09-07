using System.ComponentModel.DataAnnotations;
using Tailor360.Modules.Identity.Domain.Credentials;

namespace Tailor360.Modules.Identity.Application.Options;

/// <summary>
/// The configurable part of the password policy. The floor is not configurable: twelve characters is
/// the minimum this system accepts, and a configuration file cannot lower it.
/// </summary>
public sealed class PasswordPolicyOptions
{
    /// <summary>The configuration section these options bind to.</summary>
    public const string SectionName = "Identity:PasswordPolicy";

    /// <summary>The shortest password accepted. Never below twelve.</summary>
    [Range(PasswordPolicy.AbsoluteMinimumLength, PasswordPolicy.AbsoluteMaximumLength)]
    public int MinimumLength { get; set; } = PasswordPolicy.AbsoluteMinimumLength;

    /// <summary>The longest password the hasher is asked to process.</summary>
    [Range(PasswordPolicy.AbsoluteMinimumLength, PasswordPolicy.AbsoluteMaximumLength)]
    public int MaximumLength { get; set; } = 256;

    /// <summary>
    /// Whether a candidate is checked against the configured breached-password list. Turning it off
    /// does not remove the code path, so switching it back on needs no deployment of new logic.
    /// </summary>
    public bool CheckBreachedPasswords { get; set; } = true;

    /// <summary>The domain policy these options describe.</summary>
    public PasswordPolicy ToPolicy()
    {
        var created = PasswordPolicy.Create(MinimumLength, MaximumLength);
        return created.IsSuccess ? created.Value : PasswordPolicy.Default;
    }

    /// <summary>True when the configured lengths make a usable policy.</summary>
    public bool IsUsable => PasswordPolicy.Create(MinimumLength, MaximumLength).IsSuccess;
}
