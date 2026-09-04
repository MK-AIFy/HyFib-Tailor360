using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Npgsql;
using Tailor360.Platform.Persistence.Contexts;

namespace Tailor360.Platform.Persistence.Health;

/// <summary>
/// Reports whether audit entries are landing in the default partition. Zero is the healthy state:
/// every entry belongs to a month that has its own partition. A non-zero count means partition
/// maintenance has stopped running, and those rows must be relocated before that month can be given a
/// partition of its own. Nothing is broken at that point, which is exactly why it needs reporting —
/// the failure would otherwise surface months later as a migration that will not apply.
/// </summary>
/// <param name="database">The platform context.</param>
public sealed class AuditPartitionHealthCheck(PlatformDbContext database) : IHealthCheck
{
    /// <inheritdoc />
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        var connection = (NpgsqlConnection)database.Database.GetDbConnection();
        await database.Database.OpenConnectionAsync(cancellationToken);

        try
        {
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT platform.audit_default_partition_rows()";

            var rows = Convert.ToInt64(
                await command.ExecuteScalarAsync(cancellationToken), CultureInfo.InvariantCulture);

            return rows == 0
                ? HealthCheckResult.Healthy()
                : HealthCheckResult.Degraded(
                    "Audit entries are landing in the default partition; partition maintenance is behind.");
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return HealthCheckResult.Degraded("The audit partition state could not be read.");
        }
        finally
        {
            await database.Database.CloseConnectionAsync();
        }
    }
}
