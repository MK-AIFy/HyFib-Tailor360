using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Tailor360.Platform.Security.Authorisation;

namespace Tailor360.Platform.Security.Background;

/// <summary>Registers the background-job principal for a host that runs jobs rather than requests.</summary>
public static class BackgroundServiceCollectionExtensions
{
    /// <summary>
    /// Registers the permission catalogue, the per-scope principal holder and the scope factory.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Only the worker and the command-line tool call this. It deliberately registers no authentication
    /// scheme, no anti-forgery service and no authorisation pipeline: those belong to a host that
    /// serves requests, and a job has none.
    /// </para>
    /// <para>
    /// <see cref="ICurrentUser"/> resolves from the scope's principal holder, so a scope opened by
    /// <see cref="IWorkerScopeFactory"/> presents the job's principal to every service in it, and any
    /// other scope in the process presents the anonymous caller — which holds nothing. Both halves are
    /// deliberate: the first is what makes an authorisation check in a job mean the same thing it means
    /// in a request, and the second is what stops a scope created for some other purpose from
    /// inheriting a job's authority.
    /// </para>
    /// <para>
    /// The requester authority store is registered fail-closed. The module that owns accounts replaces
    /// it, and until it does every job that would act for a person is refused rather than run
    /// unchecked.
    /// </para>
    /// </remarks>
    public static IServiceCollection AddTailor360WorkerScopes(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddTailor360PermissionCatalogue();

        services.TryAddScoped<WorkerPrincipalAccessor>();
        services.TryAddScoped<ICurrentUser>(provider =>
            provider.GetRequiredService<WorkerPrincipalAccessor>().Principal);
        services.TryAddScoped<IRequesterAuthorityStore, UnavailableRequesterAuthorityStore>();

        services.TryAddSingleton<IWorkerScopeFactory, WorkerScopeFactory>();

        return services;
    }
}
