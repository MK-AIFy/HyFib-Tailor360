using System.Diagnostics;

namespace Tailor360.Modules.Identity.Application.Timing;

/// <summary>
/// The shipped uniform-timing guard: run the work, then wait out whatever is left of the floor.
/// </summary>
/// <remarks>
/// Two details are deliberate. The padding runs in a <c>finally</c>, so a path that fails early is
/// padded exactly like one that succeeds — an exception that returned immediately would be as
/// informative as a fast success. And the wait does not observe the caller's cancellation token: a
/// caller who could cancel the pad could measure the unpadded work, which is the thing being hidden.
/// <para>
/// Elapsed time is measured with <see cref="Stopwatch"/> rather than the clock abstraction on purpose.
/// <c>IClock</c> answers "what time is it", which tests replace with a fixed instant; this needs "how
/// long did that take", which a fixed instant cannot answer and which no test should want to fake.
/// </para>
/// </remarks>
public sealed class UniformResponseTime : IUniformResponseTime
{
    /// <inheritdoc />
    public async Task<TResult> RunAsync<TResult>(
        TimeSpan floor,
        Func<CancellationToken, Task<TResult>> work,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(work);

        var started = Stopwatch.GetTimestamp();
        try
        {
            return await work(cancellationToken);
        }
        finally
        {
            // Waited in a loop, and rounded up to the next whole millisecond, because a single
            // Task.Delay does not quite guarantee the floor: it truncates its argument to whole
            // milliseconds and its timer may fire a tick early, so one call can return a fraction of a
            // millisecond short. That fraction would never leak anything on its own; the loop is here
            // because "this call takes at least the floor" is either true or it is a comment.
            var remaining = floor - Stopwatch.GetElapsedTime(started);
            while (remaining > TimeSpan.Zero)
            {
                await Task.Delay(
                    TimeSpan.FromMilliseconds(Math.Ceiling(remaining.TotalMilliseconds)),
                    CancellationToken.None);

                remaining = floor - Stopwatch.GetElapsedTime(started);
            }
        }
    }
}
