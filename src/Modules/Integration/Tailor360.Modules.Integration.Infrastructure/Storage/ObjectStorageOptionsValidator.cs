using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Tailor360.Modules.Integration.Infrastructure.Storage;

/// <summary>
/// The guard that keeps the in-memory store out of a deployment: outside Development the endpoint and both
/// keys must be present at start, or the host does not start. In Development an endpoint without keys is
/// allowed and falls back to the in-memory adapter with a warning, so a developer without the secrets
/// written still gets a working loop.
/// </summary>
/// <param name="environment">The host's environment.</param>
public sealed class ObjectStorageOptionsValidator(IHostEnvironment environment) : IValidateOptions<ObjectStorageOptions>
{
    /// <inheritdoc />
    public ValidateOptionsResult Validate(string? name, ObjectStorageOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (environment.IsDevelopment())
        {
            return ValidateOptionsResult.Success;
        }

        if (!options.IsConfigured)
        {
            return ValidateOptionsResult.Fail(
                "ObjectStorage:Endpoint is required outside Development: without it every rendered document would live in one process's memory.");
        }

        return options.HasCredentials
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail("ObjectStorage:AccessKey and ObjectStorage:SecretKey are mounted secrets and both must be present outside Development.");
    }
}
