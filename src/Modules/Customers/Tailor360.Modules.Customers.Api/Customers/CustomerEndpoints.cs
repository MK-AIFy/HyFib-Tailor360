using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Tailor360.Modules.Customers.Api.Payloads;
using Tailor360.Modules.Customers.Application.Abstractions;
using Tailor360.Modules.Customers.Application.Customers;
using Tailor360.Modules.Customers.Domain;
using Tailor360.Modules.Customers.Domain.Customers;
using Tailor360.Platform.Abstractions.Multitenancy;
using Tailor360.Platform.Abstractions.Results;
using Tailor360.Platform.Security.Authorisation;
using Tailor360.Platform.Security.Endpoints;
using Tailor360.Platform.Security.Permissions;

namespace Tailor360.Modules.Customers.Api.Customers;

/// <summary>
/// The customer record: finding one, creating one, correcting it, withdrawing it from use, and
/// recording that a second branch has begun serving the person.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Why every route here declares <see cref="BranchScope.AssignedBranches"/>.</strong> A
/// customer record is organisation-wide and its <em>visibility</em> is the branch-scoped part
/// (<c>docs/prd/workflows/branch-scenarios.md</c> section 3.2). The two narrower declarations both
/// break the scenario the module exists for.
/// <see cref="BranchScope.Organisation"/> is satisfied only by
/// <c>admin.organisation.read_all_branches</c>, which Reception does not hold, so declaring it would
/// close the counter search to the only role that runs it.
/// <see cref="BranchScope.CurrentBranch"/> with a resource scope would refuse the record the moment a
/// second branch serves the customer, which is section 3.1 exactly. What is left is the caller's own
/// assignments, plus the reach rule the module enforces itself: the handler checks the caller's
/// organisation, and the directory decides what a search offers from the record's visibility rows.
/// </para>
/// <para>
/// <strong>The cross-branch reach is bounded by what crosses, not by who asks.</strong> A search
/// answers across the whole organisation, and a record outside the caller's branches comes back as a
/// masked disambiguation card — name, native name, a number with only its last four digits, the owning
/// branch and when it was last touched. That is the trade section 3.1 states out loud: the reveal is
/// cheaper than the duplicate it prevents, whose remedy is an irreversible, step-up merge.
/// </para>
/// <para>
/// <strong>Contact details are a second permission.</strong> Whether the six contact fields are
/// populated is decided once, in <see cref="CustomerPayload.From"/>, against
/// <c>customers.read_contact</c> — so a Tailor reading a job card sees a name and never a telephone
/// number (<c>docs/nfr/data-classification.md</c> section 5.2).
/// </para>
/// </remarks>
public static class CustomerEndpoints
{
    /// <summary>Where the reach and permission choices on these routes were reviewed.</summary>
    private const string Review = "#26, docs/prd/workflows/branch-scenarios.md";

    /// <summary>
    /// Why a route naming a customer declares no resource scope (ARCH-023).
    /// </summary>
    /// <remarks>
    /// The exemption is deliberately noisy to write, so it says the whole reason rather than pointing
    /// at one. A resource scope resolves a row to <em>one</em> branch and compares it with the caller's
    /// reach. A customer belongs to no single branch: it is organisation-wide with a set of branches
    /// that can see it, and the set grows every time somebody is served somewhere new. Resolving it to
    /// its owning branch would refuse the second branch the scenario is about; resolving it to any
    /// visible branch would make the check pass for everyone it could ever refuse.
    /// </remarks>
    private const string NoBranchResource =
        "A customer record is organisation-wide and belongs to no one branch, so there is no branch for "
        + "a resource scope to resolve it to: what is branch-scoped is the set of branches that can see "
        + "it, which grows whenever a second branch serves the person (branch-scenarios.md section 3.2). "
        + "Reach is enforced instead where it means something — the handler refuses a record outside the "
        + "caller's organisation, and the search projects a record outside the caller's branches as a "
        + "masked card rather than a record.";

    /// <summary>Reads of a customer are never stored by an intermediary or a shared browser.</summary>
    private const string NoStore = "no-store";

    /// <summary>Maps the customer record's routes.</summary>
    /// <param name="customers">The <c>/api/v1/customers</c> group.</param>
    /// <returns>The same group, for chaining.</returns>
    public static RouteGroupBuilder MapCustomerEndpoints(this RouteGroupBuilder customers)
    {
        ArgumentNullException.ThrowIfNull(customers);

        MapSearch(customers);
        MapRegister(customers);
        MapRead(customers);
        MapCorrect(customers);
        MapStatusCommands(customers);
        MapOpenAtBranch(customers);
        MapDuplicates(customers);
        MapMerge(customers);

        return customers;
    }

    private static void MapSearch(RouteGroupBuilder customers)
    {
        customers.MapGet("/", async Task<IResult> (
                HttpContext context,
                CustomerHandler handler,
                ICurrentUser caller,
                CancellationToken cancellationToken,
                string? term = null,
                bool includeDeactivated = false,
                string? cursor = null,
                int limit = CustomerSearchQuery.DefaultLimit) =>
            {
                var result = await handler.SearchAsync(
                    new CustomerSearchQuery(
                        caller.Context.OrganisationId,
                        caller.AssignedBranches,
                        term,
                        includeDeactivated,
                        cursor,
                        limit),
                    cancellationToken);

                if (result.IsFailure)
                {
                    return Problems.From(result.Error, context);
                }

                context.Response.Headers.CacheControl = NoStore;

                return Results.Ok(CustomerPagePayload.From(result.Value));
            })
            .Produces<CustomerPagePayload>(StatusCodes.Status200OK)
            .WithName("SearchCustomers")
            .WithSummary("Find a customer by name, native name, customer number or telephone number.")
            .WithDescription(
                "Answers across the organisation. A record one of the caller's branches can see comes "
                + "back in full; one it cannot comes back as a masked disambiguation card, which is "
                + "enough to tell two people apart and not enough to be a contact list. A term shorter "
                + "than three characters returns nothing rather than the whole customer list.")
            .RequirePermission(CustomersPermissions.Read, BranchScope.AssignedBranches)
            .RequireRateLimiting(RateLimitPolicyNames.DefaultUser)
            .WithRequestTimeout(RequestTimeoutPolicies.Read);
    }

    private static void MapRegister(RouteGroupBuilder customers)
    {
        customers.MapPost("/", async Task<IResult> (
                RegisterCustomerRequest request,
                HttpContext context,
                CustomerHandler handler,
                ICurrentUser caller,
                CancellationToken cancellationToken) =>
            {
                var details = Details(
                    request?.DisplayName,
                    request?.NativeName,
                    request?.Phone,
                    request?.AlternatePhone,
                    request?.Email,
                    request?.AddressLine,
                    request?.Locality,
                    request?.Postcode,
                    request?.Language);

                if (details.IsFailure)
                {
                    return Problems.From(details.Error, context);
                }

                var result = await handler.RegisterAsync(
                    new RegisterCustomerCommand(
                        caller.Context.OrganisationId,
                        caller.Context.BranchId ?? Guid.Empty,
                        details.Value,
                        request!.DuplicatesReviewed,
                        Reason: null,
                        caller.UserId),
                    cancellationToken);

                if (result.IsFailure)
                {
                    return Problems.From(result.Error, context);
                }

                if (result.Value.Customer is null)
                {
                    return DuplicatesToReview(context, result.Value.Candidates);
                }

                var created = result.Value.Customer;
                context.Response.SetEntityTag(created.Version);
                context.Response.Headers.CacheControl = NoStore;

                return Results.Created(
                    $"{CustomersEndpoints.GroupPrefix}/{created.CustomerId}",
                    CustomerPayload.From(created, caller.HasPermission(CustomersPermissions.ReadContact)));
            })
            .Produces<CustomerPayload>(StatusCodes.Status201Created)
            .WithName("RegisterCustomer")
            .WithSummary("Create a customer record at the branch the caller is working in.")
            .WithDescription(
                "Refused with 409 and a `candidates` member when an existing record resembles this one "
                + "strongly enough to be worth reading — a shared telephone number, or a matching name "
                + "in the same place. Read them, then either open one or send the request again with "
                + "`duplicatesReviewed` set, which records that a person took the decision.")
            .RequirePermission(CustomersPermissions.Create, BranchScope.AssignedBranches)
            .RequireRateLimiting(RateLimitPolicyNames.Write)
            .Audited(CustomerHandler.RegisteredAction)
            .RequireIdempotency()
            .WithRequestTimeout(RequestTimeoutPolicies.Command);
    }

    private static void MapRead(RouteGroupBuilder customers)
    {
        customers.MapGet("/{customerId:guid}", async Task<IResult> (
                Guid customerId,
                HttpContext context,
                CustomerHandler handler,
                ICurrentUser caller,
                CancellationToken cancellationToken) =>
            {
                var result = await handler.ReadAsync(
                    customerId, caller.Context.OrganisationId, cancellationToken);

                if (result.IsFailure)
                {
                    return Problems.From(result.Error, context);
                }

                context.Response.SetEntityTag(result.Value.Version);
                context.Response.Headers.CacheControl = NoStore;

                return Results.Ok(CustomerPayload.From(
                    result.Value, caller.HasPermission(CustomersPermissions.ReadContact)));
            })
            .Produces<CustomerPayload>(StatusCodes.Status200OK)
            .WithName("GetCustomer")
            .WithSummary("Read one customer record, with the version a correction must be made against.")
            .WithDescription(
                "The contact fields are populated only for a caller holding `customers.read_contact`; "
                + "for everybody else they are null, which is the field-level minimisation "
                + "`docs/nfr/data-classification.md` section 5.2 requires rather than an omission.")
            .RequirePermission(CustomersPermissions.Read, BranchScope.AssignedBranches)
            .TouchesNoBranchOwnedResource(NoBranchResource, Review)
            .RequireRateLimiting(RateLimitPolicyNames.DefaultUser)
            .WithRequestTimeout(RequestTimeoutPolicies.Read);
    }

    private static void MapCorrect(RouteGroupBuilder customers)
    {
        customers.MapPut("/{customerId:guid}", async Task<IResult> (
                Guid customerId,
                CorrectCustomerRequest request,
                HttpContext context,
                CustomerHandler handler,
                ICurrentUser caller,
                CancellationToken cancellationToken) =>
            {
                var details = Details(
                    request?.DisplayName,
                    request?.NativeName,
                    request?.Phone,
                    request?.AlternatePhone,
                    request?.Email,
                    request?.AddressLine,
                    request?.Locality,
                    request?.Postcode,
                    request?.Language);

                if (details.IsFailure)
                {
                    return Problems.From(details.Error, context);
                }

                return await ApplyAsync(
                    customerId,
                    context,
                    handler,
                    caller,
                    token => handler.CorrectAsync(
                        new CorrectCustomerCommand(
                            customerId,
                            caller.Context.OrganisationId,
                            details.Value,
                            request!.Reason,
                            caller.UserId),
                        token),
                    cancellationToken);
            })
            .Produces<CustomerPayload>(StatusCodes.Status200OK)
            .WithName("CorrectCustomer")
            .WithSummary("Correct what a customer record says about the person.")
            .WithDescription(
                "A changed name is kept as an alias, so a customer who married last year is still found "
                + "under the name on her old receipts. The trail records which fields changed and never "
                + "what they changed to.")
            .RequirePermission(CustomersPermissions.Update, BranchScope.AssignedBranches)
            .TouchesNoBranchOwnedResource(NoBranchResource, Review)
            .RequireRateLimiting(RateLimitPolicyNames.Write)
            .Audited(CustomerHandler.CorrectedAction, reasonRequired: true)
            .RequireIdempotency()
            .RequireIfMatch()
            .WithRequestTimeout(RequestTimeoutPolicies.Command);
    }

    private static void MapStatusCommands(RouteGroupBuilder customers)
    {
        foreach (var (segment, operationId, action, summary, description) in StatusCommands)
        {
            var command = segment;

            customers.MapPost($"/{{customerId:guid}}/{command}", async Task<IResult> (
                    Guid customerId,
                    CustomerReasonRequest request,
                    HttpContext context,
                    CustomerHandler handler,
                    ICurrentUser caller,
                    CancellationToken cancellationToken) =>
                    await ApplyAsync(
                        customerId,
                        context,
                        handler,
                        caller,
                        token => string.Equals(command, "deactivate", StringComparison.Ordinal)
                            ? handler.DeactivateAsync(
                                customerId,
                                caller.Context.OrganisationId,
                                request?.Reason,
                                caller.UserId,
                                token)
                            : handler.ReactivateAsync(
                                customerId,
                                caller.Context.OrganisationId,
                                request?.Reason,
                                caller.UserId,
                                token),
                        cancellationToken))
                .Produces<CustomerPayload>(StatusCodes.Status200OK)
                .WithName(operationId)
                .WithSummary(summary)
                .WithDescription(description)
                .RequirePermission(CustomersPermissions.Deactivate, BranchScope.AssignedBranches)
                .TouchesNoBranchOwnedResource(NoBranchResource, Review)
                .RequireRateLimiting(RateLimitPolicyNames.Write)
                .Audited(action, reasonRequired: true)
                .RequireIdempotency()
                .RequireIfMatch()
                .WithRequestTimeout(RequestTimeoutPolicies.Command);
        }
    }

    private static void MapOpenAtBranch(RouteGroupBuilder customers)
    {
        customers.MapPost("/{customerId:guid}/open", async Task<IResult> (
                Guid customerId,
                HttpContext context,
                CustomerHandler handler,
                ICurrentUser caller,
                CancellationToken cancellationToken) =>
            {
                var result = await handler.OpenAtBranchAsync(
                    customerId,
                    caller.Context.OrganisationId,
                    caller.Context.BranchId ?? Guid.Empty,
                    caller.UserId,
                    cancellationToken);

                if (result.IsFailure)
                {
                    return Problems.From(result.Error, context);
                }

                context.Response.SetEntityTag(result.Value.Version);
                context.Response.Headers.CacheControl = NoStore;

                return Results.Ok(CustomerPayload.From(
                    result.Value, caller.HasPermission(CustomersPermissions.ReadContact)));
            })
            .Produces<CustomerPayload>(StatusCodes.Status200OK)
            .WithName("OpenCustomerAtBranch")
            .WithSummary("Record that the caller's branch has begun serving this customer.")
            .WithDescription(
                "Adds the caller's branch to the record's visibility, so the record appears in that "
                + "branch's ordinary search results from now on, and writes the cross-branch entry "
                + "`branch-scenarios.md` section 3.1 requires — actor, branch and correlation. Opening a "
                + "record the branch already sees changes nothing and writes no entry. There is no "
                + "`If-Match`: Reception opens from a search card, which carries no version because it "
                + "is not the record.")
            .RequirePermission(CustomersPermissions.Read, BranchScope.AssignedBranches)
            .TouchesNoBranchOwnedResource(NoBranchResource, Review)
            .RequireRateLimiting(RateLimitPolicyNames.Write)
            .Audited(CustomerHandler.OpenedAtBranchAction)
            .RequireIdempotency()
            .WithRequestTimeout(RequestTimeoutPolicies.Command);
    }

    private static void MapDuplicates(RouteGroupBuilder customers)
    {
        customers.MapGet("/{customerId:guid}/duplicates", async Task<IResult> (
                Guid customerId,
                HttpContext context,
                CustomerHandler handler,
                ICurrentUser caller,
                CancellationToken cancellationToken) =>
            {
                var result = await handler.DuplicatesAsync(
                    customerId,
                    caller.Context.OrganisationId,
                    caller.AssignedBranches,
                    cancellationToken);

                if (result.IsFailure)
                {
                    return Problems.From(result.Error, context);
                }

                context.Response.Headers.CacheControl = NoStore;

                return Results.Ok(DuplicateReviewPayload.From(result.Value));
            })
            .Produces<DuplicateReviewPayload>(StatusCodes.Status200OK)
            .WithName("GetCustomerDuplicates")
            .WithSummary("List the records that may be the same person as this one.")
            .WithDescription(
                "The screen a merge is decided from. It scores the records as they stand rather than "
                + "reading back the suspicions raised when either was created, because a correction to "
                + "either can create a resemblance or remove one, and a merge is too final to take on "
                + "a score somebody computed months ago. Cards are masked exactly as they are in "
                + "search, and a record that has already been merged is never offered. Gated on "
                + "`customers.read` rather than `customers.merge`: reading who might be a duplicate is "
                + "what Reception does before asking a manager to merge, and demanding the merge "
                + "permission to look would mean nobody could prepare the decision.")
            .RequirePermission(CustomersPermissions.Read, BranchScope.AssignedBranches)
            .TouchesNoBranchOwnedResource(NoBranchResource, Review)
            .RequireRateLimiting(RateLimitPolicyNames.DefaultUser)
            .WithRequestTimeout(RequestTimeoutPolicies.Read);
    }

    private static void MapMerge(RouteGroupBuilder customers)
    {
        customers.MapPost("/{customerId:guid}/merge", async Task<IResult> (
                Guid customerId,
                MergeCustomerRequest request,
                HttpContext context,
                CustomerHandler handler,
                ICurrentUser caller,
                CancellationToken cancellationToken) =>
            {
                // Read the survivor first: a caller who cannot reach it hears "not found" before
                // anything is locked, and the If-Match has a version to be compared against. The
                // handler checks the precondition a second time inside the lock, against the row the
                // merge will actually change.
                var current = await handler.ReadAsync(
                    customerId, caller.Context.OrganisationId, cancellationToken);

                if (current.IsFailure)
                {
                    return Problems.From(current.Error, context);
                }

                var precondition = ConcurrencyResults.CheckIfMatch(
                    context,
                    current.Value.Version,
                    CustomerRequests.VersionConflict,
                    CustomerRequests.VersionConflictDetail);

                if (precondition is not null)
                {
                    return precondition;
                }

                // Read as a concrete version, never as an If-Match value: the wildcard that header
                // parsing accepts would make this precondition match anything at all.
                if (request is null || !request.TryReadMergedCustomerVersion(out var mergedVersion))
                {
                    return Problems.From(CustomersErrors.Required("mergedCustomerVersion"), context);
                }

                var result = await handler.MergeAsync(
                    new MergeCustomersCommand(
                        customerId,
                        request.MergedCustomerId,
                        caller.Context.OrganisationId,
                        caller.Context.BranchId,
                        current.Value.Version,
                        mergedVersion,
                        request.Reason,
                        caller.UserId),
                    cancellationToken);

                if (result.IsFailure)
                {
                    if (result.Error == CustomersErrors.MergedRecordChanged)
                    {
                        return await MergedRecordChangedAsync(
                            request.MergedCustomerId, context, handler, caller, cancellationToken);
                    }

                    return result.Error == CustomersErrors.ConcurrentChange
                        ? ConcurrencyResults.VersionConflict(
                            context,
                            CustomerRequests.VersionConflict,
                            CustomerRequests.VersionConflictDetail,
                            current.Value.Version)
                        : Problems.From(result.Error, context);
                }

                context.Response.SetEntityTag(result.Value.Survivor.Version);
                context.Response.Headers.CacheControl = NoStore;

                return Results.Ok(CustomerMergePayload.From(
                    result.Value, caller.HasPermission(CustomersPermissions.ReadContact)));
            })
            .Produces<CustomerMergePayload>(StatusCodes.Status200OK)
            .WithName("MergeCustomers")
            .WithSummary("Fold one customer record into another. This cannot be undone.")
            .WithDescription(
                "The remedy for exception EX-01, and the only irreversible operation on a customer "
                + "record. The record in the path survives; the one in the body is folded into it, "
                + "keeps its number searchable as an alias on the survivor, and is withdrawn from "
                + "ordinary use. Every branch that could see it can now see the survivor. Measurements "
                + "and orders are re-pointed by `customers.customer-merged.v1`; a snapshot already "
                + "frozen onto an invoice or a job card is never rewritten. Needs a reason and a fresh "
                + "re-authentication, and there is no un-merge.\n\n"
                + "Both records are preconditions. `If-Match` carries the survivor's version and "
                + "`mergedCustomerVersion` carries the folded record's, because what a manager "
                + "approves is a *pair*: if either has been corrected since they were read, the pair "
                + "being merged is not the pair that was approved, and the answer is 409 rather than "
                + "an irreversible merge of something nobody looked at. `mergedCustomerVersion` is a "
                + "concrete version and never an `If-Match` value: `*` is refused, because there is "
                + "no such thing as \"any version\" of a record somebody approved destroying.\n\n"
                + "The two halves refuse differently, because they send the caller to different "
                + "records. A stale survivor is `customers.version-conflict`, carrying "
                + "`currentVersion` and an `ETag`. A stale record being folded in is "
                + "`customers.merged-record-changed`, carrying `mergedCustomerVersion` — the "
                + "value to resend in that field — and no `ETag`, since an `ETag` would describe "
                + "the survivor, which is not what changed.")
            .RequirePermission(CustomersPermissions.Merge, BranchScope.AssignedBranches)
            .RequireStepUp()
            .TouchesNoBranchOwnedResource(NoBranchResource, Review)
            .RequireRateLimiting(RateLimitPolicyNames.Write)
            .Audited(CustomerHandler.MergedAction, reasonRequired: true)
            .RequireIdempotency()
            .RequireIfMatch()
            .WithRequestTimeout(RequestTimeoutPolicies.Command);
    }

    /// <summary>
    /// Answers a merge whose <em>other</em> half went stale.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Deliberately not <see cref="ConcurrencyResults.VersionConflict"/>. That helper is the answer for
    /// the resource the request is addressed to: it puts the current version in <c>currentVersion</c>
    /// and stamps the response's <c>ETag</c>. Both would be the survivor here, and the survivor is not
    /// what changed — a client following them would re-read the survivor, find the version it already
    /// holds, and have nothing to show the person.
    /// </para>
    /// <para>
    /// So the current version of the record being folded in travels under the name of the field it has
    /// to be resent in, and the response carries no <c>ETag</c>, because an <c>ETag</c> describes the
    /// resource in the request URI and this one is about a different record.
    /// </para>
    /// </remarks>
    private static async Task<IResult> MergedRecordChangedAsync(
        Guid mergedCustomerId,
        HttpContext context,
        CustomerHandler handler,
        ICurrentUser caller,
        CancellationToken cancellationToken)
    {
        var merged = await handler.ReadAsync(
            mergedCustomerId, caller.Context.OrganisationId, cancellationToken);

        var extensions = new Dictionary<string, object?>(StringComparer.Ordinal);

        // It was readable a moment ago, inside the merge. If it somehow is not now, the refusal still
        // stands and simply cannot say what to resend.
        if (merged.IsSuccess)
        {
            extensions["mergedCustomerVersion"] = merged.Value.Version.Version;
        }

        return ProblemResults.From(
            context,
            StatusCodes.Status409Conflict,
            CustomersErrors.MergedRecordChanged.Code,
            "That record changed since you opened it",
            "The record you are folding in was corrected while the pair was on your screen. Open it "
            + "again, check the two are still the same person, and merge again. A merge cannot be "
            + "undone, so it is only ever made against records as they read now.",
            retryable: false,
            extensions: extensions);
    }

    /// <summary>
    /// Answers an attempt to create a record that resembles ones already held.
    /// </summary>
    /// <remarks>
    /// The candidates travel as a member of the problem body rather than as a separate success shape,
    /// because the request was refused: a client that treated a 2xx as "created" would show a customer
    /// number that does not exist. RFC 9457 extension members are exactly the mechanism for a refusal
    /// that carries the data needed to answer it.
    /// </remarks>
    private static IResult DuplicatesToReview(
        HttpContext context,
        IReadOnlyList<DuplicateCandidate> candidates)
        => ProblemResults.From(
            context,
            StatusCodes.Status409Conflict,
            CustomersErrors.DuplicatesNotReviewed.Code,
            "This may be somebody we already know",
            CustomersErrors.DuplicatesNotReviewed.Message,
            retryable: false,
            extensions: new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["candidates"] = candidates.Select(DuplicateCandidatePayload.From).ToArray(),
            });

    private static Result<CustomerDetails> Details(
        string? displayName,
        string? nativeName,
        string? phone,
        string? alternatePhone,
        string? email,
        string? addressLine,
        string? locality,
        string? postcode,
        string? language)
        => CustomerDetails.Create(
            displayName, nativeName, phone, alternatePhone, email, addressLine, locality, postcode,
            language);

    /// <summary>
    /// Reads the record, evaluates the precondition against it, applies the change and answers.
    /// </summary>
    /// <remarks>
    /// The precondition is evaluated before the change is attempted, which is what RFC 9110 requires
    /// and what makes 409 mean "you were looking at an older version" rather than "the write raced".
    /// The second check, on <see cref="CustomersErrors.ConcurrentChange"/>, catches the race that is
    /// left: two requests that both passed the precondition and reached the database together.
    /// </remarks>
    private static async Task<IResult> ApplyAsync(
        Guid customerId,
        HttpContext context,
        CustomerHandler handler,
        ICurrentUser caller,
        Func<CancellationToken, Task<Result<AdministeredCustomer>>> apply,
        CancellationToken cancellationToken)
    {
        var current = await handler.ReadAsync(
            customerId, caller.Context.OrganisationId, cancellationToken);

        if (current.IsFailure)
        {
            return Problems.From(current.Error, context);
        }

        var precondition = ConcurrencyResults.CheckIfMatch(
            context,
            current.Value.Version,
            CustomerRequests.VersionConflict,
            CustomerRequests.VersionConflictDetail);

        if (precondition is not null)
        {
            return precondition;
        }

        var result = await apply(cancellationToken);

        if (result.IsFailure)
        {
            return result.Error == CustomersErrors.ConcurrentChange
                ? ConcurrencyResults.VersionConflict(
                    context,
                    CustomerRequests.VersionConflict,
                    CustomerRequests.VersionConflictDetail,
                    current.Value.Version)
                : Problems.From(result.Error, context);
        }

        context.Response.SetEntityTag(result.Value.Version);
        context.Response.Headers.CacheControl = NoStore;

        return Results.Ok(CustomerPayload.From(
            result.Value, caller.HasPermission(CustomersPermissions.ReadContact)));
    }

    /// <summary>
    /// The two commands that move a record between statuses.
    /// </summary>
    /// <remarks>
    /// Both are gated on <c>customers.deactivate</c>, following the precedent Identity set for suspend
    /// and reinstate: the permission to withdraw something is the permission to put it back, because
    /// splitting them produces the state nobody can leave — a record somebody withdrew and nobody
    /// present can restore.
    /// </remarks>
    private static IReadOnlyList<(
        string Segment,
        string OperationId,
        string Action,
        string Summary,
        string Description)>
        StatusCommands
    { get; } =
    [
        ("deactivate", "DeactivateCustomer", CustomerHandler.DeactivatedAction,
            "Withdraw a customer record from ordinary use.",
            "The record stays readable and its history stays resolvable — this is not a deletion, and "
            + "an order placed last year still names the person who placed it. What changes is that a "
            + "search no longer offers the record when somebody starts a new order."),
        ("reactivate", "ReactivateCustomer", CustomerHandler.ReactivatedAction,
            "Return a withdrawn customer record to ordinary use.",
            "The record appears in ordinary search results again. Gated on the same permission as "
            + "withdrawing it, so a record cannot be put beyond the reach of everybody present."),
    ];
}
