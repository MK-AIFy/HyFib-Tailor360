namespace Tailor360.Modules.Integration.Infrastructure.Storage;

/// <summary>
/// Thrown by <see cref="ResilientObjectStorage"/> instead of letting a caller wait on a dependency that is
/// known to be failing: the circuit breaker is open, the bulkhead has no free slot, or the call ran past
/// its timeout. A caller that wants <c>docs/architecture/failure-modes.md</c> section 5's
/// <c>503 media.unavailable</c> contract catches this type specifically, rather than every
/// <see cref="Exception"/> the underlying adapter could throw.
/// </summary>
/// <param name="message">Why the call was refused. Never the endpoint, the bucket or the exception the
/// underlying adapter raised — <see cref="ResilientObjectStorage"/> logs those under its own event, not
/// here, so this message is safe to surface as far as a caller chooses to let it travel.</param>
public sealed class ObjectStorageUnavailableException(string message) : Exception(message);
