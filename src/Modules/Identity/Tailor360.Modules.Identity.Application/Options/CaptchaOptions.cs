using System.ComponentModel.DataAnnotations;

namespace Tailor360.Modules.Identity.Application.Options;

/// <summary>
/// Whether a human check is demanded once an address's attempts look automated, and what verifies it.
/// </summary>
/// <remarks>
/// It is off by default and shipped with no provider. That is a decision, not an omission: every
/// available service is a third party that would see the address of everyone signing in to a shop, and
/// choosing one is a product decision with a privacy consequence, not something an implementation
/// should settle. The adapter exists so that a deployment which has made that decision can register a
/// verifier without any change to the sign-in path.
/// </remarks>
public sealed class CaptchaOptions
{
    /// <summary>The configuration section these options bind to.</summary>
    public const string SectionName = "Identity:Captcha";

    /// <summary>True when a configured verifier should be consulted. Off unless a deployment says otherwise.</summary>
    public bool Enabled { get; set; }

    /// <summary>The provider key a registered adapter recognises. Meaningless while disabled.</summary>
    [StringLength(64)]
    public string Provider { get; set; } = string.Empty;
}
