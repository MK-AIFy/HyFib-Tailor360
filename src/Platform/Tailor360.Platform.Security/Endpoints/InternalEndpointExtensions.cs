using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace Tailor360.Platform.Security.Endpoints;

/// <summary>
/// The declaration verb for a route that is deliberately absent from the published API document.
/// </summary>
/// <remarks>
/// <para>
/// The endpoint inventory rule is that every route the application publishes appears in
/// <c>docs/api/openapi.v1.json</c>. Without a way to say "this one does not", the rule would either be
/// unsatisfiable — a fallback that answers 404 has no contract to publish — or it would have to accept
/// <c>ExcludeFromDescription</c> as its own excuse, which makes the rule toothless: hiding an endpoint
/// from the document would become the way to avoid documenting it.
/// </para>
/// <para>
/// So the exclusion has to be said out loud, with a reason and the place the decision was recorded, the
/// same shape as <see cref="AnonymousJustificationMetadata"/>. The verb excludes the route from the
/// document as well, so the two facts cannot drift apart: an internal endpoint is exactly one that is
/// both excluded and explained.
/// </para>
/// <para>
/// It is not an exemption from anything else. An internal endpoint still declares an authorisation
/// policy or a justified anonymous exposure (ARCH-007), is still audited when it changes state
/// (ARCH-008), and still declares a rate-limit policy.
/// </para>
/// </remarks>
public static class InternalEndpointExtensions
{
    /// <summary>
    /// Declares that this route is part of the application's plumbing rather than of its published
    /// contract, and excludes it from the generated API document.
    /// </summary>
    /// <param name="builder">The endpoint builder.</param>
    /// <param name="reason">
    /// Why the route publishes no contract — what it is for, and why a client never calls it directly.
    /// </param>
    /// <param name="reviewedIn">The issue, decision record or document in which that was agreed.</param>
    /// <typeparam name="TBuilder">The endpoint builder type.</typeparam>
    /// <returns>The same builder, so declarations chain.</returns>
    public static TBuilder InternalEndpoint<TBuilder>(
        this TBuilder builder,
        string reason,
        string reviewedIn)
        where TBuilder : IEndpointConventionBuilder
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        ArgumentException.ThrowIfNullOrWhiteSpace(reviewedIn);

        builder.WithMetadata(new InternalEndpointMetadata(reason, reviewedIn));
        builder.ExcludeFromDescription();
        return builder;
    }
}

/// <summary>
/// Records why a route carries no published contract. The endpoint inventory test accepts this — and
/// nothing else — as the reason a mapped route is missing from the API document.
/// </summary>
/// <param name="Reason">Why the route publishes no contract.</param>
/// <param name="ReviewedIn">The issue, decision record or document in which that was agreed.</param>
public sealed record InternalEndpointMetadata(string Reason, string ReviewedIn);
