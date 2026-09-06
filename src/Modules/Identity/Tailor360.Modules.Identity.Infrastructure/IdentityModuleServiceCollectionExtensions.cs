using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Tailor360.Modules.Identity.Application.Abstractions;
using Tailor360.Modules.Identity.Application.Abuse;
using Tailor360.Modules.Identity.Application.Access;
using Tailor360.Modules.Identity.Application.Authentication;
using Tailor360.Modules.Identity.Application.Me;
using Tailor360.Modules.Identity.Application.Mfa;
using Tailor360.Modules.Identity.Application.Notifications;
using Tailor360.Modules.Identity.Application.Options;
using Tailor360.Modules.Identity.Application.Passkeys;
using Tailor360.Modules.Identity.Application.Passwords;
using Tailor360.Modules.Identity.Application.Recovery;
using Tailor360.Modules.Identity.Application.Sessions;
using Tailor360.Modules.Identity.Application.Timing;
using Tailor360.Modules.Identity.Domain.Users;
using Tailor360.Modules.Identity.Infrastructure.Access;
using Tailor360.Modules.Identity.Infrastructure.Email;
using Tailor360.Modules.Identity.Infrastructure.Passkeys;
using Tailor360.Modules.Identity.Infrastructure.Persistence;
using Tailor360.Modules.Identity.Infrastructure.Security;
using Tailor360.Modules.Identity.Infrastructure.Sessions;
using Tailor360.Platform.Abstractions.Ports;
using Tailor360.Platform.Persistence;
using Tailor360.Platform.Persistence.Conventions;
using Tailor360.Platform.Persistence.Migrating;
using Tailor360.Platform.Security.Authentication;
using Tailor360.Platform.Security.Background;

namespace Tailor360.Modules.Identity.Infrastructure;

/// <summary>
/// Registers the Identity module: users, roles, permissions, branch assignments, sessions and multi-factor authentication.
/// Hosts compose the application only through extensions like this one (architecture rule ARCH-006),
/// so a host never depends on a module's internal types.
/// </summary>
public static class IdentityModuleServiceCollectionExtensions
{
    /// <summary>The database schema this module owns. No other module may map a table in it.</summary>
    public const string SchemaName = IdentityDbContext.SchemaName;

    /// <summary>Registers the module's services, options and persistence.</summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configuration">Application configuration.</param>
    public static IServiceCollection AddIdentityModule(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        AddOptions(services);
        AddPersistence(services);
        AddAccessServices(services);

        // The timing floor is shared by the sign-in and the recovery request, both of which would
        // otherwise answer faster for an address nobody holds than for one somebody does.
        services.TryAddSingleton<IUniformResponseTime, UniformResponseTime>();

        AddPasswordServices(services);
        AddSessionServices(services);
        AddMultiFactorServices(services);
        AddPasskeyServices(services);
        AddSignInServices(services);
        AddRecoveryServices(services);
        AddEmailServices(services);

        return services;
    }

    private static void AddOptions(IServiceCollection services)
    {
        services.AddOptions<PasswordPolicyOptions>()
            .BindConfiguration(PasswordPolicyOptions.SectionName)
            .ValidateDataAnnotations()
            .Validate(
                options => options.IsUsable,
                "The configured password policy is not usable. The minimum length may not be below " +
                "twelve characters and may not exceed the maximum. A deployment that weakened the " +
                "policy by mistake must fail to start rather than accept weak passwords quietly.")
            .ValidateOnStart();

        services.AddOptions<AccountLockoutOptions>()
            .BindConfiguration(AccountLockoutOptions.SectionName)
            .ValidateDataAnnotations()
            .Validate(
                options => options.IsUsable,
                "The configured lockout policy is not usable. The maximum duration must be at least " +
                "the base duration, and the threshold must be at least one.")
            .ValidateOnStart();

        services.AddOptions<Argon2idOptions>()
            .BindConfiguration(Argon2idOptions.SectionName)
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddOptions<MfaOptions>()
            .BindConfiguration(MfaOptions.SectionName)
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddOptions<RecoveryOptions>()
            .BindConfiguration(RecoveryOptions.SectionName)
            .ValidateDataAnnotations()
            .Validate(
                options => options.IsUsable,
                "The configured recovery-token lifetime is not usable. A recovery link is a password " +
                "with a timer on it, so it lives for between five minutes and one hour; a deployment " +
                "that widened that by mistake must fail to start rather than issue long-lived links.")
            .Validate(
                options => options.IsPublicBaseUrlUsable,
                "Identity:Recovery:PublicBaseUrl must be an absolute https URI. A recovery link carries "
                + "a token that sets a password, so it may not travel over plain HTTP; a developer's "
                + "loopback opts out with Identity:Recovery:AllowInsecurePublicBaseUrl.")
            .ValidateOnStart();

        services.AddOptions<CredentialThrottleOptions>()
            .BindConfiguration(CredentialThrottleOptions.SectionName)
            .ValidateDataAnnotations()
            .Validate(
                options => options.IsUsable,
                "The configured credential throttle is not usable. Each endpoint tolerates at least one "
                + "attempt per account, at least as many per address as per account, and counts them "
                + "over a window longer than nothing. A deployment that switched the control off by "
                + "mistake must fail to start rather than accept unbounded guessing.")
            .ValidateOnStart();

        services.AddOptions<SignInTimingOptions>()
            .BindConfiguration(SignInTimingOptions.SectionName)
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddOptions<CaptchaOptions>()
            .BindConfiguration(CaptchaOptions.SectionName)
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddOptions<TrustedDeviceOptions>()
            .BindConfiguration(TrustedDeviceOptions.SectionName)
            .ValidateDataAnnotations()
            .Validate(
                options => options.IsUsable,
                "A device may be remembered for at most thirty days, which is the cap the domain "
                + "enforces. A longer configured lifetime is refused at startup rather than silently "
                + "truncated, because the operator who set it believes it is in force.")
            .ValidateOnStart();

        services.AddOptions<PasskeyOptions>()
            .BindConfiguration(PasskeyOptions.SectionName)
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddOptions<EmailDeliveryOptions>()
            .BindConfiguration(EmailDeliveryOptions.SectionName)
            .ValidateDataAnnotations()
            .ValidateOnStart();
    }

    private static void AddPersistence(IServiceCollection services)
    {
        services.AddDbContext<IdentityDbContext>((provider, builder) =>
        {
            var options = provider.GetRequiredService<IOptions<DatabaseOptions>>().Value;

            builder.UseNpgsql(options.BuildPooledConnectionString(), npgsql =>
            {
                npgsql.MigrationsHistoryTable(
                    ModuleDbContext.MigrationsHistoryTable, IdentityDbContext.SchemaName);
                npgsql.CommandTimeout(options.CommandTimeoutSeconds);
                npgsql.EnableRetryOnFailure(3, TimeSpan.FromSeconds(2), null);
            })
            .UseSnakeCaseNamingConvention();
        });

        services.AddModuleContext<IdentityDbContext>(
            IdentityDbContext.SchemaName, ModuleContextRegistry.IdentityOrder);
    }

    private static void AddAccessServices(IServiceCollection services)
    {
        // Resolved on every authenticated request, so it is scoped to the request's context rather than
        // cached: a role removed a minute ago has to stop working now, not at the next sign-in.
        services.TryAddScoped<IUserAccessQuery, UserAccessQuery>();

        // Used by init-reference-data. Registered here rather than in the command-line tool so that the
        // seeding rules live with the schema they write, and so an integration test can call it.
        services.TryAddScoped<IIdentityReferenceDataSeeder, IdentityReferenceDataSeeder>();

        // Not TryAdd, for the same reason the session ticket store is not. Platform.Security registers
        // a fail-closed authority store so that a host composed without this module refuses every job
        // that would act for a person; this one has to be the later — and therefore winning — entry.
        services.AddScoped<IRequesterAuthorityStore, RequesterAuthorityStore>();
    }

    private static void AddPasswordServices(IServiceCollection services)
    {
        // The hasher is registered against ASP.NET Core Identity's interface, so replacing Argon2id is a
        // change to this one line and stored hashes keep verifying under whichever hasher recognises
        // their prefix.
        services.TryAddSingleton<IPasswordHasher<StaffUser>, Argon2idPasswordHasher>();

        // No breached-password list ships with the product. Registering the no-op keeps the check on one
        // code path, so adding a local list later is a registration and not a change to the logic that
        // sets a password.
        services.TryAddSingleton<IBreachedPasswordChecker, NullBreachedPasswordChecker>();

        services.TryAddSingleton<IPasswordPolicyService, PasswordPolicyService>();
        services.TryAddSingleton<IPasswordHashingService, PasswordHashingService>();
    }

    private static void AddSessionServices(IServiceCollection services)
    {
        // Not TryAdd. Platform.Security registers a fail-closed store so that a host composed without
        // this module refuses every cookie rather than throwing on the first authenticated request;
        // that registration runs first, so this one has to be the later — and therefore winning — entry.
        services.AddScoped<ISessionTicketStore, SessionTicketStore>();

        services.TryAddScoped<ISessionService, SessionService>();
    }

    private static void AddMultiFactorServices(IServiceCollection services)
    {
        // The authenticator's shared secret is the one stored secret this module has to read back, so
        // it is wrapped with data protection rather than hashed. AddDataProtection is safe to call more
        // than once and composes with whatever persists the key ring; what it must not be is left
        // uncalled, because an unpersisted ring makes every enrolled secret unreadable at the next
        // deployment. Section 4.4 requires the ring in platform.data_protection_keys.
        services.AddDataProtection();

        services.TryAddSingleton<ITotpService, TotpService>();
        services.TryAddSingleton<IRecoveryCodeService, RecoveryCodeService>();
        services.TryAddSingleton<ISecretProtector, DataProtectionSecretProtector>();
        services.TryAddSingleton<IMfaRequirementPolicy, MfaRequirementPolicy>();

        services.TryAddScoped<IIdentityStore, IdentityStore>();
        services.TryAddScoped<IMfaChallengeService, MfaChallengeService>();
        services.TryAddScoped<TotpEnrolmentHandler>();
    }

    private static void AddPasskeyServices(IServiceCollection services)
    {
        // The ceremony store is a singleton because a challenge outlives the request that issued it —
        // that is the whole point of it — and it is scoped to the instance, so a deployment behind more
        // than one web replica needs sticky routing for the two requests of one ceremony, or a shared
        // store. Recorded in the threat model rather than left to be discovered.
        services.TryAddSingleton<PasskeyCeremonyStore>();

        // Scoped, because the cross-account uniqueness check reads the module's context.
        services.TryAddScoped<IPasskeyCeremony, Fido2PasskeyCeremony>();
        services.TryAddScoped<PasskeyHandler>();
    }

    private static void AddSignInServices(IServiceCollection services)
    {
        // A singleton, because the counters have to outlive the request they are counting. It holds
        // nothing durable: see ICredentialThrottle for what that costs and why the lockout column and
        // the endpoint rate limits are what cover it.
        services.TryAddSingleton<ICredentialThrottle, CredentialThrottle>();

        // No human-verification provider ships with the product. The registered verifier reports itself
        // off, so the sign-in path asks nobody to prove they are human until a deployment registers one
        // — and if the feature is switched on without one, it refuses rather than waves through.
        services.TryAddSingleton<ICaptchaVerifier, DisabledCaptchaVerifier>();

        services.TryAddSingleton<IOpaqueTokenFactory, OpaqueTokenFactory>();

        // A singleton, and that is the whole point of it. The decoy exists so that a sign-in naming an
        // account that cannot be used costs the same as one naming an account that can; built per
        // request it would cost a second Argon2id derivation and make the unknown branch twice as slow
        // as the known one, which is a sharper account oracle than the one it was added to close.
        services.TryAddSingleton<IDecoyCredential, DecoyCredential>();

        services.TryAddScoped<ISignInDirectory, SignInDirectory>();
        services.TryAddScoped<SignInHandler>();
        services.TryAddScoped<MultiFactorSignInHandler>();
        services.TryAddScoped<SignOutHandler>();
        services.TryAddScoped<CurrentUserQuery>();
    }

    private static void AddRecoveryServices(IServiceCollection services)
    {
        services.TryAddSingleton<IRecoveryTokenService, RecoveryTokenService>();
        services.TryAddScoped<PasswordRecoveryHandler>();
    }

    private static void AddEmailServices(IServiceCollection services)
    {
        services.TryAddSingleton<IEmailTemplateRenderer, EmailTemplateRenderer>();

        // The queue is resolved by its concrete type as well as through the port, because the
        // background service reads from it while the port is deliberately write-only: nothing in the
        // application layer should be able to drain the queue it writes to.
        services.TryAddSingleton<ChannelEmailDispatchQueue>();
        services.TryAddSingleton<IEmailDispatchQueue>(
            provider => provider.GetRequiredService<ChannelEmailDispatchQueue>());

        // Both senders are registered and configuration decides which one answers IEmailSender, at
        // resolution rather than at registration, so a test host changes the setting rather than the
        // service graph.
        services.TryAddSingleton<CollectedEmailSender>();
        services.TryAddSingleton<SmtpEmailSender>();
        services.TryAddSingleton<IEmailSender>(provider =>
        {
            var options = provider.GetRequiredService<IOptions<EmailDeliveryOptions>>().Value;
            return options.Delivery is EmailDelivery.Collect
                ? provider.GetRequiredService<CollectedEmailSender>()
                : provider.GetRequiredService<SmtpEmailSender>();
        });

        services.TryAddScoped<IIdentityMailer, IdentityMailer>();
        services.AddHostedService<EmailDispatchService>();
    }
}
