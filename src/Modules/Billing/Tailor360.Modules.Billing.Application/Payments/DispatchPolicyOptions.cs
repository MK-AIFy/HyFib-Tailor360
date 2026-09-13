using System.ComponentModel.DataAnnotations;
using Microsoft.Extensions.Options;
using Tailor360.Modules.Billing.Domain.Payments;

namespace Tailor360.Modules.Billing.Application.Payments;

/// <summary>
/// The dispatch policy's configuration (plan lines 1902-1907, OD-04). The numbers are not sourced: the
/// shape is built and the interim default is the strictest reading of every knob — <see cref="Full"/>,
/// no advance floor — so that nothing here loosens dispatch until the Owner decides otherwise.
/// </summary>
public sealed class DispatchPolicyOptions
{
    /// <summary>The configuration section.</summary>
    public const string SectionName = "Billing:Dispatch";

    /// <summary>
    /// The version stamped on every exception approved and every eligibility answer given while it is
    /// in force. Bumping it — because the rule, the threshold or the advance floor changed — is what
    /// makes a dispatch exception approved under the old policy refused at consumption
    /// (<c>billing.dispatch-exception-policy-version-changed</c>) rather than silently honoured under
    /// numbers that no longer apply.
    /// </summary>
    [Required]
    public string Version { get; set; } = "1";

    /// <summary>The rule: <see cref="DispatchPolicyRule.Full"/> (default) or <see cref="DispatchPolicyRule.PartialThreshold"/>.</summary>
    public DispatchPolicyRule Rule { get; set; } = DispatchPolicyRule.Full;

    /// <summary>The paid share, from zero to one, the partial rule requires. Unused under <see cref="DispatchPolicyRule.Full"/>.</summary>
    [Range(0d, 1d)]
    public decimal PartialThreshold { get; set; }

    /// <summary>Whether an unapplied advance alone may clear an order with no posted invoice.</summary>
    public bool AllowOnAdvance { get; set; }

    /// <summary>
    /// The floor an unapplied advance must clear for <see cref="AllowOnAdvance"/> to take effect, in
    /// rupees. Zero — the interim default — means the flag has no effect even when set, because OD-04
    /// has not sourced a number and a floor of zero is the only default that invents nothing.
    /// </summary>
    [Range(typeof(decimal), "0", "1000000")]
    public decimal AdvanceThreshold { get; set; }
}

/// <summary>Validates <see cref="DispatchPolicyOptions"/> beyond what data annotations express.</summary>
public sealed class DispatchPolicyOptionsValidator : IValidateOptions<DispatchPolicyOptions>
{
    /// <inheritdoc />
    public ValidateOptionsResult Validate(string? name, DispatchPolicyOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (decimal.Round(options.PartialThreshold, 4) != options.PartialThreshold)
        {
            return ValidateOptionsResult.Fail("Billing:Dispatch:PartialThreshold is a share between 0 and 1.");
        }

        return decimal.Round(options.AdvanceThreshold, 2) == options.AdvanceThreshold
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail("Billing:Dispatch:AdvanceThreshold is an amount in rupees to the paisa.");
    }
}
