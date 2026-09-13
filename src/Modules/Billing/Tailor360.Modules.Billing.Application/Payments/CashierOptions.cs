using System.ComponentModel.DataAnnotations;
using Microsoft.Extensions.Options;

namespace Tailor360.Modules.Billing.Application.Payments;

/// <summary>
/// The cashier session's configuration. The variance a close may carry without a reason is a number
/// nobody has sourced (invariants IOD-05 says per branch, configurable; the value is open decision OD-24),
/// so the default is zero: every variance, over or short, is explained. Raising it is the Owner's act,
/// with the accountant.
/// </summary>
public sealed class CashierOptions
{
    /// <summary>The configuration section.</summary>
    public const string SectionName = "Billing:Cashier";

    /// <summary>The largest per-mode variance, in rupees, that closes without a reason. Zero or more, to the paisa.</summary>
    [Range(typeof(decimal), "0", "1000000")]
    public decimal VarianceReasonThreshold { get; set; }
}

/// <summary>The check the annotation cannot make: a threshold is an amount to the paisa, not a fraction of one.</summary>
public sealed class CashierOptionsValidator : IValidateOptions<CashierOptions>
{
    /// <inheritdoc />
    public ValidateOptionsResult Validate(string? name, CashierOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        return decimal.Round(options.VarianceReasonThreshold, 2) == options.VarianceReasonThreshold
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail("Billing:Cashier:VarianceReasonThreshold is an amount in rupees to the paisa.");
    }
}
