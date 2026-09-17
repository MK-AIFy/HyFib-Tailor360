using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Tailor360.Modules.Integration.Infrastructure.Storage;

/// <summary>
/// The guard that keeps the in-memory store out of a deployment: outside Development the endpoint and both
/// keys must be present at start, or the host does not start. In Development an endpoint without keys is
/// allowed and falls back to the in-memory adapter with a warning, so a developer without the secrets
/// written still gets a working loop.
/// </summary>
/// <remarks>
/// The bulkhead, breaker and timeout settings below are validated here rather than left to
/// <c>[Range]</c> attributes on the nested <see cref="ObjectStorageBulkheadOptions"/> and
/// <see cref="ObjectStorageBreakerOptions"/> alone: reflection-based data-annotation validation does not
/// walk into a nested options object by default, and a resilience setting that silently failed to be
/// checked would be worse than one that was never added. These checks apply in every environment, not
/// only outside Development, because a broken bulkhead or breaker is a bug regardless of where it runs.
/// </remarks>
/// <param name="environment">The host's environment.</param>
public sealed class ObjectStorageOptionsValidator(IHostEnvironment environment) : IValidateOptions<ObjectStorageOptions>
{
    /// <inheritdoc />
    public ValidateOptionsResult Validate(string? name, ObjectStorageOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (options.Bulkhead.MaxConcurrentCalls < 1)
        {
            return ValidateOptionsResult.Fail(
                "ObjectStorage:Bulkhead:MaxConcurrentCalls must be at least 1, or object storage could never be reached at all.");
        }

        if (options.Breaker.FailureThreshold < 1)
        {
            return ValidateOptionsResult.Fail("ObjectStorage:Breaker:FailureThreshold must be at least 1.");
        }

        if (options.Breaker.BreakDuration <= TimeSpan.Zero)
        {
            return ValidateOptionsResult.Fail(
                "ObjectStorage:Breaker:BreakDuration must be a positive duration, or an open breaker would never half-open.");
        }

        if (options.CallTimeout <= TimeSpan.Zero)
        {
            return ValidateOptionsResult.Fail(
                "ObjectStorage:CallTimeout must be a positive duration, or a hung call would never be treated as a failure.");
        }

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
