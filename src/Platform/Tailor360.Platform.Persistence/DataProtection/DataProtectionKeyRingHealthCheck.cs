using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

namespace Tailor360.Platform.Persistence.DataProtection;

/// <summary>
/// Reports whether the data-protection key ring is loaded and stored where it survives a restart. It
/// is a startup check (Section 4.4): an instance whose ring cannot be read would accept no anti-forgery
/// token and no protected payload, so it must not be put into rotation.
/// </summary>
/// <remarks>
/// The check asserts the storage as well as the contents. A ring that has silently fallen back to the
/// file system or to memory still answers "here are some keys" — and then answers with different keys
/// after a restart or on the second replica, which presents to a user as anti-forgery failures that
/// clear when they sign in again and come back the next day. Naming the repository type here is what
/// turns that into a startup failure instead.
/// </remarks>
/// <param name="keyManager">The key ring.</param>
/// <param name="keyManagement">Key-management options, which carry the repository the ring is stored in.</param>
public sealed class DataProtectionKeyRingHealthCheck(
    IKeyManager keyManager,
    IOptions<KeyManagementOptions> keyManagement)
    : IHealthCheck
{
    /// <summary>The repository type the deployment requires.</summary>
    private const string RequiredRepository = "EntityFrameworkCoreXmlRepository";

    /// <inheritdoc />
    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        var repositoryName = keyManagement.Value.XmlRepository?.GetType().Name ?? "(the framework default)";

        if (!repositoryName.StartsWith(RequiredRepository, StringComparison.Ordinal))
        {
            return Task.FromResult(HealthCheckResult.Unhealthy(
                "The data-protection key ring is not persisted in the database. Tokens issued by this "
                + "instance would stop being accepted after a restart and would be rejected by every "
                + $"other instance. Repository in use: {repositoryName}."));
        }

        int keyCount;
        try
        {
            // Reading the ring is the point: it proves the table is reachable and readable, which is
            // exactly what a probe run before the instance takes traffic needs to establish.
            keyCount = keyManager.GetAllKeys().Count;
        }
        catch (InvalidOperationException exception)
        {
            return Task.FromResult(HealthCheckResult.Unhealthy(
                "The data-protection key ring could not be read.", exception));
        }

        // An empty ring is healthy on a first start: the ring creates its first key when something is
        // first protected, and refusing to serve until then would deadlock a fresh deployment.
        var data = new Dictionary<string, object>(StringComparer.Ordinal)
        {
            ["repository"] = repositoryName,
            ["keys"] = keyCount,
        };

        return Task.FromResult(HealthCheckResult.Healthy(
            keyCount == 0
                ? "The key ring is empty; the first key is created when a payload is first protected."
                : "The key ring is loaded from the database.",
            data));
    }
}
