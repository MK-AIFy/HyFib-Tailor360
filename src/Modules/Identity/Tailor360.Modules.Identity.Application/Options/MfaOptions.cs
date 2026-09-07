using System.ComponentModel.DataAnnotations;
using Tailor360.Modules.Identity.Domain.Mfa;
using Tailor360.Modules.Identity.Domain.Users;

namespace Tailor360.Modules.Identity.Application.Options;

/// <summary>
/// How the second factor behaves, and — the part that is configuration rather than code on purpose —
/// who has to have one.
/// </summary>
/// <remarks>
/// Issue #23 states the rule as "Owner, Admin, Cashier and any role holding <c>billing.*</c> or
/// <c>admin.*</c> permissions, configurable per role". Those are the defaults below, and a deployment
/// may widen them. It may not usefully narrow them below what the permission catalogue asks for: a
/// permission marked <c>RequiresMfa</c> in #24's catalogue makes multi-factor mandatory for anyone who
/// holds it whatever this file says, because that flag is on the permission rather than on the role and
/// is the thing an endpoint's authorisation actually checks.
/// </remarks>
public sealed class MfaOptions
{
    /// <summary>The configuration section these options bind to.</summary>
    public const string SectionName = "Identity:Mfa";

    /// <summary>The name an authenticator application lists the account under.</summary>
    [Required]
    [StringLength(64, MinimumLength = 1)]
    public string Issuer { get; set; } = "HyFib Tailor 360";

    /// <summary>How many digits a generated code has.</summary>
    [Range(6, 8)]
    public int Digits { get; set; } = TotpEnrolment.DefaultDigits;

    /// <summary>How long one step lasts, in seconds.</summary>
    [Range(15, 120)]
    public int PeriodSeconds { get; set; } = TotpEnrolment.DefaultPeriodSeconds;

    /// <summary>
    /// How many steps either side of the present are accepted, for devices whose clocks disagree. One
    /// step — thirty seconds each way — covers ordinary drift. Every extra step lengthens the window in
    /// which a code seen over a shoulder still works, so the range stops at two.
    /// </summary>
    [Range(0, 2)]
    public int DriftSteps { get; set; } = 1;

    /// <summary>How many recovery codes are issued in one sheet.</summary>
    [Range(StaffUser.MinimumRecoveryCodes, StaffUser.MaximumRecoveryCodes)]
    public int RecoveryCodeCount { get; set; } = 10;

    /// <summary>
    /// How few unspent codes may remain before the holder is told to print a new sheet. Reaching zero
    /// unnoticed is how someone with a lost phone finds they have no way back in.
    /// </summary>
    [Range(1, 8)]
    public int RecoveryCodeReissueThreshold { get; set; } = 3;

    /// <summary>True when every account must enrol a second factor, whatever its role.</summary>
    public bool RequiredForEveryone { get; set; }

    /// <summary>
    /// Roles whose holders must enrol a second factor. Compared without regard to case, so a role named
    /// <c>owner</c> and one named <c>Owner</c> are the same rule rather than a gap.
    /// </summary>
    /// <remarks>
    /// Empty means "use <see cref="DefaultRequiredRoles"/>", which is the #23 set. The default is not
    /// pre-populated here because the configuration binder appends to a list rather than replacing it,
    /// so a deployment that listed one role would silently get four.
    /// </remarks>
    public IList<string> RequiredRoles { get; } = [];

    /// <summary>
    /// Permission prefixes whose holders must enrol a second factor, whichever role granted them. This
    /// is what stops a custom role from quietly becoming an administrator without one. Empty means
    /// <see cref="DefaultRequiredPermissionPrefixes"/>.
    /// </summary>
    public IList<string> RequiredPermissionPrefixes { get; } = [];

    /// <summary>The roles #23 requires a second factor for when configuration names none.</summary>
    public static IReadOnlyList<string> DefaultRequiredRoles { get; } = ["Owner", "Admin", "Cashier"];

    /// <summary>The permission prefixes #23 requires a second factor for when configuration names none.</summary>
    public static IReadOnlyList<string> DefaultRequiredPermissionPrefixes { get; } = ["admin.", "billing."];

    /// <summary>The roles actually in force.</summary>
    public IReadOnlyList<string> EffectiveRequiredRoles
        => RequiredRoles.Count > 0 ? [.. RequiredRoles] : DefaultRequiredRoles;

    /// <summary>The permission prefixes actually in force.</summary>
    public IReadOnlyList<string> EffectiveRequiredPermissionPrefixes
        => RequiredPermissionPrefixes.Count > 0 ? [.. RequiredPermissionPrefixes] : DefaultRequiredPermissionPrefixes;

    /// <summary>The step length as a duration.</summary>
    public TimeSpan Period => TimeSpan.FromSeconds(PeriodSeconds);
}
