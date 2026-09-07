namespace Tailor360.Modules.Identity.Application.Timing;

/// <summary>
/// Makes an operation take the same time whichever way it goes, so that a caller cannot read the
/// answer off the clock.
/// </summary>
/// <remarks>
/// It exists for the recovery request, where the honest implementation is fast when the address is
/// unknown and slower when it is known, and where that difference is an account-enumeration oracle
/// worth more to an attacker than the endpoint's response body. The guard puts a floor under both
/// paths; the work either path does must stay well under that floor, which is why the message is
/// queued rather than sent inside it.
/// <para>
/// A floor is a mitigation, not a proof. It removes the difference an attacker can see with a handful
/// of requests; the per-account and per-IP rate limits are what stop them collecting enough samples to
/// see through it.
/// </para>
/// </remarks>
public interface IUniformResponseTime
{
    /// <summary>Runs the work and does not return until at least <paramref name="floor"/> has passed.</summary>
    /// <typeparam name="TResult">What the work produces.</typeparam>
    /// <param name="floor">The minimum time the whole call takes.</param>
    /// <param name="work">The work to run.</param>
    /// <param name="cancellationToken">Cancellation token passed to the work.</param>
    Task<TResult> RunAsync<TResult>(
        TimeSpan floor,
        Func<CancellationToken, Task<TResult>> work,
        CancellationToken cancellationToken = default);
}
