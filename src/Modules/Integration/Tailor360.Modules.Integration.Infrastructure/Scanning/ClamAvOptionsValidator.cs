using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Tailor360.Modules.Integration.Infrastructure.Scanning;

/// <summary>
/// The guard that keeps the fake scanner out of a deployment: outside Development, a host must be
/// configured at start, or the host does not start. The mirror of
/// <c>ObjectStorageOptionsValidator</c> for the same reason — a scanner that quietly answers "clean"
/// to everything is a security control in name only, and that must never be true anywhere but a
/// developer's own machine or the test host.
/// </summary>
/// <param name="environment">The host's environment.</param>
public sealed class ClamAvOptionsValidator(IHostEnvironment environment) : IValidateOptions<ClamAvOptions>
{
    /// <inheritdoc />
    public ValidateOptionsResult Validate(string? name, ClamAvOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (options.ScanTimeout <= TimeSpan.Zero)
        {
            return ValidateOptionsResult.Fail(
                "ClamAv:ScanTimeout must be a positive duration, or a hung scan would never be treated as unavailable.");
        }

        if (environment.IsDevelopment())
        {
            return ValidateOptionsResult.Success;
        }

        return options.IsConfigured
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(
                "ClamAv:Host is required outside Development: without it every upload would be waved through unscanned.");
    }
}
