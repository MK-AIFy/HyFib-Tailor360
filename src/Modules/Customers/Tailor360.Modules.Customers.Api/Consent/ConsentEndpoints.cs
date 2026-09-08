using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Tailor360.Modules.Customers.Api.Payloads;
using Tailor360.Modules.Customers.Application.Consent;
using Tailor360.Modules.Customers.Application.Preferences;
using Tailor360.Modules.Customers.Domain;
using Tailor360.Modules.Customers.Domain.Consent;
using Tailor360.Modules.Customers.Domain.Preferences;
using Tailor360.Platform.Abstractions.Multitenancy;
using Tailor360.Platform.Abstractions.Results;
using Tailor360.Platform.Security.Authorisation;
using Tailor360.Platform.Security.Endpoints;
using Tailor360.Platform.Security.Permissions;

namespace Tailor360.Modules.Customers.Api.Consent;

/// <summary>
/// What a customer agreed to, and how she wants to be reached.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Both are gated on <c>customers.read_consent</c> to read.</strong>
/// <c>docs/nfr/data-classification.md</c> section 5.3 is one inventory row covering "consent records
/// <em>and</em> communication preferences", and names who may see it: Reception and Branch Manager to
/// record and read, Auditor to read. Those are exactly the roles the permission matrix grants
/// <c>customers.read_consent</c>. Gating the preference on <c>customers.read</c> instead would show a
/// customer's quiet hours to every workshop role that can find her, which that row does not approve.
/// </para>
/// <para>
/// <strong>Recording is <c>customers.update</c>, and it carries no reason.</strong> The permission's
/// own description in the catalogue is "correct a customer record and record or withdraw consent", and
/// its holders — Branch Manager and Reception — are the roles section 5.3 names. A reason is demanded
/// of a <em>correction</em>, where the trail has to say why somebody changed what the record says
/// about a person. Consent is not a correction: the reason an answer was recorded is that the customer
/// gave it, and the trail already carries the purpose, the outcome and the wording version.
/// </para>
/// <para>
/// <strong>The branch declarations are the customer record's, for the customer record's reasons.</strong>
/// A consent record and a preference belong to the customer, who is organisation-wide and belongs to no
/// one branch (<c>docs/prd/workflows/branch-scenarios.md</c> section 3.2). A customer served at two
/// branches has one set of answers, and either counter must be able to read them before sending her
/// anything.
/// </para>
/// </remarks>
public static class ConsentEndpoints
{
    /// <summary>Where the reach and permission choices on these routes were reviewed.</summary>
    private const string Review = "#26, docs/nfr/data-classification.md section 5.3";

    /// <summary>Why a route naming a customer declares no resource scope (ARCH-023).</summary>
    private const string NoBranchResource =
        "A consent record and a communication preference belong to the customer, and a customer record "
        + "is organisation-wide: it belongs to no one branch, so there is no branch for a resource "
        + "scope to resolve it to (branch-scenarios.md section 3.2). Resolving it to the owning branch "
        + "would refuse the second branch that serves her, which is the scenario the module exists for, "
        + "and would leave that counter about to message somebody without being able to read whether "
        + "she agreed. Reach is enforced instead where it means something: the handler refuses a record "
        + "outside the caller's organisation.";

    /// <summary>Consent and preference reads are never stored by an intermediary or a shared browser.</summary>
    private const string NoStore = "no-store";

    /// <summary>Maps the consent and communication-preference routes.</summary>
    /// <param name="customers">The <c>/api/v1/customers</c> group.</param>
    /// <returns>The same group, for chaining.</returns>
    public static RouteGroupBuilder MapConsentEndpoints(this RouteGroupBuilder customers)
    {
        ArgumentNullException.ThrowIfNull(customers);

        MapReadConsent(customers);
        MapRecordConsent(customers);
        MapReadPreferences(customers);
        MapReplacePreferences(customers);

        return customers;
    }

    private static void MapReadConsent(RouteGroupBuilder customers)
    {
        customers.MapGet("/{customerId:guid}/consent", async Task<IResult> (
                Guid customerId,
                HttpContext context,
                ConsentHandler handler,
                ICurrentUser caller,
                CancellationToken cancellationToken) =>
            {
                var result = await handler.ReadAsync(
                    customerId, caller.Context.OrganisationId, cancellationToken);

                if (result.IsFailure)
                {
                    return Problems.From(result.Error, context);
                }

                context.Response.Headers.CacheControl = NoStore;

                return Results.Ok(CustomerConsentPayload.From(result.Value));
            })
            .Produces<CustomerConsentPayload>(StatusCodes.Status200OK)
            .WithName("GetCustomerConsent")
            .WithSummary("Read what a customer has agreed to, purpose by purpose.")
            .WithDescription(
                "The whole register comes back, including purposes she has never been asked about — "
                + "which is how the counter knows to ask — and purposes that have been retired, "
                + "because what she said about one still stands. Answers are newest first, so the "
                + "first of them is the one that stands; `canBeAnswered` says whether a new answer may "
                + "be recorded now.")
            .RequirePermission(CustomersPermissions.ReadConsent, BranchScope.AssignedBranches)
            .TouchesNoBranchOwnedResource(NoBranchResource, Review)
            .RequireRateLimiting(RateLimitPolicyNames.DefaultUser)
            .WithRequestTimeout(RequestTimeoutPolicies.Read);
    }

    private static void MapRecordConsent(RouteGroupBuilder customers)
    {
        customers.MapPost("/{customerId:guid}/consent", async Task<IResult> (
                Guid customerId,
                RecordConsentRequest request,
                HttpContext context,
                ConsentHandler handler,
                ICurrentUser caller,
                CancellationToken cancellationToken) =>
            {
                if (!Enum.TryParse<ConsentDecision>(request?.Decision, out var decision)
                    || !Enum.IsDefined(decision))
                {
                    return Problems.From(
                        CustomersErrors.ConsentDecisionNotUnderstood("decision"), context);
                }

                var result = await handler.RecordAsync(
                    new RecordConsentCommand(
                        customerId,
                        caller.Context.OrganisationId,
                        request!.PurposeKey,
                        decision,
                        request.Source,
                        caller.Context.BranchId,
                        caller.UserId),
                    cancellationToken);

                if (result.IsFailure)
                {
                    return Problems.From(result.Error, context);
                }

                context.Response.Headers.CacheControl = NoStore;

                return Results.Ok(ConsentAnswerPayload.From(result.Value));
            })
            .Produces<ConsentAnswerPayload>(StatusCodes.Status200OK)
            .WithName("RecordCustomerConsent")
            .WithSummary("Record what a customer said about one purpose.")
            .WithDescription(
                "Appends an answer; it never edits one. Withdrawing is a `Withdrawn` answer and "
                + "agreeing again is another `Granted` one, so the evidence that she once withdrew "
                + "survives her changing her mind. The wording version is read from the register here "
                + "rather than sent, because a client that could name a version could record an answer "
                + "against words she was never read; a purpose with no published wording is refused "
                + "for the same reason. A withdrawal is written to the trail as "
                + "`customers.consent.withdrawn` rather than as the action this route declares, "
                + "because a withdrawal is what somebody reviewing the trail is looking for.")
            .RequirePermission(CustomersPermissions.Update, BranchScope.AssignedBranches)
            .TouchesNoBranchOwnedResource(NoBranchResource, Review)
            .RequireRateLimiting(RateLimitPolicyNames.Write)
            .Audited(ConsentHandler.RecordedAction)
            .RequireIdempotency()
            .WithRequestTimeout(RequestTimeoutPolicies.Command);
    }

    private static void MapReadPreferences(RouteGroupBuilder customers)
    {
        customers.MapGet("/{customerId:guid}/communication-preferences", async Task<IResult> (
                Guid customerId,
                HttpContext context,
                PreferenceHandler handler,
                ICurrentUser caller,
                CancellationToken cancellationToken) =>
            {
                var result = await handler.ReadAsync(
                    customerId, caller.Context.OrganisationId, cancellationToken);

                if (result.IsFailure)
                {
                    return Problems.From(result.Error, context);
                }

                if (result.Value.Version is { } version)
                {
                    context.Response.SetEntityTag(version);
                }

                context.Response.Headers.CacheControl = NoStore;

                return Results.Ok(CommunicationPreferencePayload.From(result.Value));
            })
            .Produces<CommunicationPreferencePayload>(StatusCodes.Status200OK)
            .WithName("GetCustomerCommunicationPreferences")
            .WithSummary("Read how a customer wants to be reached.")
            .WithDescription(
                "`hasBeenRecorded` is false when nobody has asked her, and the response then carries "
                + "no ETag — there is no version of a row that does not exist, which is why the first "
                + "write is the one change that needs no `If-Match`. An empty `allowedChannels` on a "
                + "recorded preference is an answer and not a gap: it means do not message her, and it "
                + "withdraws consent to nothing.")
            .RequirePermission(CustomersPermissions.ReadConsent, BranchScope.AssignedBranches)
            .TouchesNoBranchOwnedResource(NoBranchResource, Review)
            .RequireRateLimiting(RateLimitPolicyNames.DefaultUser)
            .WithRequestTimeout(RequestTimeoutPolicies.Read);
    }

    private static void MapReplacePreferences(RouteGroupBuilder customers)
    {
        customers.MapPut("/{customerId:guid}/communication-preferences", async Task<IResult> (
                Guid customerId,
                ReplacePreferencesRequest request,
                HttpContext context,
                PreferenceHandler handler,
                ICurrentUser caller,
                CancellationToken cancellationToken) =>
            {
                var channels = ReadChannels(request?.AllowedChannels);

                if (channels.IsFailure)
                {
                    return Problems.From(channels.Error, context);
                }

                var current = await handler.ReadAsync(
                    customerId, caller.Context.OrganisationId, cancellationToken);

                if (current.IsFailure)
                {
                    return Problems.From(current.Error, context);
                }

                // The precondition is demanded only where there is a version to demand it against. A
                // preference nobody has recorded has no row and no ETag, so requiring `If-Match` on the
                // first write would make it unmakeable; from the second write on, two counters saving
                // at once must not silently overwrite each other, which is what the check is for.
                if (current.Value.Version is { } version)
                {
                    var precondition = ConcurrencyResults.CheckIfMatch(
                        context,
                        version,
                        CustomersErrors.ConcurrentChange.Code,
                        CustomersErrors.ConcurrentChange.Message);

                    if (precondition is not null)
                    {
                        return precondition;
                    }
                }

                var result = await handler.ReplaceAsync(
                    new ReplacePreferencesCommand(
                        customerId,
                        caller.Context.OrganisationId,
                        channels.Value,
                        request!.Language,
                        request.QuietHoursStart,
                        request.QuietHoursEnd,
                        caller.UserId),
                    cancellationToken);

                if (result.IsFailure)
                {
                    return result.Error.Code == CustomersErrors.ConcurrentChange.Code
                        ? ConcurrencyResults.VersionConflict(
                            context,
                            CustomersErrors.ConcurrentChange.Code,
                            CustomersErrors.ConcurrentChange.Message,
                            current.Value.Version ?? default)
                        : Problems.From(result.Error, context);
                }

                if (result.Value.Version is { } saved)
                {
                    context.Response.SetEntityTag(saved);
                }

                context.Response.Headers.CacheControl = NoStore;

                return Results.Ok(CommunicationPreferencePayload.From(result.Value));
            })
            .Produces<CommunicationPreferencePayload>(StatusCodes.Status200OK)
            .WithName("ReplaceCustomerCommunicationPreferences")
            .WithSummary("Record how a customer wants to be reached, replacing what was there.")
            .WithDescription(
                "The whole preference is replaced rather than patched, so that the trail reads as a "
                + "state and \"which channels does she accept\" has one answer. An empty "
                + "`allowedChannels` is how she says do not message me, and it withdraws consent to "
                + "nothing. Quiet hours are wall-clock times at the branch and may run backwards over "
                + "midnight, which is the ordinary case; send both ends or neither. `If-Match` is "
                + "required once a preference exists and must be omitted before then, because there is "
                + "no version of a row that does not exist.")
            .RequirePermission(CustomersPermissions.Update, BranchScope.AssignedBranches)
            .TouchesNoBranchOwnedResource(NoBranchResource, Review)
            .RequireRateLimiting(RateLimitPolicyNames.Write)
            .Audited(PreferenceHandler.ChangedAction)
            .RequireIdempotency()
            .WithRequestTimeout(RequestTimeoutPolicies.Command);
    }

    /// <summary>
    /// Reads the channel names a caller sent, refusing one the shop cannot send on.
    /// </summary>
    /// <remarks>
    /// Parsed here rather than bound as an enumeration so that an unknown channel is a field error
    /// naming the field, and not a body the JSON reader rejected with nothing useful to show at a
    /// counter.
    /// </remarks>
    private static Result<IReadOnlyList<CommunicationChannel>> ReadChannels(
        IReadOnlyList<string>? names)
    {
        var channels = new List<CommunicationChannel>();

        foreach (var name in names ?? [])
        {
            if (!Enum.TryParse<CommunicationChannel>(name, ignoreCase: true, out var channel)
                || !Enum.IsDefined(channel))
            {
                return Result.Failure<IReadOnlyList<CommunicationChannel>>(
                    CustomersErrors.CommunicationChannelNotUnderstood("allowedChannels"));
            }

            channels.Add(channel);
        }

        return Result.Success<IReadOnlyList<CommunicationChannel>>(channels);
    }
}
