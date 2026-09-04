using Microsoft.EntityFrameworkCore;
using Tailor360.Platform.Abstractions.Time;
using Tailor360.Platform.Persistence.Contexts;

namespace Tailor360.Platform.Persistence.Scheduling;

/// <summary>
/// Takes a lease on a scheduled job. Two worker instances is the normal deployment and both wake for
/// the same job; the lease is what stops both from running it. The claim is a conditional upsert, so
/// exactly one instance wins without any coordination beyond the database.
/// </summary>
/// <param name="context">The platform context.</param>
/// <param name="clock">The clock.</param>
public sealed class JobLeaseService(PlatformDbContext context, IClock clock)
{
    /// <summary>
    /// Attempts to take the lease. Returns true when this instance may run the job. A lease that has
    /// expired is taken over, so an instance that crashed mid-job does not block the job for ever.
    /// </summary>
    public async Task<bool> TryAcquireAsync(
        string jobName,
        string owner,
        TimeSpan duration,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(jobName);
        ArgumentException.ThrowIfNullOrWhiteSpace(owner);

        var now = clock.UtcNow;

        var claimed = await context.Database.ExecuteSqlAsync(
            $"""
             INSERT INTO platform.job_leases (job_name, owner, acquired_at, expires_at)
             VALUES ({jobName}, {owner}, {now}, {now + duration})
             ON CONFLICT (job_name) DO UPDATE
                 SET owner = EXCLUDED.owner,
                     acquired_at = EXCLUDED.acquired_at,
                     expires_at = EXCLUDED.expires_at
               WHERE platform.job_leases.expires_at < {now}
             """,
            cancellationToken);

        return claimed == 1;
    }

    /// <summary>Releases a lease this instance holds, so another instance need not wait it out.</summary>
    public async Task ReleaseAsync(string jobName, string owner, CancellationToken cancellationToken = default)
        => await context.Database.ExecuteSqlAsync(
            $"""
             UPDATE platform.job_leases SET expires_at = {clock.UtcNow}
              WHERE job_name = {jobName} AND owner = {owner}
             """,
            cancellationToken);
}
