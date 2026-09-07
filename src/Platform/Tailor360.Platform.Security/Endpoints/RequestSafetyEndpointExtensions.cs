using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Tailor360.Platform.Security.Endpoints;

/// <summary>
/// The declarations that make a command safe to retry and an update safe to make: idempotency keys and
/// concurrency preconditions.
/// </summary>
/// <remarks>
/// They sit beside <c>RequirePermission</c> and <c>Audited</c> rather than being applied by the host,
/// because which commands are retryable and which aggregates are editable is something only the module
/// writing the endpoint knows.
/// </remarks>
public static class RequestSafetyEndpointExtensions
{
    private static readonly string[] ProblemJson = ["application/problem+json"];

    /// <summary>
    /// Requires an <c>Idempotency-Key</c>, and makes a repeat of that key return the first outcome
    /// instead of a second effect.
    /// </summary>
    /// <remarks>
    /// Declare it on every command a client may reasonably send twice — confirm, scan, post, pay,
    /// callback, webhook replay (plan Section 4.4) — which in practice is every command the progressive
    /// web application can queue offline or retry after re-authenticating. The host must have called
    /// <c>UseTailor360IdempotencyKeys()</c> for a request with a body to be fingerprinted.
    /// </remarks>
    /// <typeparam name="TBuilder">The endpoint builder type.</typeparam>
    /// <param name="builder">The endpoint builder.</param>
    public static TBuilder RequireIdempotency<TBuilder>(this TBuilder builder)
        where TBuilder : IEndpointConventionBuilder
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.AddEndpointFilter<TBuilder, IdempotencyEndpointFilter>();
        builder.WithMetadata(new IdempotentEndpointMetadata());

        // Declared so that the generated specification carries the refusals this filter can produce
        // without every module restating them, and so the client's generated types include them.
        builder.WithMetadata(new ProducesResponseTypeMetadata(
            StatusCodes.Status400BadRequest, typeof(ProblemDetails), ProblemJson));
        builder.WithMetadata(new ProducesResponseTypeMetadata(
            StatusCodes.Status409Conflict, typeof(ProblemDetails), ProblemJson));
        builder.WithMetadata(new ProducesResponseTypeMetadata(
            StatusCodes.Status422UnprocessableEntity, typeof(ProblemDetails), ProblemJson));

        return builder;
    }

    /// <summary>
    /// Requires an <c>If-Match</c> carrying the version the change was made against, so that two people
    /// editing one record cannot silently overwrite each other.
    /// </summary>
    /// <remarks>
    /// Declare it on every update or command against an <em>editable</em> aggregate. Append-only
    /// aggregates — scan events, ledger entries, payments, posted invoices — carry no version and use an
    /// idempotency key instead (<c>docs/architecture/conventions.md</c> section 4.1).
    /// </remarks>
    /// <typeparam name="TBuilder">The endpoint builder type.</typeparam>
    /// <param name="builder">The endpoint builder.</param>
    public static TBuilder RequireIfMatch<TBuilder>(this TBuilder builder)
        where TBuilder : IEndpointConventionBuilder
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.AddEndpointFilter<TBuilder, IfMatchEndpointFilter>();
        builder.WithMetadata(new IfMatchRequiredMetadata());

        builder.WithMetadata(new ProducesResponseTypeMetadata(
            StatusCodes.Status400BadRequest, typeof(ProblemDetails), ProblemJson));
        builder.WithMetadata(new ProducesResponseTypeMetadata(
            StatusCodes.Status409Conflict, typeof(ProblemDetails), ProblemJson));
        builder.WithMetadata(new ProducesResponseTypeMetadata(
            StatusCodes.Status428PreconditionRequired, typeof(ProblemDetails), ProblemJson));

        return builder;
    }
}

/// <summary>Registers what the idempotency and timeout mechanisms need from the container.</summary>
public static class RequestSafetyServiceCollectionExtensions
{
    /// <summary>
    /// Registers the idempotency request options, the clock the duplicate wait is measured on, and the
    /// request-timeout catalogue.
    /// </summary>
    /// <remarks>
    /// <see cref="SecurityServiceCollectionExtensions.AddTailor360Security"/> calls this, so a host that
    /// registers the security model gets it. The two pipeline steps —
    /// <c>UseTailor360IdempotencyKeys()</c> and the framework's <c>UseRequestTimeouts()</c> — still have
    /// to be placed by the host, because where they sit in the pipeline is a decision the host makes.
    /// </remarks>
    /// <param name="services">The service collection.</param>
    public static IServiceCollection AddTailor360RequestSafety(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddSingleton(TimeProvider.System);

        services.AddOptions<IdempotencyRequestOptions>()
            .BindConfiguration(IdempotencyRequestOptions.SectionName)
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddTailor360RequestTimeouts();

        return services;
    }
}
