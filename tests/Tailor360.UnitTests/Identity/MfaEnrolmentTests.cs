using OtpNet;
using Shouldly;
using Tailor360.Modules.Identity.Application.Mfa;
using Tailor360.Modules.Identity.Application.Options;
using Tailor360.Modules.Identity.Application.Sessions;
using Tailor360.Modules.Identity.Domain;
using Tailor360.Modules.Identity.Domain.Users;
using Tailor360.Modules.Identity.Infrastructure.Security;

namespace Tailor360.UnitTests.Identity;

/// <summary>
/// Enrolling an authenticator, confirming it, and answering the challenge it produces.
/// </summary>
[Trait("Category", "Unit")]
public sealed class MfaEnrolmentTests
{
    /// <summary>The test's own cancellation token, so a hung test is cancelled rather than waited on.</summary>
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private readonly MovableClock _clock = new(IdentityTestData.Now);
    private readonly InMemoryIdentityStore _store = new();
    private readonly ReversibleSecretProtector _protector = new();
    private readonly TotpService _totp = new();
    private readonly RecoveryCodeService _recoveryCodes = new();
    private readonly MfaOptions _options = new();
    private readonly RecordingAuditWriter _audit = new();

    [Fact]
    public async Task EnrolmentOffersTheLinkAndTheTypeableKeyAndStoresNeitherInTheClear()
    {
        var user = IdentityTestData.Active();
        _store.With(user);

        var started = (await Handler().BeginAsync(Enrolling(user), Token)).Value;

        // Both forms, because somebody enrolling on the phone that holds the authenticator cannot scan
        // their own screen.
        started.OtpAuthUri.ShouldStartWith("otpauth://totp/");
        started.ManualEntryKey.ShouldContain(" ");
        started.Issuer.ShouldBe(_options.Issuer);
        started.AccountName.ShouldBe(user.UserName);

        // The stored secret went through the protector rather than into the row as it stands. What
        // that protection is worth is the protector's business — in production it is data protection,
        // here it is a reversible stand-in — but the handler must not be the one to skip it.
        var stored = user.Totp.ShouldNotBeNull().ProtectedSecret;
        stored.ShouldNotBe(SecretOf(started));
        stored.ShouldBe(_protector.Protect(SecretOf(started)));
        _protector.Unprotect(stored).Value.ShouldBe(SecretOf(started));
        user.MfaEnrolment.ShouldBe(MfaEnrolmentState.PendingConfirmation);
        user.HasConfirmedSecondFactor.ShouldBeFalse();
    }

    [Fact]
    public async Task AnEnrolmentBecomesAFactorOnlyWhenACodeConfirmsItAndOnlyThenAreCodesIssued()
    {
        var user = IdentityTestData.Active();
        _store.With(user);
        var handler = Handler();

        var started = (await handler.BeginAsync(Enrolling(user), Token)).Value;

        // A wrong code changes nothing: a secret that has never produced a working code is not a second
        // factor, it is a way of locking somebody out of their own account.
        var wrong = await handler.ConfirmAsync(Enrolling(user), "000000", Token);
        wrong.Error.ShouldBe(IdentityErrors.MfaCodeInvalid);
        user.HasConfirmedSecondFactor.ShouldBeFalse();
        user.UnusedRecoveryCodeCount.ShouldBe(0);

        var confirmed = await handler.ConfirmAsync(Enrolling(user), CodeFor(started), Token);

        confirmed.IsSuccess.ShouldBeTrue();
        confirmed.Value.RecoveryCodes.Count.ShouldBe(_options.RecoveryCodeCount);
        user.MfaEnrolment.ShouldBe(MfaEnrolmentState.Enrolled);
        user.HasConfirmedSecondFactor.ShouldBeTrue();
        user.UnusedRecoveryCodeCount.ShouldBe(_options.RecoveryCodeCount);

        // The codes exist in a readable form exactly once, here, and only as digests afterwards.
        user.RecoveryCodes.ShouldAllBe(code => HashedSecret.IsWellFormed(code.CodeHash));
    }

    [Fact]
    public async Task TheConfirmingCodeCannotBeTurnedRoundIntoASignIn()
    {
        // A time-based code is valid for a whole step, so the code that proved the authenticator works
        // would otherwise still be valid as an answer to the challenge that follows it.
        var user = IdentityTestData.Active();
        _store.With(user);
        var handler = Handler();

        var started = (await handler.BeginAsync(Enrolling(user), Token)).Value;
        var code = CodeFor(started);
        (await handler.ConfirmAsync(Enrolling(user), code, Token)).IsSuccess.ShouldBeTrue();

        var replayed = await Challenge().VerifyAsync(user.Id, MfaFactor.Totp, code, Token);

        replayed.Error.ShouldBe(IdentityErrors.MfaCodeInvalid);
    }

    [Fact]
    public async Task ACodeFromTheNextStepIsAccepted()
    {
        var user = IdentityTestData.Active();
        _store.With(user);
        var handler = Handler();
        var started = (await handler.BeginAsync(Enrolling(user), Token)).Value;
        await handler.ConfirmAsync(Enrolling(user), CodeFor(started), Token);

        _clock.Advance(TimeSpan.FromSeconds(_options.PeriodSeconds));

        var answered = await Challenge().VerifyAsync(user.Id, MfaFactor.Totp, CodeFor(started), Token);

        answered.IsSuccess.ShouldBeTrue();
        answered.Value.Factor.ShouldBe(MfaFactor.Totp);
    }

    [Fact]
    public async Task ARecoveryCodeWorksOnceAndThenNeverAgain()
    {
        var (user, codes) = await EnrolledWithCodes();
        var challenge = Challenge();

        var first = await challenge.VerifyAsync(user.Id, MfaFactor.RecoveryCode, codes[0], Token);
        first.IsSuccess.ShouldBeTrue();
        first.Value.RemainingRecoveryCodes.ShouldBe(codes.Count - 1);

        var replay = await challenge.VerifyAsync(user.Id, MfaFactor.RecoveryCode, codes[0], Token);
        replay.Error.ShouldBe(IdentityErrors.RecoveryCodeInvalid);

        // An unknown code and a spent one fail identically, so nothing is learned from the difference.
        (await challenge.VerifyAsync(user.Id, MfaFactor.RecoveryCode, "ABCDE-FGHJK", Token)).Error
            .ShouldBe(IdentityErrors.RecoveryCodeInvalid);
    }

    /// <summary>
    /// An answer that loses the race to write is refused exactly as a wrong one is.
    /// </summary>
    /// <remarks>
    /// The database settles which of two requests carrying the same code actually spent it — the
    /// enrolment and the recovery code both carry a concurrency token for that reason. What is asserted
    /// here is the half of the fix above the store: the loser is told the code is not valid, in the
    /// same words a wrong code gets, rather than being handed a server error or, worse, a success.
    /// </remarks>
    [Fact]
    public async Task AnAnswerThatLosesTheRaceToWriteIsRefusedLikeAWrongOne()
    {
        var user = IdentityTestData.Active();
        _store.With(user);
        var handler = Handler();
        var started = (await handler.BeginAsync(Enrolling(user), Token)).Value;
        var codes = (await handler.ConfirmAsync(Enrolling(user), CodeFor(started), Token)).Value.RecoveryCodes;

        var challenge = Challenge();

        _store.LoseTheNextRace = true;
        (await challenge.VerifyAsync(user.Id, MfaFactor.RecoveryCode, codes[0], Token)).Error
            .ShouldBe(IdentityErrors.MfaCodeInvalid);

        _clock.Advance(TimeSpan.FromSeconds(_options.PeriodSeconds));

        _store.LoseTheNextRace = true;
        (await challenge.VerifyAsync(user.Id, MfaFactor.Totp, CodeFor(started), Token)).Error
            .ShouldBe(IdentityErrors.MfaCodeInvalid);
    }

    /// <summary>
    /// A confirmation that loses the race to write is refused rather than reported as confirmed.
    /// </summary>
    /// <remarks>
    /// Confirmation spends the code that proved the authenticator and issues the recovery sheet in one
    /// write, so a second confirmation arriving with the first would otherwise print a second sheet and
    /// leave the holder unable to tell which one works. Which of the two actually wrote is settled by
    /// the concurrency token in the database; what is asserted here is that the loser is told so, in
    /// the same words a wrong code gets, instead of being handed a sheet that was never stored.
    /// </remarks>
    [Fact]
    public async Task ConfirmingAnEnrolmentThatLostTheRaceIsRefusedRatherThanConfirmed()
    {
        var user = IdentityTestData.Active();
        _store.With(user);
        var handler = Handler();
        var started = (await handler.BeginAsync(Enrolling(user), Token)).Value;

        _store.LoseTheNextRace = true;
        var confirmed = await handler.ConfirmAsync(Enrolling(user), CodeFor(started), Token);

        confirmed.Error.ShouldBe(IdentityErrors.MfaCodeInvalid);
    }

    [Fact]
    public async Task RunningLowOnRecoveryCodesIsReportedBeforeTheyRunOut()
    {
        // Somebody who reaches zero without noticing has no way back in the day they lose their phone.
        var (user, codes) = await EnrolledWithCodes();
        var challenge = Challenge();

        var spend = codes.Count - _options.RecoveryCodeReissueThreshold;
        for (var index = 0; index < spend - 1; index++)
        {
            (await challenge.VerifyAsync(user.Id, MfaFactor.RecoveryCode, codes[index], Token))
                .Value.ShouldReissueRecoveryCodes.ShouldBeFalse();
        }

        var last = await challenge.VerifyAsync(user.Id, MfaFactor.RecoveryCode, codes[spend - 1], Token);

        last.Value.RemainingRecoveryCodes.ShouldBe(_options.RecoveryCodeReissueThreshold);
        last.Value.ShouldReissueRecoveryCodes.ShouldBeTrue();
    }

    [Fact]
    public async Task PrintingAFreshSheetStopsTheOldOneWorking()
    {
        // A sheet somebody has photographed stops working the moment a new one is printed. That is the
        // point of reissue rather than a side effect of it.
        var (user, codes) = await EnrolledWithCodes();

        var reissued = (await Handler().ReissueRecoveryCodesAsync(Verified(user), Token)).Value;

        reissued.RecoveryCodes.ShouldNotBe(codes);
        user.UnusedRecoveryCodeCount.ShouldBe(_options.RecoveryCodeCount);
        (await Challenge().VerifyAsync(user.Id, MfaFactor.RecoveryCode, codes[0], Token)).Error
            .ShouldBe(IdentityErrors.RecoveryCodeInvalid);
    }

    [Fact]
    public async Task AnAuthenticatorCannotBeSwappedOnceItIsConfirmed()
    {
        // Otherwise somebody who has taken over a live session could quietly replace the second factor
        // with their own and keep the account after the password was changed back. Two refusals, in
        // order: a caller who has not answered the account's existing factor is turned away before the
        // account is even consulted, and one who has is still told the authenticator is already there.
        var (user, _) = await EnrolledWithCodes();

        (await Handler().BeginAsync(Enrolling(user), Token)).Error
            .ShouldBe(IdentityErrors.SecondFactorNotSatisfied);
        (await Handler().BeginAsync(Verified(user), Token)).Error
            .ShouldBe(IdentityErrors.MfaAlreadyEnrolled);
    }

    [Fact]
    public async Task APasswordOnlySessionCannotPrintAFreshSheetOfRecoveryCodes()
    {
        // The sheet is a working set of second factors, handed back in the response body. A caller who
        // has proved only a password could otherwise print themselves one, answer the challenge with
        // it, and destroy the holder's own sheet on the way past.
        var (user, codes) = await EnrolledWithCodes();

        (await Handler().ReissueRecoveryCodesAsync(Enrolling(user), Token)).Error
            .ShouldBe(IdentityErrors.SecondFactorNotSatisfied);

        // The holder's own sheet still works, which is the half of the attack that locks them out.
        (await Challenge().VerifyAsync(user.Id, MfaFactor.RecoveryCode, codes[0], Token))
            .IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public async Task EnrolmentAndReissueAreBothRecordedInTheAuditTrail()
    {
        // ".Audited(...)" only declares the action name; what writes the row is the handler. These
        // three actions were declared and never written, so an attacker printing themselves a sheet of
        // recovery codes left nothing behind.
        var (user, _) = await EnrolledWithCodes();
        (await Handler().ReissueRecoveryCodesAsync(Verified(user), Token)).IsSuccess.ShouldBeTrue();

        _audit.Entries.Select(entry => entry.Action).ShouldBe(
            [
                TotpEnrolmentHandler.EnrolmentStartedAction,
                TotpEnrolmentHandler.EnrolmentConfirmedAction,
                TotpEnrolmentHandler.RecoveryCodesIssuedAction,
            ],
            ignoreOrder: false);

        // Attributed to the account itself rather than to "system", so an actor query finds them.
        _audit.Entries.ShouldAllBe(entry => entry.ActorId == user.Id);
        _audit.Saves.ShouldBe(_audit.Entries.Count);
    }

    [Fact]
    public async Task ASecretThatCannotBeUnwrappedIsReportedAsAServerProblemNotAWrongCode()
    {
        // The realistic cause is a data-protection key ring that was never persisted. The holder cannot
        // fix that by typing a different code, so they must not be told they typed the wrong one.
        var user = IdentityTestData.Enrolled();
        _store.With(user);

        var answered = await Challenge().VerifyAsync(user.Id, MfaFactor.Totp, "123456", Token);

        answered.Error.ShouldBe(IdentityErrors.MfaSecretUnreadable);
    }

    [Fact]
    public async Task AnAccountThatCannotAuthenticateCannotEnrol()
    {
        var user = IdentityTestData.Active();
        user.Suspend(IdentityTestData.Now, null);
        _store.With(user);

        (await Handler().BeginAsync(Enrolling(user), Token)).Error.ShouldBe(IdentityErrors.UserSuspended);
        (await Handler().BeginAsync(new CallerSession(IdentityTestData.Id("nobody"), null, false), Token)).Error
            .ShouldBe(IdentityErrors.UserNotFound);
    }

    private TotpEnrolmentHandler Handler()
        => new(
            _store,
            _totp,
            _recoveryCodes,
            _protector,
            new RecordingSessionService(),
            _audit,
            _clock,
            new CountingIdGenerator(),
            TestOptions.For(_options),
            TestOptions.Logger<TotpEnrolmentHandler>());

    /// <summary>
    /// The caller enrolling their first factor: their own account, no session, nothing yet proved. It
    /// is the only state in which the handler will enrol a factor on an account that has none.
    /// </summary>
    private static CallerSession Enrolling(StaffUser user)
        => new(user.Id, SessionId: null, SecondFactorSatisfied: false);

    /// <summary>The same caller after answering a challenge, which is what changing a factor needs.</summary>
    private static CallerSession Verified(StaffUser user)
        => new(user.Id, SessionId: null, SecondFactorSatisfied: true);

    private MfaChallengeService Challenge()
        => new(
            _store,
            _totp,
            _recoveryCodes,
            _protector,
            _clock,
            TestOptions.For(_options),
            TestOptions.Logger<MfaChallengeService>());

    private async Task<(StaffUser User, IReadOnlyList<string> Codes)> EnrolledWithCodes()
    {
        var user = IdentityTestData.Active();
        _store.With(user);
        var handler = Handler();

        var started = (await handler.BeginAsync(Enrolling(user), Token)).Value;
        var confirmed = (await handler.ConfirmAsync(Enrolling(user), CodeFor(started), Token)).Value;

        // Move past the confirming step so a later challenge is not refused as a replay of it.
        _clock.Advance(TimeSpan.FromSeconds(_options.PeriodSeconds));

        return (user, confirmed.RecoveryCodes);
    }

    private static string SecretOf(TotpEnrolmentStarted started)
        => started.ManualEntryKey.Replace(" ", string.Empty, StringComparison.Ordinal);

    private string CodeFor(TotpEnrolmentStarted started)
        => new Totp(Base32Encoding.ToBytes(SecretOf(started)), started.PeriodSeconds, OtpHashMode.Sha1, started.Digits)
            .ComputeTotp(_clock.UtcNow.UtcDateTime);
}
