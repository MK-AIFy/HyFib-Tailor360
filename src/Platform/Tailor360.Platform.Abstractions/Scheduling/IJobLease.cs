namespace Tailor360.Platform.Abstractions.Scheduling;

/// <summary>
/// Taking and releasing the lease that stops two worker instances running one job at once.
/// </summary>
/// <remarks>
/// <para>
/// The interface exists so a job's <em>shell</em> can be tested. A background job is a timer, a lease,
/// a call and a try/catch, and every one of those is a decision worth asserting: that a run happens at
/// start-up, that a second instance which cannot take the lease does nothing, that the lease is
/// released when the work throws, that a failed pass does not stop the loop. None of that could be
/// reached before, because the only implementation was a sealed class over a live
/// <c>PlatformDbContext</c>, so testing any of it meant standing up a database and a worker host — and
/// the result was three jobs whose shells were never executed by anything, recorded as debt in
/// <c>.github/coverage-floors.json</c>.
/// </para>
/// <para>
/// It lives in <c>Platform.Abstractions</c> rather than beside either party, because the implementation
/// is in <c>Platform.Persistence</c> and the consumers are worker hosts: those two projects are
/// siblings that share only the abstractions, so this is the one place both can see.
/// </para>
/// <para>
/// It is deliberately the whole surface of the lease and nothing more. It is not a general
/// distributed-lock abstraction and has no renewal, no re-entrancy and no ownership query: a job takes
/// the lease, does its work, and releases it, and anything richer belongs to the job that needs it.
/// </para>
/// </remarks>
public interface IJobLease
{
    /// <summary>
    /// Attempts to take the lease. Returns true when this instance may run the job.
    /// </summary>
    /// <remarks>
    /// A lease that has expired is taken over, so an instance that crashed mid-job does not block the
    /// job for ever.
    /// </remarks>
    /// <param name="jobName">The job, in the <c>module.job</c> shape a <c>[WorkerJob]</c> declares.</param>
    /// <param name="owner">Which instance is asking, so a release can be checked against it.</param>
    /// <param name="duration">How long the lease is held before another instance may take it over.</param>
    /// <param name="cancellationToken">Cancels the attempt.</param>
    /// <returns>True when the lease was taken.</returns>
    Task<bool> TryAcquireAsync(
        string jobName,
        string owner,
        TimeSpan duration,
        CancellationToken cancellationToken = default);

    /// <summary>Releases the lease, if this owner still holds it.</summary>
    /// <param name="jobName">The job.</param>
    /// <param name="owner">The instance releasing it.</param>
    /// <param name="cancellationToken">Cancels the release.</param>
    /// <returns>A task that completes when the lease is released.</returns>
    Task ReleaseAsync(string jobName, string owner, CancellationToken cancellationToken = default);
}
