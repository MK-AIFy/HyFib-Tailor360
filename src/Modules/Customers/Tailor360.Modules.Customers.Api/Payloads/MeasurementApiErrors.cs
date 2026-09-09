using Tailor360.Platform.Abstractions.Results;

namespace Tailor360.Modules.Customers.Api.Payloads;

/// <summary>
/// Failures that belong to the transport rather than to the template.
/// </summary>
/// <remarks>
/// Declared here rather than taken from the module's domain error list, because these are about what arrived on
/// the wire — an enumeration member spelled wrongly, a reason left off a request that needs one — and the Api
/// layer's permitted references do not include Domain.
/// </remarks>
public static class MeasurementApiErrors
{
    /// <summary>A value did not name a member of the enumeration the field takes.</summary>
    /// <param name="field">The field.</param>
    /// <param name="value">What arrived.</param>
    public static Error NotAValidValue(string field, string? value) => Error.Validation(
        "measurements.not-a-valid-value",
        $"'{value}' is not one of the values this field takes.",
        field);

    /// <summary>An act the trail has to explain arrived without a reason.</summary>
    public static readonly Error ReasonRequired = Error.Validation(
        "measurements.value-required",
        "A required value was not supplied.",
        "reason");
}
