using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using OtpNet;
using Shouldly;
using Tailor360.Modules.Identity.Application.Abstractions;
using Tailor360.Modules.Identity.Application.Mfa;
using Tailor360.Modules.Identity.Application.Notifications;
using Tailor360.Modules.Identity.Application.Options;
using Tailor360.Modules.Identity.Application.Passwords;
using Tailor360.Modules.Identity.Application.Recovery;
using Tailor360.Modules.Identity.Application.Sessions;
using Tailor360.Modules.Identity.Application.Timing;
using Tailor360.Modules.Identity.Domain;
using Tailor360.Modules.Identity.Domain.Mfa;
using Tailor360.Modules.Identity.Domain.Recovery;
using Tailor360.Modules.Identity.Domain.Sessions;
using Tailor360.Modules.Identity.Domain.Users;
using Tailor360.Modules.Identity.Infrastructure.Email;
using Tailor360.Modules.Identity.Infrastructure.Persistence;
using Tailor360.Modules.Identity.Infrastructure.Security;
using Tailor360.Platform.Abstractions.Auditing;
using Tailor360.Platform.Abstractions.Identifiers;
using Tailor360.Platform.Abstractions.Ports;
using Tailor360.Platform.Abstractions.Results;

namespace Tailor360.IntegrationTests.Identity;

/// <summary>
/// Multi-factor enrolment and password recovery against a real database.
/// </summary>
/// <remarks>
/// These behaviours cannot be proved in the unit tier, because what they depend on is the mapping
/// rather than the logic: an aggregate whose children are reached through backing fields is mapped
/// correctly or silently loses them at the first reload, a replaced enrolment either respects the
/// unique index on <c>user_id</c> or deadlocks against it, and "single use" is only worth anything if
/// it survives a round trip. Each test therefore reloads through a second context, which is the only
/// way to tell a working mapping from a change tracker that happens to be holding the right answer.
/// </remarks>
[Collection(SessionDatabaseCollection.Name)]
[Trait("Category", "Integration")]
public sealed class RecoveryAndMultiFactorPersistenceTests(SessionDatabaseFixture fixture)
{
    private const string NewPassword = "a-long-enough-new-passphrase";

    private static readonly DateTimeOffset Start = new(2026, 9, 5, 9, 0, 0, TimeSpan.Zero);

    /// <summary>Whether a PostgreSQL instance was found for this run.</summary>
    public static bool Available => SessionDatabaseFixture.IsAvailable;

    [Fact(Skip = DatabaseAvailability.SkipMessage,
        SkipUnless = nameof(Available),
        SkipType = typeof(RecoveryAndMultiFactorPersistenceTests))]
    public async Task AnAbandonedEnrolmentIsReplacedRatherThanBlockedByItsOwnUniqueIndex()
    {
        // Somebody opens the enrolment screen, is interrupted, and comes back to it later. The second
        // secret has to replace the first, and the table allows one enrolment per account — so this is
        // an insert and a delete against a unique index in one save, which either orders correctly or
        // fails at exactly the moment a person is trying to set up their authenticator.
        var harness = await ArrangeAsync(nameof(AnAbandonedEnrolmentIsReplacedRatherThanBlockedByItsOwnUniqueIndex));
        await using var _ = harness;
        var user = await harness.SeedUserAsync();

        var first = (await harness.Enrolment.BeginAsync(Caller(user), Token)).Value;
        var second = (await harness.Enrolment.BeginAsync(Caller(user), Token)).Value;

        second.ManualEntryKey.ShouldNotBe(first.ManualEntryKey);

        await using var reader = SessionDatabaseFixture.CreateContext(harness.ConnectionString);
        var enrolments = await reader.TotpEnrolments.Where(e => e.UserId == user.Id).ToListAsync(Token);

        enrolments.ShouldHaveSingleItem();
        harness.Protector.Unprotect(enrolments[0].ProtectedSecret).Value
            .ShouldBe(SecretOf(second));
    }

    [Fact(Skip = DatabaseAvailability.SkipMessage,
        SkipUnless = nameof(Available),
        SkipType = typeof(RecoveryAndMultiFactorPersistenceTests))]
    public async Task ARecoveryCodeSpentInOneRequestIsStillSpentInTheNext()
    {
        var harness = await ArrangeAsync(nameof(ARecoveryCodeSpentInOneRequestIsStillSpentInTheNext));
        await using var _ = harness;
        var (user, codes, _) = await harness.EnrolAsync();

        (await harness.Challenge.VerifyAsync(user.Id, MfaFactor.RecoveryCode, codes[0], Token))
            .IsSuccess.ShouldBeTrue();

        // A second context, so what is asserted is the row rather than the change tracker.
        await using var reader = SessionDatabaseFixture.CreateContext(harness.ConnectionString);
        var stored = await reader.RecoveryCodes.Where(code => code.UserId == user.Id).ToListAsync(Token);

        stored.Count.ShouldBe(codes.Count);
        stored.Count(code => code.ConsumedAt is not null).ShouldBe(1);

        var replayed = await harness.Challenge.VerifyAsync(user.Id, MfaFactor.RecoveryCode, codes[0], Token);
        replayed.Error.ShouldBe(IdentityErrors.RecoveryCodeInvalid);
    }

    [Fact(Skip = DatabaseAvailability.SkipMessage,
        SkipUnless = nameof(Available),
        SkipType = typeof(RecoveryAndMultiFactorPersistenceTests))]
    public async Task AResetChangesThePasswordAndLeavesTheAuthenticatorAndItsCodesUntouched()
    {
        // The rule of #23 stated against storage: after a reset the row that proves the second factor
        // is the same row it was before, so the sign-in that follows still has to answer it.
        var harness = await ArrangeAsync(nameof(AResetChangesThePasswordAndLeavesTheAuthenticatorAndItsCodesUntouched));
        await using var _ = harness;
        var (user, codes, secret) = await harness.EnrolAsync();

        await using (var before = SessionDatabaseFixture.CreateContext(harness.ConnectionString))
        {
            (await before.TotpEnrolments.CountAsync(Token)).ShouldBe(1);
        }

        (await harness.Recovery.RequestAsync(new RequestPasswordRecovery(user.Email), Token))
            .IsSuccess.ShouldBeTrue();

        var completed = await harness.Recovery.ConfirmAsync(
            new ConfirmPasswordRecovery(harness.QueuedToken(), NewPassword), Token);

        completed.IsSuccess.ShouldBeTrue();
        completed.Value.MultiFactorStillRequired.ShouldBeTrue();

        await using var reader = SessionDatabaseFixture.CreateContext(harness.ConnectionString);
        var reloaded = await reader.Users
            .Include(u => u.Password)
            .Include(u => u.Totp)
            .Include(u => u.RecoveryCodes)
            .SingleAsync(u => u.Id == user.Id, Token);

        // The password is the new one.
        harness.Hashing.Verify(reloaded, reloaded.Password.ShouldNotBeNull().EncodedHash, NewPassword)
            .ShouldBe(PasswordVerification.Succeeded);

        // The second factor is exactly as it was, down to the stored secret and the unspent sheet.
        reloaded.Totp.ShouldNotBeNull().IsConfirmed.ShouldBeTrue();
        reloaded.MfaEnrolment.ShouldBe(MfaEnrolmentState.Enrolled);
        reloaded.UnusedRecoveryCodeCount.ShouldBe(codes.Count);
        harness.Protector.Unprotect(reloaded.Totp.ProtectedSecret).Value.ShouldBe(secret);
    }

    [Fact(Skip = DatabaseAvailability.SkipMessage,
        SkipUnless = nameof(Available),
        SkipType = typeof(RecoveryAndMultiFactorPersistenceTests))]
    public async Task AskingForASecondLinkWithdrawsTheFirstInTheDatabase()
    {
        var harness = await ArrangeAsync(nameof(AskingForASecondLinkWithdrawsTheFirstInTheDatabase));
        await using var _ = harness;
        var user = await harness.SeedUserAsync();

        await harness.Recovery.RequestAsync(new RequestPasswordRecovery(user.Email), Token);
        var first = harness.QueuedToken();

        await harness.Recovery.RequestAsync(new RequestPasswordRecovery(user.Email), Token);

        await using var reader = SessionDatabaseFixture.CreateContext(harness.ConnectionString);
        var tokens = await reader.RecoveryTokens
            .Where(token => token.UserId == user.Id)
            .OrderBy(token => token.CreatedAt)
            .ToListAsync(Token);

        tokens.Count.ShouldBe(2);
        tokens[0].InvalidatedAt.ShouldNotBeNull();
        tokens[1].IsUsable(Start).ShouldBeTrue();

        (await harness.Recovery.ConfirmAsync(new ConfirmPasswordRecovery(first, NewPassword), Token))
            .Error.ShouldBe(IdentityErrors.RecoveryTokenInvalid);
    }

    [Fact(Skip = DatabaseAvailability.SkipMessage,
        SkipUnless = nameof(Available),
        SkipType = typeof(RecoveryAndMultiFactorPersistenceTests))]
    public async Task ARequestForAnAddressThatIsNotAnAccountWritesNothingAndSendsNothing()
    {
        var harness = await ArrangeAsync(nameof(ARequestForAnAddressThatIsNotAnAccountWritesNothingAndSendsNothing));
        await using var _ = harness;
        await harness.SeedUserAsync();

        var answered = await harness.Recovery.RequestAsync(
            new RequestPasswordRecovery("nobody@synthetic.invalid"), Token);

        answered.IsSuccess.ShouldBeTrue();

        await using var reader = SessionDatabaseFixture.CreateContext(harness.ConnectionString);
        (await reader.RecoveryTokens.CountAsync(Token)).ShouldBe(0);
        harness.Queued.ShouldBeEmpty();
    }

    /// <summary>
    /// Two requests answering one challenge with the same authenticator code spend it once.
    /// </summary>
    /// <remarks>
    /// A time-based code is valid for a whole step, so a code seen over someone's shoulder or captured
    /// in front of the sign-in page can be replayed inside that window. The enrolment refuses a step it
    /// has already accepted — but that decision is made against the row as it was read, so two requests
    /// arriving together both read a step that permits the code, both accept it, and without a
    /// concurrency token both write. The one-time password would then have been used twice, which is
    /// the whole of what the second factor is for.
    /// </remarks>
    [Fact(Skip = DatabaseAvailability.SkipMessage,
        SkipUnless = nameof(Available),
        SkipType = typeof(RecoveryAndMultiFactorPersistenceTests))]
    public async Task TwoRequestsAnsweringWithTheSameAuthenticatorCodeSpendItOnce()
    {
        var harness = await ArrangeAsync(nameof(TwoRequestsAnsweringWithTheSameAuthenticatorCodeSpendItOnce));
        await using var _ = harness;
        var (user, _, secret) = await harness.EnrolAsync();

        // A code from the next step, because confirming the enrolment spent the current one.
        harness.Clock.Advance(TimeSpan.FromSeconds(TotpEnrolment.DefaultPeriodSeconds));
        var code = new Totp(Base32Encoding.ToBytes(secret)).ComputeTotp(harness.Clock.UtcNow.UtcDateTime);

        Guid enrolmentId;
        await using (var reader = SessionDatabaseFixture.CreateContext(harness.ConnectionString))
        {
            enrolmentId = (await reader.TotpEnrolments.SingleAsync(e => e.UserId == user.Id, Token)).Id;
        }

        var outcomes = await AnswerConcurrentlyAsync(harness, user.Id, MfaFactor.Totp, code,
            "identity.totp_enrolments", enrolmentId);

        outcomes.Count(outcome => outcome.IsSuccess)
            .ShouldBe(1, "a one-time password is used once however the requests interleave");
        outcomes.Where(outcome => outcome.IsFailure)
            .ShouldAllBe(outcome => outcome.Error == IdentityErrors.MfaCodeInvalid);

        // The row records one acceptance, and the code is refused from here on.
        await using var after = SessionDatabaseFixture.CreateContext(harness.ConnectionString);
        var enrolment = await after.TotpEnrolments.SingleAsync(e => e.UserId == user.Id, Token);
        enrolment.LastAcceptedStep.ShouldNotBeNull();
        enrolment.LastUsedAt.ShouldBe(harness.Clock.UtcNow);

        (await harness.Challenge.VerifyAsync(user.Id, MfaFactor.Totp, code, Token))
            .Error.ShouldBe(IdentityErrors.MfaCodeInvalid);
    }

    /// <summary>
    /// Two requests redeeming the same recovery code spend it once.
    /// </summary>
    /// <remarks>
    /// The same read-modify-write as an authenticator code, and worth more to an attacker: a recovery
    /// code redeemed twice is a whole second factor satisfied twice from one printed line.
    /// </remarks>
    [Fact(Skip = DatabaseAvailability.SkipMessage,
        SkipUnless = nameof(Available),
        SkipType = typeof(RecoveryAndMultiFactorPersistenceTests))]
    public async Task TwoRequestsRedeemingTheSameRecoveryCodeSpendItOnce()
    {
        var harness = await ArrangeAsync(nameof(TwoRequestsRedeemingTheSameRecoveryCodeSpendItOnce));
        await using var _ = harness;
        var (user, codes, _) = await harness.EnrolAsync();

        var digest = new RecoveryCodeService().DigestOf(codes[0]);

        Guid codeId;
        await using (var reader = SessionDatabaseFixture.CreateContext(harness.ConnectionString))
        {
            codeId = (await reader.RecoveryCodes
                .SingleAsync(code => code.UserId == user.Id && code.CodeHash == digest, Token)).Id;
        }

        var outcomes = await AnswerConcurrentlyAsync(harness, user.Id, MfaFactor.RecoveryCode, codes[0],
            "identity.recovery_codes", codeId);

        outcomes.Count(outcome => outcome.IsSuccess)
            .ShouldBe(1, "one printed line satisfies the second factor once");
        outcomes.Where(outcome => outcome.IsFailure)
            .ShouldAllBe(outcome => outcome.Error == IdentityErrors.MfaCodeInvalid);

        // Exactly one code off the sheet is spent — not none, and not two.
        await using var after = SessionDatabaseFixture.CreateContext(harness.ConnectionString);
        var stored = await after.RecoveryCodes.Where(code => code.UserId == user.Id).ToListAsync(Token);
        stored.Count(code => code.ConsumedAt is not null).ShouldBe(1);
    }

    /// <summary>
    /// Answers one challenge from two independent requests at once, with the contested row held until
    /// both of them have read it and reached their write.
    /// </summary>
    private static async Task<IReadOnlyList<Result<MfaChallengeOutcome>>> AnswerConcurrentlyAsync(
        Harness harness,
        Guid userId,
        MfaFactor factor,
        string answer,
        string qualifiedTable,
        Guid rowId)
    {
        // Two contexts, because two requests are two units of work. Sharing the harness's context would
        // share a change tracker, and the second request would see the first one's decision without
        // either of them having written anything.
        await using var firstContext = SessionDatabaseFixture.CreateContext(harness.ConnectionString);
        await using var secondContext = SessionDatabaseFixture.CreateContext(harness.ConnectionString);

        // The row is held before either request starts, or the first could finish before the gate is
        // taken and there would be no race left to observe.
        await using var gate = await RowGate.HoldAsync(
            harness.ConnectionString, qualifiedTable, rowId, Token);

        var requests = new[] { firstContext, secondContext }
            .Select(harness.ChallengeOver)
            .Select(challenge => challenge.VerifyAsync(userId, factor, answer, Token))
            .ToList();

        await gate.ReleaseWhenWaitingAsync(requests.Count, Token);

        return await Task.WhenAll(requests);
    }

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private static string SecretOf(TotpEnrolmentStarted started)
        => started.ManualEntryKey.Replace(" ", string.Empty, StringComparison.Ordinal);

    private async Task<Harness> ArrangeAsync(string name)
    {
        var connectionString = await fixture.CreateDatabaseAsync(name);
        return new Harness(connectionString, new TestClock(Start));
    }

    /// <summary>
    /// The real adapters over a real database, with two stand-ins: a reversible secret protector, so
    /// the test does not need a data-protection key ring, and a session service that counts
    /// revocations rather than performing them.
    /// </summary>
    private sealed class Harness : IAsyncDisposable
    {
        private readonly IdentityDbContext _context;
        private readonly List<EmailMessage> _queued = [];

        public Harness(string connectionString, TestClock clock)
        {
            ConnectionString = connectionString;
            _context = SessionDatabaseFixture.CreateContext(connectionString);
            Clock = clock;

            var store = new IdentityStore(_context);
            var totp = new TotpService();
            var recoveryCodes = new RecoveryCodeService();
            var mfaOptions = Options.Create(new MfaOptions());
            var recoveryOptions = Options.Create(new RecoveryOptions
            {
                PublicBaseUrl = "https://shop.synthetic.invalid",
            });

            Enrolment = new TotpEnrolmentHandler(
                store, totp, recoveryCodes, Protector, new CountingSessionService(), Audit, clock,
                new SequentialIds(), mfaOptions, NullLogger<TotpEnrolmentHandler>.Instance);

            Challenge = new MfaChallengeService(
                store, totp, recoveryCodes, Protector, clock, mfaOptions,
                NullLogger<MfaChallengeService>.Instance);

            Hashing = new PasswordHashingService(
                new Argon2idPasswordHasher(Options.Create(new Argon2idOptions())));

            Recovery = new PasswordRecoveryHandler(
                store,
                new RecoveryTokenService(),
                new PasswordPolicyService(
                    Options.Create(new PasswordPolicyOptions()), new NullBreachedPasswordChecker()),
                Hashing,
                new CountingSessionService(),
                new IdentityMailer(
                    new EmailTemplateRenderer(), new ListQueue(_queued), recoveryOptions,
                    NullLogger<IdentityMailer>.Instance),
                new UniformResponseTime(),
                Audit,
                clock,
                new SequentialIds(),
                recoveryOptions,
                NullLogger<PasswordRecoveryHandler>.Instance);
        }

        public string ConnectionString { get; }

        /// <summary>
        /// Builds a second challenge service over its own context, so that two answers to one challenge
        /// are two units of work rather than two calls sharing a change tracker.
        /// </summary>
        /// <param name="context">The context the new service reads and writes through.</param>
        public MfaChallengeService ChallengeOver(IdentityDbContext context) => new(
            new IdentityStore(context),
            new TotpService(),
            new RecoveryCodeService(),
            Protector,
            Clock,
            Options.Create(new MfaOptions()),
            NullLogger<MfaChallengeService>.Instance);

        public TestClock Clock { get; }

        public ReversibleProtector Protector { get; } = new();

        public RecordingAuditWriter Audit { get; } = new();

        public TotpEnrolmentHandler Enrolment { get; }

        public MfaChallengeService Challenge { get; }

        public PasswordRecoveryHandler Recovery { get; }

        public PasswordHashingService Hashing { get; }

        public IReadOnlyList<EmailMessage> Queued => _queued;

        public Task<StaffUser> SeedUserAsync() =>
            SessionTestData.CreateActiveUserAsync(_context, Clock.UtcNow);

        public async Task<(StaffUser User, IReadOnlyList<string> Codes, string Secret)> EnrolAsync()
        {
            var user = await SeedUserAsync();
            var started = (await Enrolment.BeginAsync(Caller(user), Token)).Value;
            var secret = SecretOf(started);

            var code = new Totp(Base32Encoding.ToBytes(secret), started.PeriodSeconds, OtpHashMode.Sha1, started.Digits)
                .ComputeTotp(Clock.UtcNow.UtcDateTime);

            var confirmed = (await Enrolment.ConfirmAsync(Caller(user), code, Token)).Value;
            return (user, confirmed.RecoveryCodes, secret);
        }

        public string QueuedToken()
        {
            var body = _queued
                .Last(message => message.Tag == IdentityMailer.PasswordRecoveryTemplate)
                .PlainTextBody;

            var marker = body.IndexOf("token=", StringComparison.Ordinal);
            var value = body[(marker + "token=".Length)..];
            var end = value.IndexOfAny([' ', '\r', '\n']);
            return end < 0 ? value : value[..end];
        }

        public ValueTask DisposeAsync() => _context.DisposeAsync();
    }

    /// <summary>
    /// The caller these tests act as: the account's own holder, with no session and no factor yet
    /// satisfied. That is the state somebody enrolling their first authenticator is genuinely in, and
    /// it is the only state in which the handler will enrol one on an account that has none.
    /// </summary>
    private static CallerSession Caller(StaffUser user)
        => new(user.Id, SessionId: null, SecondFactorSatisfied: false);

    private sealed class SequentialIds : IIdGenerator
    {
        public Guid NewId() => Guid.CreateVersion7();
    }

    /// <summary>
    /// Collects audit entries instead of writing them. These tests are about what the identity schema
    /// holds afterwards, and the trail lives in another schema and another context; the entries
    /// themselves are asserted through the API in <c>AuthenticationEndpointTests</c>.
    /// </summary>
    private sealed class RecordingAuditWriter : IAuditWriter
    {
        private readonly List<AuditEntry> _entries = [];

        public IReadOnlyList<AuditEntry> Entries => _entries;

        public Task WriteAsync(AuditEntry entry, CancellationToken cancellationToken = default)
        {
            _entries.Add(entry);
            return Task.CompletedTask;
        }

        public Task SaveAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class ReversibleProtector : ISecretProtector
    {
        private const string Marker = "wrapped:";

        public string Protect(string plaintext) => Marker + plaintext;

        public Result<string> Unprotect(string protectedValue)
            => protectedValue.StartsWith(Marker, StringComparison.Ordinal)
                ? Result.Success(protectedValue[Marker.Length..])
                : Result.Failure<string>(IdentityErrors.MfaSecretUnreadable);
    }

    private sealed class ListQueue(List<EmailMessage> messages) : IEmailDispatchQueue
    {
        public bool Enqueue(EmailMessage message)
        {
            messages.Add(message);
            return true;
        }
    }

    private sealed class CountingSessionService : ISessionService
    {
        public Task<Result<IssuedSession>> StartAsync(
            StartSessionRequest request, CancellationToken cancellationToken = default)
            => throw new NotSupportedException("Starting a session is not part of these tests.");

        public Task<Result<IssuedSession>> RotateAsync(
            Guid sessionId, SessionRotationReason reason, CancellationToken cancellationToken = default)
            => throw new NotSupportedException("Rotating a session is not part of these tests.");

        public Task<Result> RevokeAsync(
            Guid sessionId, SessionEndReason reason, CancellationToken cancellationToken = default)
            => throw new NotSupportedException("Revoking one session is not part of these tests.");

        public Task<Result<int>> RevokeAllForUserAsync(
            Guid userId,
            SessionEndReason reason,
            Guid? exceptSessionId = null,
            CancellationToken cancellationToken = default)
            => Task.FromResult(Result.Success(0));

        public Task<IReadOnlyList<SessionSummary>> ListForUserAsync(
            Guid userId, Guid? currentSessionId = null, CancellationToken cancellationToken = default)
            => throw new NotSupportedException("Listing sessions is not part of these tests.");
    }
}
