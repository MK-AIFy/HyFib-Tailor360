using System.Diagnostics;
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
using Tailor360.Modules.Identity.Domain.Sessions;
using Tailor360.Modules.Identity.Domain.Users;
using Tailor360.Modules.Identity.Infrastructure.Email;
using Tailor360.Modules.Identity.Infrastructure.Security;
using Tailor360.Platform.Abstractions.Ports;

namespace Tailor360.UnitTests.Identity;

/// <summary>
/// Email-verified password recovery, and the two rules that make it safe: it never touches the second
/// factor, and it tells the caller nothing about whether the address is one this system knows.
/// </summary>
[Trait("Category", "Unit")]
public sealed class PasswordRecoveryTests
{
    /// <summary>The test's own cancellation token, so a hung test is cancelled rather than waited on.</summary>
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private const string NewPassword = "a-long-enough-new-passphrase";

    private readonly MovableClock _clock = new(IdentityTestData.Now);
    private readonly InMemoryIdentityStore _store = new();
    private readonly RecordingDispatchQueue _queue = new();
    private readonly RecordingSessionService _sessions = new();
    private readonly RecordingAuditWriter _audit = new();
    private readonly RecordingUniformResponseTime _timing = new();
    private readonly ReversibleSecretProtector _protector = new();
    private readonly RecoveryTokenService _tokens = new();
    private readonly TotpService _totp = new();
    private readonly RecoveryCodeService _recoveryCodes = new();
    private readonly MfaOptions _mfa = new();

    private readonly RecoveryOptions _options = new()
    {
        PublicBaseUrl = "https://shop.example",
        TokenLifetime = TimeSpan.FromMinutes(30),
        UniformResponseTime = TimeSpan.FromMilliseconds(150),
    };

    /* Anti-enumeration ------------------------------------------------------------------------- */

    [Fact]
    public async Task AKnownAndAnUnknownAddressAreAnsweredIdentically()
    {
        var user = ActiveUser();
        var handler = Handler();

        var known = await handler.RequestAsync(new RequestPasswordRecovery(user.Email), Token);
        var unknown = await handler.RequestAsync(new RequestPasswordRecovery("nobody@example.test"), Token);

        known.IsSuccess.ShouldBeTrue();
        unknown.IsSuccess.ShouldBeTrue();

        // The same object, carrying nothing. A response that differed by so much as a field name would
        // be an account-enumeration oracle, and so would a different message on the screen.
        known.Value.ShouldBe(unknown.Value);

        // Both asked for the same floor, so the two cannot be told apart on the clock either.
        _timing.Floors.ShouldBe([_options.UniformResponseTime, _options.UniformResponseTime]);

        // Exactly one message, for the address that exists.
        _queue.Messages.Count.ShouldBe(1);
        _queue.Messages[0].To.ShouldBe(user.Email);
    }

    [Theory]
    [InlineData(UserStatus.Invited)]
    [InlineData(UserStatus.Suspended)]
    [InlineData(UserStatus.Deactivated)]
    public async Task AnAccountThatIsNotActiveIsAnsweredLikeAnAddressThatDoesNotExist(UserStatus status)
    {
        var user = ActiveUser();
        switch (status)
        {
            case UserStatus.Invited:
                user = IdentityTestData.Invited("invited.user");
                _store.With(user);
                break;
            case UserStatus.Suspended:
                user.Suspend(_clock.UtcNow, null);
                break;
            default:
                user.Deactivate(_clock.UtcNow, null);
                break;
        }

        var answered = await Handler().RequestAsync(new RequestPasswordRecovery(user.Email), Token);

        answered.IsSuccess.ShouldBeTrue();
        _queue.Messages.ShouldBeEmpty();
        _store.Tokens.ShouldBeEmpty();
    }

    [Fact]
    public async Task TheRequestTakesTheSameTimeWhicheverBranchItRuns()
    {
        // The wording being identical is not enough on its own: the honest implementation is fast when
        // the address is unknown and slower when it is known, and an attacker with a stopwatch reads
        // that difference straight off. The floor is what closes it, and it works only because the
        // message is queued rather than sent while the caller waits.
        var user = ActiveUser();
        var floor = _options.UniformResponseTime;
        var handler = Handler(new UniformResponseTime());

        // The statistic compared is the fastest of several samples, not one measurement and not a mean.
        // The floor is a lower bound, so a sample is "floor plus the work plus whatever else this
        // machine was doing"; a busy test host only ever adds. The fastest sample is therefore the one
        // closest to floor-plus-work, which is precisely the quantity an attacker would converge on by
        // sending many requests — and it is the one statistic that does not turn a loaded build agent
        // into a failing test.
        await handler.RequestAsync(new RequestPasswordRecovery(user.Email), Token);
        await handler.RequestAsync(new RequestPasswordRecovery("nobody@example.test"), Token);

        var known = TimeSpan.MaxValue;
        var unknown = TimeSpan.MaxValue;

        for (var sample = 0; sample < 5; sample++)
        {
            var withAccount = await MeasureAsync(
                () => handler.RequestAsync(new RequestPasswordRecovery(user.Email), Token));
            var withoutAccount = await MeasureAsync(
                () => handler.RequestAsync(new RequestPasswordRecovery("nobody@example.test"), Token));

            known = known < withAccount ? known : withAccount;
            unknown = unknown < withoutAccount ? unknown : withoutAccount;
        }

        // Neither path can answer sooner than the floor, which is what stops the fast path being fast.
        known.ShouldBeGreaterThanOrEqualTo(floor);
        unknown.ShouldBeGreaterThanOrEqualTo(floor);

        // And what is left over the floor — the work itself — is far too small to read the answer from.
        (known - unknown).Duration().ShouldBeLessThan(floor / 2);
    }

    /* The rule that matters: recovery never touches the second factor -------------------------- */

    [Fact]
    public async Task ResettingAPasswordLeavesTheSecondFactorExactlyWhereItWas()
    {
        // A person who has just proved they can read an inbox has proved exactly that. If recovery
        // cleared the second factor, control of one mailbox would become full control of an account —
        // which is the attack multi-factor exists to stop. Losing the factor itself is a different
        // journey: an administrator resets it under step-up, with a reason and out-of-band checks.
        var (user, code) = await EnrolledUserAsync();
        var codesBefore = user.UnusedRecoveryCodeCount;
        var handler = Handler();

        await handler.RequestAsync(new RequestPasswordRecovery(user.Email), Token);
        var completed = await handler.ConfirmAsync(new ConfirmPasswordRecovery(QueuedToken(), NewPassword), Token);

        completed.IsSuccess.ShouldBeTrue();

        // The authenticator, its enrolment state and the printed sheet are all untouched.
        user.Totp.ShouldNotBeNull().IsConfirmed.ShouldBeTrue();
        user.MfaEnrolment.ShouldBe(MfaEnrolmentState.Enrolled);
        user.HasConfirmedSecondFactor.ShouldBeTrue();
        user.UnusedRecoveryCodeCount.ShouldBe(codesBefore);

        // And the caller is told so, because a person who resets a password and is then asked for a
        // code assumes something has gone wrong unless they were told it would happen.
        completed.Value.MultiFactorStillRequired.ShouldBeTrue();
        QueuedBodies().ShouldContain(body => body.Contains("still be asked for your authenticator code", StringComparison.Ordinal));
    }

    [Fact]
    public async Task TheSignInAfterAResetStillHasToAnswerTheAuthenticator()
    {
        // The same rule stated as the behaviour it produces: the authenticator enrolled before the
        // reset is the one that answers the challenge after it, and it still has to be answered.
        var (user, secret) = await EnrolledUserAsync();
        var handler = Handler();

        await handler.RequestAsync(new RequestPasswordRecovery(user.Email), Token);
        (await handler.ConfirmAsync(new ConfirmPasswordRecovery(QueuedToken(), NewPassword), Token))
            .IsSuccess.ShouldBeTrue();

        _clock.Advance(TimeSpan.FromSeconds(_mfa.PeriodSeconds * 2));
        var challenge = Challenge();

        (await challenge.VerifyAsync(user.Id, MfaFactor.Totp, "000000", Token)).Error
            .ShouldBe(IdentityErrors.MfaCodeInvalid);
        (await challenge.VerifyAsync(user.Id, MfaFactor.Totp, CodeFrom(secret), Token)).IsSuccess.ShouldBeTrue();
    }

    /* Completing a reset ------------------------------------------------------------------------ */

    [Fact]
    public async Task CompletingAResetSetsThePasswordEndsEverySessionAndSaysSo()
    {
        var user = ActiveUser();
        var handler = Handler();
        var hashing = Hashing();

        await handler.RequestAsync(new RequestPasswordRecovery(user.Email), Token);
        var completed = await handler.ConfirmAsync(new ConfirmPasswordRecovery(QueuedToken(), NewPassword), Token);

        completed.Value.UserId.ShouldBe(user.Id);
        completed.Value.SessionsRevoked.ShouldBe(_sessions.LiveSessions);
        _sessions.Revocations.ShouldBe([(user.Id, SessionEndReason.PasswordChanged)]);

        hashing.Verify(user, user.Password.ShouldNotBeNull().EncodedHash, NewPassword)
            .ShouldBe(PasswordVerification.Succeeded);

        // The holder is told, because this message is what turns a silent takeover into one they find
        // out about.
        QueuedTags().ShouldContain(IdentityMailer.PasswordChangedTemplate);
    }

    [Fact]
    public async Task ALinkWorksOnceAndThenStopsWorking()
    {
        var user = ActiveUser();
        var handler = Handler();

        await handler.RequestAsync(new RequestPasswordRecovery(user.Email), Token);
        var token = QueuedToken();

        (await handler.ConfirmAsync(new ConfirmPasswordRecovery(token, NewPassword), Token)).IsSuccess.ShouldBeTrue();
        (await handler.ConfirmAsync(new ConfirmPasswordRecovery(token, "another-long-passphrase"), Token)).Error
            .ShouldBe(IdentityErrors.RecoveryTokenInvalid);
    }

    [Fact]
    public async Task AskingAgainWithdrawsTheLinkThatWasSentFirst()
    {
        // Two "forgot my password" clicks must not leave two working links in one inbox.
        var user = ActiveUser();
        var handler = Handler();

        await handler.RequestAsync(new RequestPasswordRecovery(user.Email), Token);
        var first = QueuedToken();

        await handler.RequestAsync(new RequestPasswordRecovery(user.Email), Token);
        var second = QueuedToken();

        first.ShouldNotBe(second);
        (await handler.ConfirmAsync(new ConfirmPasswordRecovery(first, NewPassword), Token)).Error
            .ShouldBe(IdentityErrors.RecoveryTokenInvalid);
        (await handler.ConfirmAsync(new ConfirmPasswordRecovery(second, NewPassword), Token)).IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public async Task AnExpiredLinkIsRefused()
    {
        var user = ActiveUser();
        var handler = Handler();

        await handler.RequestAsync(new RequestPasswordRecovery(user.Email), Token);
        var token = QueuedToken();

        _clock.Advance(_options.TokenLifetime + TimeSpan.FromSeconds(1));

        (await handler.ConfirmAsync(new ConfirmPasswordRecovery(token, NewPassword), Token)).Error
            .ShouldBe(IdentityErrors.RecoveryTokenInvalid);
    }

    [Fact]
    public async Task APasswordThePolicyRejectsCostsARetypeRatherThanTheWholeLink()
    {
        // Spending the link on a rejected password would send the person back to their inbox for a
        // second one, which is both infuriating and a reason to choose something memorable and weak.
        var user = ActiveUser();
        var handler = Handler();

        await handler.RequestAsync(new RequestPasswordRecovery(user.Email), Token);
        var token = QueuedToken();

        var rejected = await handler.ConfirmAsync(new ConfirmPasswordRecovery(token, "short"), Token);
        rejected.Error.Code.ShouldBe("identity.password-too-short");

        (await handler.ConfirmAsync(new ConfirmPasswordRecovery(token, NewPassword), Token)).IsSuccess.ShouldBeTrue();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not-a-token")]
    public async Task AValueThatIsNotALinkIsRefusedTheSameWayAnUnknownOneIs(string? token)
        => (await Handler().ConfirmAsync(new ConfirmPasswordRecovery(token, NewPassword), Token)).Error
            .ShouldBe(IdentityErrors.RecoveryTokenInvalid);

    /* What the message may contain ------------------------------------------------------------- */

    [Fact]
    public async Task TheMessageCarriesAnAbsoluteLinkAndNothingElseOfValue()
    {
        var user = ActiveUser();

        await Handler().RequestAsync(new RequestPasswordRecovery(user.Email), Token);

        var body = _queue.Messages.ShouldHaveSingleItem().PlainTextBody;
        body.ShouldContain("https://shop.example/recovery/confirm?token=");
        body.ShouldContain("30 minutes");

        // Nothing that is stored as a digest may travel in a message.
        body.ShouldNotContain(user.Password.ShouldNotBeNull().EncodedHash);
        body.ShouldNotContain("password:", Case.Insensitive);
    }

    [Fact]
    public async Task ARelayThatIsNotAcceptingMessagesDoesNotChangeTheAnswerTheCallerGets()
    {
        // Surfacing a mail failure for one address and not another would be exactly the difference
        // this endpoint exists to hide.
        var user = ActiveUser();
        _queue.Accepting = false;

        var answered = await Handler().RequestAsync(new RequestPasswordRecovery(user.Email), Token);

        answered.IsSuccess.ShouldBeTrue();
        _store.Tokens.ShouldHaveSingleItem();
    }

    [Fact]
    public async Task AskingForALinkAndCompletingAResetAreBothWrittenToTheAuditTrail()
    {
        // Both endpoints declared these actions and neither wrote them, so a password reset — the one
        // operation that changes a credential without anybody being signed in — left no trace at all.
        // The entries name the account rather than deferring to the request context, in which nobody
        // is authenticated: completing a reset is what proves anything, so there is no caller to defer
        // to and an actor query would otherwise never find it.
        var user = ActiveUser();
        var handler = Handler();

        (await handler.RequestAsync(new RequestPasswordRecovery(user.Email), Token))
            .IsSuccess.ShouldBeTrue();

        (await handler.ConfirmAsync(new ConfirmPasswordRecovery(QueuedToken(), NewPassword), Token))
            .IsSuccess.ShouldBeTrue();

        _audit.Entries.Select(entry => entry.Action).ShouldBe(
            [PasswordRecoveryHandler.RequestedAction, PasswordRecoveryHandler.CompletedAction],
            ignoreOrder: false);

        _audit.Entries.ShouldAllBe(entry => entry.ActorId == user.Id);

        // And nothing readable about the credential reaches the entry.
        foreach (var entry in _audit.Entries)
        {
            entry.Summary.ShouldNotContain(NewPassword, Case.Sensitive);
        }
    }

    /* Configuration ---------------------------------------------------------------------------- */

    [Theory]
    [InlineData("https://shop.example", false, true)]
    [InlineData("https://shop.example/", false, true)]
    [InlineData("", false, false)]
    [InlineData("   ", false, false)]
    [InlineData("", true, false)]
    [InlineData("http://shop.example", false, false)]
    [InlineData("http://127.0.0.1:5173", false, false)]
    [InlineData("http://127.0.0.1:5173", true, true)]
    [InlineData("/recovery", false, false)]
    [InlineData("shop.example", false, false)]
    [InlineData("ftp://shop.example", false, false)]
    [InlineData("ftp://shop.example", true, false)]
    public void ThePublicOriginMustBeAnAbsoluteHttpsAddress(
        string publicBaseUrl,
        bool allowInsecure,
        bool usable)
    {
        // What travels through this address is a token that sets a password. The shape an operator
        // copies when configuring staging is whatever development was left holding, so plain HTTP has
        // to be refused at start-up rather than accepted quietly and discovered by an interceptor.
        //
        // An unset value is refused too, and the insecure opt-out does not rescue it. It used to pass,
        // so that a host with nothing to do with recovery would not be stopped by a setting it never
        // reads — but the command-line tool builds its host without starting it, so the validation
        // never ran there anyway, and the only host the carve-out actually reached was the web host,
        // which is the one that sends the mail. What it bought was a deployment that started cleanly
        // and then e-mailed "/recovery/confirm?token=…", a path with no origin, to somebody who could
        // not get in.
        var options = new RecoveryOptions
        {
            PublicBaseUrl = publicBaseUrl,
            AllowInsecurePublicBaseUrl = allowInsecure,
        };

        options.IsPublicBaseUrlUsable.ShouldBe(usable);
    }

    /* Construction ----------------------------------------------------------------------------- */

    private StaffUser ActiveUser(string userName = "recovery.user")
    {
        var user = IdentityTestData.Active(userName);
        _store.With(user);
        return user;
    }

    private async Task<(StaffUser User, string Secret)> EnrolledUserAsync()
    {
        var user = ActiveUser();
        var enrolment = new TotpEnrolmentHandler(
            _store, _totp, _recoveryCodes, _protector, _sessions, _audit, _clock,
            new CountingIdGenerator(), TestOptions.For(_mfa),
            TestOptions.Logger<TotpEnrolmentHandler>());

        var caller = new CallerSession(user.Id, SessionId: null, SecondFactorSatisfied: false);

        var started = (await enrolment.BeginAsync(caller, Token)).Value;
        var secret = started.ManualEntryKey.Replace(" ", string.Empty, StringComparison.Ordinal);
        (await enrolment.ConfirmAsync(caller, CodeFrom(secret), Token)).IsSuccess.ShouldBeTrue();

        return (user, secret);
    }

    private PasswordRecoveryHandler Handler(IUniformResponseTime? timing = null)
        => new(
            _store,
            _tokens,
            new PasswordPolicyService(
                TestOptions.For(new PasswordPolicyOptions()), new NullBreachedPasswordChecker()),
            Hashing(),
            _sessions,
            new IdentityMailer(
                new EmailTemplateRenderer(),
                _queue,
                TestOptions.For(_options),
                TestOptions.Logger<IdentityMailer>()),
            timing ?? _timing,
            _audit,
            _clock,
            new CountingIdGenerator(),
            TestOptions.For(_options),
            TestOptions.Logger<PasswordRecoveryHandler>());

    private MfaChallengeService Challenge()
        => new(
            _store, _totp, _recoveryCodes, _protector, _clock,
            TestOptions.For(_mfa), TestOptions.Logger<MfaChallengeService>());

    private static PasswordHashingService Hashing()
        => new(new Argon2idPasswordHasher(TestOptions.For(new Argon2idOptions())));

    private string CodeFrom(string secretBase32)
        => new Totp(Base32Encoding.ToBytes(secretBase32), _mfa.PeriodSeconds, OtpHashMode.Sha1, _mfa.Digits)
            .ComputeTotp(_clock.UtcNow.UtcDateTime);

    /// <summary>Reads the token out of the most recent recovery message, as a person clicking would.</summary>
    private string QueuedToken()
    {
        var body = _queue.Messages
            .Last(message => message.Tag == IdentityMailer.PasswordRecoveryTemplate)
            .PlainTextBody;

        var marker = body.IndexOf("token=", StringComparison.Ordinal);
        marker.ShouldBeGreaterThan(-1);

        var value = body[(marker + "token=".Length)..];
        var end = value.IndexOfAny([' ', '\r', '\n']);
        return end < 0 ? value : value[..end];
    }

    private IEnumerable<string> QueuedBodies() => _queue.Messages.Select(message => message.PlainTextBody);

    private IEnumerable<string?> QueuedTags() => _queue.Messages.Select(message => message.Tag);

    private static async Task<TimeSpan> MeasureAsync<TResult>(Func<Task<TResult>> work)
    {
        var started = Stopwatch.GetTimestamp();
        await work();
        return Stopwatch.GetElapsedTime(started);
    }
}
