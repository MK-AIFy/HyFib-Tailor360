using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Tailor360.Platform.Security.Antiforgery;
using Tailor360.Platform.Security.Audit;
using Tailor360.Platform.Security.Authentication;
using Tailor360.Platform.Security.Authorisation;
using Tailor360.Platform.Security.FieldVisibility;
using Tailor360.Platform.Security.Permissions;

namespace Tailor360.Platform.Security;

/// <summary>Registers the authentication and authorisation model shared by every module.</summary>
public static class SecurityServiceCollectionExtensions
{
    /// <summary>
    /// Registers the session cookie scheme, the session-backed caller, the anti-forgery token pair, the
    /// permission catalogue, the permission and branch-scope handlers and the policy provider that
    /// materialises <c>perm:</c> policies.
    /// </summary>
    /// <remarks>
    /// Exactly one authentication scheme is registered, which is what keeps ARCH-019 — no endpoint
    /// accepts more than one scheme — true by construction rather than by inspection. API keys and
    /// OAuth clients arrive on their own path and their own scheme in a later issue.
    /// </remarks>
    public static IServiceCollection AddTailor360Security(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        AddOptions(services);
        AddAuthenticationAndCaller(services);
        AddCrossSiteDefences(services);
        AddAuthorisation(services);

        return services;
    }

    private static void AddOptions(IServiceCollection services)
    {
        services.AddOptions<StepUpOptions>()
            .BindConfiguration(StepUpOptions.SectionName)
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddOptions<SessionAuthenticationOptions>()
            .BindConfiguration(SessionAuthenticationOptions.SectionName)
            .ValidateDataAnnotations()
            .Validate(
                options => options.IsUsable,
                "The configured session lifetimes cannot behave as described. The inactivity timeout "
                + "must fit inside the absolute lifetime, the expiry warning must arrive before the "
                + "deadline it warns about, and the sliding write-back interval must be shorter than "
                + "the inactivity timeout. A deployment that lengthened a session by mistake must fail "
                + "to start rather than hand out sessions that never end.")
            .ValidateOnStart();

        services.AddOptions<RequestOriginOptions>()
            .BindConfiguration(RequestOriginOptions.SectionName)
            .ValidateDataAnnotations()
            .ValidateOnStart();
    }

    private static void AddAuthenticationAndCaller(IServiceCollection services)
    {
        // The session resolved for this request, filled once by the authentication handler. Registered
        // before ICurrentUser because that is what reads it.
        services.TryAddScoped<SessionContext>();

        // Fail closed twice over. Without a module owning sessions, every cookie is refused; and with no
        // session resolved, SessionCurrentUser reports exactly what AnonymousCurrentUser would — nobody,
        // holding nothing — so there is no state in which a permissive principal can be resolved.
        services.TryAddScoped<ISessionTicketStore, UnavailableSessionTicketStore>();
        services.TryAddScoped<ICurrentUser, SessionCurrentUser>();

        services.AddAuthentication(SessionAuthenticationDefaults.Scheme)
            .AddScheme<AuthenticationSchemeOptions, SessionAuthenticationHandler>(
                SessionAuthenticationDefaults.Scheme, displayName: null, configureOptions: null);
    }

    private static void AddCrossSiteDefences(IServiceCollection services)
        => services.AddAntiforgery(options =>
        {
            options.Cookie.Name = AntiforgeryDefaults.CookieName;
            options.Cookie.HttpOnly = true;
            options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
            options.Cookie.SameSite = SameSiteMode.Lax;
            options.Cookie.Path = "/";
            options.Cookie.IsEssential = true;
            options.HeaderName = AntiforgeryDefaults.HeaderName;

            // Framing is refused by the content-security policy's frame-ancestors directive, which
            // every response already carries. Letting the anti-forgery service add a second, weaker
            // header as well would only invite someone to conclude the directive is redundant.
            options.SuppressXFrameOptionsHeader = true;
        });

    /// <summary>
    /// Registers the permission catalogue on its own, for a host that has no request pipeline.
    /// </summary>
    /// <remarks>
    /// The command-line tool and the worker need to know what the application can authorise — the tool
    /// to seed the roles that grant permissions, the worker to build a job's declared scope — without
    /// registering a cookie scheme, an anti-forgery token pair or an authorisation pipeline they have
    /// no requests to run. This is that subset, and <see cref="AddTailor360Security"/> calls it, so the
    /// catalogue is composed the same way everywhere.
    /// </remarks>
    public static IServiceCollection AddTailor360PermissionCatalogue(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IPermissionSource, ApplicationPermissions>());
        services.TryAddSingleton<PermissionCatalogue>();

        return services;
    }

    /// <summary>
    /// Registers the response-view catalogue and the policy that projects a response through it.
    /// </summary>
    /// <remarks>
    /// Separate from <see cref="AddTailor360Security"/> for the same reason the permission catalogue is:
    /// a host with no request pipeline still has to know what a view may carry — a worker rendering a
    /// job card for the print queue is projecting the same fields for the same reasons as the endpoint
    /// that serves it on screen, and it must not be free to project a different set.
    /// </remarks>
    public static IServiceCollection AddTailor360FieldVisibility(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IResponseViewSource, ApplicationResponseViews>());
        services.TryAddSingleton<ResponseViewCatalogue>();
        services.TryAddScoped<IFieldVisibilityPolicy, FieldVisibilityPolicy>();

        return services;
    }

    private static void AddAuthorisation(IServiceCollection services)
    {
        services.AddTailor360PermissionCatalogue();
        services.AddTailor360FieldVisibility();
        services.TryAddSingleton<IAuthorizationPolicyProvider, PermissionPolicyProvider>();
        services.AddScoped<IAuthorizationHandler, PermissionAuthorisationHandler>();
        services.AddScoped<IAuthorizationHandler, BranchScopeAuthorisationHandler>();
        services.AddScoped<IAuthorizationHandler, SessionAssuranceAuthorisationHandler>();
        services.AddScoped<IAuthorizationHandler, ResourceBranchAuthorisationHandler>();
        services.AddScoped<IAuthorizationHandler, ResourceOwnershipAuthorisationHandler>();
        services.AddScoped<IAuthorizationHandler, StepUpAuthorisationHandler>();

        // The resource resolved for this request. Scoped and starting in its refusing state, so that a
        // pipeline assembled without the resolution step denies rather than skips the check.
        services.TryAddScoped<ResourceScopeContext>();

        // The refusal path, in two parts. The problem writer is registered by its own type because it is
        // composed rather than resolved: the middleware asks for exactly one result handler, and the
        // recorder wraps the writer so that a refusal cannot be audited without the caller having been
        // given the answer that was audited.
        services.TryAddSingleton<AuthorisationProblemResultHandler>();
        services.TryAddSingleton<AuthorisationDenialCoalescer>();

        // Not TryAdd: the framework registers its own bare-status-line handler, and this one has to be
        // the later — and therefore winning — entry, or every refusal answers with an empty body.
        services.AddSingleton<IAuthorizationMiddlewareResultHandler, AuthorisationDenialAuditingHandler>();

        services.AddAuthorization();
    }
}
