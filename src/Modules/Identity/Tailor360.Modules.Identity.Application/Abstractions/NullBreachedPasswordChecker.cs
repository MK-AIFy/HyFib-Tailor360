namespace Tailor360.Modules.Identity.Application.Abstractions;

/// <summary>
/// The default breached-password checker: it has no list, so it reports nothing as breached.
/// </summary>
/// <remarks>
/// Registering this rather than leaving the dependency unsatisfied keeps the check on one code path
/// whether or not a list is configured, so enabling a real checker later is a registration change and
/// not a change to the password-setting logic. It is registered with <c>TryAdd</c>, so an adapter
/// registered first wins.
/// </remarks>
public sealed class NullBreachedPasswordChecker : IBreachedPasswordChecker
{
    /// <inheritdoc />
    public ValueTask<bool> IsBreachedAsync(string candidate, CancellationToken cancellationToken = default)
        => ValueTask.FromResult(false);
}
