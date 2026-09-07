using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Tailor360.Modules.Identity.Application.Abstractions;
using Tailor360.Modules.Identity.Application.Sessions;
using Tailor360.Modules.Identity.Application.Timing;
using Tailor360.Modules.Identity.Domain;
using Tailor360.Modules.Identity.Domain.Recovery;
using Tailor360.Modules.Identity.Domain.Sessions;
using Tailor360.Modules.Identity.Domain.Users;
using Tailor360.Platform.Abstractions.Auditing;
using Tailor360.Platform.Abstractions.Identifiers;
using Tailor360.Platform.Abstractions.Ports;
using Tailor360.Platform.Abstractions.Results;
using Tailor360.Platform.Abstractions.Time;

namespace Tailor360.UnitTests.Identity;

/// <summary>
/// The stand-ins the multi-factor and recovery handler tests run against. They are deliberately
/// simple-minded: an in-memory store that does no querying, a protector that is reversible without a
/// key ring, a clock that can be moved. Anything cleverer would start testing the double.
/// </summary>
internal sealed class MovableClock(DateTimeOffset now) : IClock
{
    public DateTimeOffset UtcNow { get; private set; } = now;

    public DateOnly TodayIn(TimeZoneInfo branchTimeZone)
        => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(UtcNow, branchTimeZone).DateTime);

    public void Advance(TimeSpan by) => UtcNow += by;
}

/// <summary>Identifiers that are unique, ordered and reproducible, so a failure names the same value twice.</summary>
internal sealed class CountingIdGenerator : IIdGenerator
{
    private int _issued;

    public Guid NewId()
    {
        _issued++;
        return IdentityTestData.Id($"generated-{_issued}");
    }
}

/// <summary>The accounts and tokens a handler under test can see.</summary>
internal sealed class InMemoryIdentityStore : IIdentityStore
{
    private readonly List<StaffUser> _users = [];
    private readonly List<RecoveryToken> _tokens = [];

    public int SaveCount { get; private set; }

    public IReadOnlyList<RecoveryToken> Tokens => _tokens;

    /// <summary>
    /// Makes the next commit report a lost race, which is what the concurrency token does when a
    /// second request has already spent the credential this one just accepted.
    /// </summary>
    public bool LoseTheNextRace { get; set; }

    public InMemoryIdentityStore With(StaffUser user)
    {
        _users.Add(user);
        return this;
    }

    public Task<StaffUser?> FindUserAsync(Guid userId, CancellationToken cancellationToken = default)
        => Task.FromResult(_users.FirstOrDefault(user => user.Id == userId));

    public Task<StaffUser?> FindUserByNormalisedEmailAsync(
        string normalisedEmail,
        CancellationToken cancellationToken = default)
        => Task.FromResult(_users.FirstOrDefault(
            user => string.Equals(user.NormalisedEmail, normalisedEmail, StringComparison.Ordinal)));

    public void AddRecoveryToken(RecoveryToken token) => _tokens.Add(token);

    public Task<RecoveryToken?> FindRecoveryTokenAsync(
        string tokenHash,
        CancellationToken cancellationToken = default)
        => Task.FromResult(_tokens.FirstOrDefault(token => token.Matches(tokenHash)));

    public Task<int> InvalidateOutstandingRecoveryTokensAsync(
        Guid userId,
        RecoveryPurpose purpose,
        DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        var outstanding = _tokens
            .Where(token => token.UserId == userId && token.Purpose == purpose && token.IsUsable(now))
            .ToList();

        foreach (var token in outstanding)
        {
            token.Invalidate(now);
        }

        return Task.FromResult(outstanding.Count);
    }

    public Task SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        SaveCount++;
        return Task.CompletedTask;
    }

    public Task<Result> TrySaveChangesAsync(CancellationToken cancellationToken = default)
    {
        if (LoseTheNextRace)
        {
            LoseTheNextRace = false;
            return Task.FromResult(Result.Failure(IdentityErrors.ConcurrentChange));
        }

        SaveCount++;
        return Task.FromResult(Result.Success());
    }
}

/// <summary>
/// A protector that wraps by prefixing. It is reversible without a key ring, which is what a unit test
/// needs, and it fails on anything it did not wrap, which is how the unreadable-secret path is reached.
/// </summary>
internal sealed class ReversibleSecretProtector : ISecretProtector
{
    private const string Marker = "wrapped:";

    public string Protect(string plaintext) => Marker + plaintext;

    public Result<string> Unprotect(string protectedValue)
        => protectedValue.StartsWith(Marker, StringComparison.Ordinal)
            ? Result.Success(protectedValue[Marker.Length..])
            : Result.Failure<string>(IdentityErrors.MfaSecretUnreadable);
}

/// <summary>Records which messages were queued, without sending anything.</summary>
internal sealed class RecordingDispatchQueue : IEmailDispatchQueue
{
    private readonly List<EmailMessage> _messages = [];

    public IReadOnlyList<EmailMessage> Messages => _messages;

    public bool Accepting { get; set; } = true;

    public bool Enqueue(EmailMessage message)
    {
        if (!Accepting)
        {
            return false;
        }

        _messages.Add(message);
        return true;
    }
}

/// <summary>
/// Runs the work and records the floor it was asked for, without waiting. A test asserting that both
/// branches ask for the same floor needs no wall clock; the one test that does measure real time uses
/// the shipped implementation instead.
/// </summary>
internal sealed class RecordingUniformResponseTime : IUniformResponseTime
{
    private readonly List<TimeSpan> _floors = [];

    public IReadOnlyList<TimeSpan> Floors => _floors;

    public Task<TResult> RunAsync<TResult>(
        TimeSpan floor,
        Func<CancellationToken, Task<TResult>> work,
        CancellationToken cancellationToken = default)
    {
        _floors.Add(floor);
        return work(cancellationToken);
    }
}

/// <summary>Counts revocations. Only the "sign everyone out" call is exercised here.</summary>
internal sealed class RecordingSessionService : ISessionService
{
    private readonly List<(Guid UserId, SessionEndReason Reason)> _revocations = [];

    public IReadOnlyList<(Guid UserId, SessionEndReason Reason)> Revocations => _revocations;

    public int LiveSessions { get; set; } = 2;

    public Task<Result<IssuedSession>> StartAsync(
        StartSessionRequest request,
        CancellationToken cancellationToken = default)
        => throw new NotSupportedException("Starting a session is not part of these tests.");

    public Task<Result<IssuedSession>> RotateAsync(
        Guid sessionId,
        SessionRotationReason reason,
        CancellationToken cancellationToken = default)
        => throw new NotSupportedException("Rotating a session is not part of these tests.");

    public Task<Result> RevokeAsync(
        Guid sessionId,
        SessionEndReason reason,
        CancellationToken cancellationToken = default)
        => throw new NotSupportedException("Revoking one session is not part of these tests.");

    public Task<Result<int>> RevokeAllForUserAsync(
        Guid userId,
        SessionEndReason reason,
        Guid? exceptSessionId = null,
        CancellationToken cancellationToken = default)
    {
        _revocations.Add((userId, reason));
        return Task.FromResult(Result.Success(LiveSessions));
    }

    public Task<IReadOnlyList<SessionSummary>> ListForUserAsync(
        Guid userId,
        Guid? currentSessionId = null,
        CancellationToken cancellationToken = default)
        => throw new NotSupportedException("Listing sessions is not part of these tests.");
}

/// <summary>Wraps a plain value in the options shape the services take.</summary>
internal static class TestOptions
{
    public static IOptions<TOptions> For<TOptions>(TOptions value)
        where TOptions : class
        => Microsoft.Extensions.Options.Options.Create(value);

    public static NullLogger<T> Logger<T>() => NullLogger<T>.Instance;
}

/// <summary>
/// Collects audit entries instead of writing them, so a unit test can assert that a handler recorded
/// what it claims to record without a database behind it.
/// </summary>
internal sealed class RecordingAuditWriter : IAuditWriter
{
    private readonly List<AuditEntry> _entries = [];

    public IReadOnlyList<AuditEntry> Entries => _entries;

    public int Saves { get; private set; }

    public Task WriteAsync(AuditEntry entry, CancellationToken cancellationToken = default)
    {
        _entries.Add(entry);
        return Task.CompletedTask;
    }

    public Task SaveAsync(CancellationToken cancellationToken = default)
    {
        Saves++;
        return Task.CompletedTask;
    }
}
