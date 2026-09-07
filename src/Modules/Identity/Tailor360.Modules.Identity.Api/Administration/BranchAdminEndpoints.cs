using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Tailor360.Modules.Identity.Api.Payloads;
using Tailor360.Modules.Identity.Application.Administration;
using Tailor360.Modules.Identity.Domain;
using Tailor360.Modules.Identity.Domain.Branches;
using Tailor360.Platform.Abstractions.Multitenancy;
using Tailor360.Platform.Abstractions.Results;
using Tailor360.Platform.Security.Authorisation;
using Tailor360.Platform.Security.Endpoints;
using Tailor360.Platform.Security.Permissions;

namespace Tailor360.Modules.Identity.Api.Administration;

/// <summary>
/// The branch register: opening a location, describing it, closing it and reopening it.
/// </summary>
/// <remarks>
/// There is no delete, and the code is not editable. Both follow from the same fact: a branch code is
/// embedded in every order, estimate and invoice number it ever produced, and those numbers are on
/// paper customers still hold. A branch that needs a different code is a different branch.
/// </remarks>
public static class BranchAdminEndpoints
{
    private const string Review = "#25, docs/security/permission-matrix.md";

    private const string NoBranchResource =
        "The route names a branch in the register rather than a row owned by one. Administering the "
        + "register is organisation-scoped by definition — an administrator opens and closes the "
        + "branches their organisation operates — so there is no narrower reach for a scope check to "
        + "evaluate, and checking the caller's own branch here would stop them closing anywhere but it.";

    /// <summary>Maps the branch register.</summary>
    /// <param name="branches">The <c>/api/v1/admin/branches</c> group.</param>
    public static RouteGroupBuilder MapBranchAdminEndpoints(this RouteGroupBuilder branches)
    {
        ArgumentNullException.ThrowIfNull(branches);

        branches.MapGet("/", async Task<IResult> (
                BranchAdministrationHandler handler,
                ICurrentUser caller,
                CancellationToken cancellationToken) =>
                Results.Ok((await handler.ListAsync(caller.Context.OrganisationId, cancellationToken))
                    .Select(BranchPayload.From).ToArray()))
            .Produces<IReadOnlyList<BranchPayload>>(StatusCodes.Status200OK)
            .WithName("ListBranches")
            .WithSummary("List the organisation's branches.")
            .WithTags(IdentityRoutes.AdminTag)
            .RequirePermission(IdentityPermissions.Branches, BranchScope.Organisation)
            .RequireStepUp()
            .TouchesNoBranchOwnedResource(NoBranchResource, Review)
            .RequireRateLimiting(RateLimitPolicyNames.DefaultUser);

        branches.MapGet("/{branchId:guid}", async Task<IResult> (
                Guid branchId,
                HttpContext context,
                BranchAdministrationHandler handler,
                CancellationToken cancellationToken) =>
            {
                var result = await handler.ReadAsync(branchId, cancellationToken);

                if (result.IsFailure)
                {
                    return Problems.From(result.Error, context);
                }

                context.Response.SetEntityTag(result.Value.Version);

                return Results.Ok(BranchPayload.From(result.Value));
            })
            .Produces<BranchPayload>(StatusCodes.Status200OK)
            .WithName("GetBranch")
            .WithSummary("Read one branch, with the version an edit must be made against.")
            .WithTags(IdentityRoutes.AdminTag)
            .RequirePermission(IdentityPermissions.Branches, BranchScope.Organisation)
            .RequireStepUp()
            .TouchesNoBranchOwnedResource(NoBranchResource, Review)
            .RequireRateLimiting(RateLimitPolicyNames.DefaultUser);

        branches.MapPost("/", async Task<IResult> (
                OpenBranchPayload request,
                HttpContext context,
                BranchAdministrationHandler handler,
                ICurrentUser caller,
                CancellationToken cancellationToken) =>
            {
                if (Reason(request?.Reason, context) is { Refusal: { } refusal })
                {
                    return refusal;
                }

                var result = await handler.OpenAsync(
                    caller.Context.OrganisationId,
                    request!.Code,
                    request.Details(),
                    request.Reason!.Trim(),
                    caller.UserId,
                    cancellationToken);

                if (result.IsFailure)
                {
                    return Problems.From(result.Error, context);
                }

                context.Response.SetEntityTag(result.Value.Version);

                return Results.Created(
                    $"{IdentityRoutes.AdminBranches}/{result.Value.BranchId}",
                    BranchPayload.From(result.Value));
            })
            .Produces<BranchPayload>(StatusCodes.Status201Created)
            .WithName("OpenBranch")
            .WithSummary("Open a branch.")
            .WithTags(IdentityRoutes.AdminTag)
            .RequirePermission(IdentityPermissions.Branches, BranchScope.Organisation)
            .RequireStepUp()
            .TouchesNoBranchOwnedResource(NoBranchResource, Review)
            .RequireRateLimiting(RateLimitPolicyNames.Write)
            .Audited(BranchAdministrationHandler.OpenedAction, reasonRequired: true)
            .RequireIdempotency()
            .WithRequestTimeout(RequestTimeoutPolicies.Command);

        branches.MapPut("/{branchId:guid}", async Task<IResult> (
                Guid branchId,
                ReconfigureBranchPayload request,
                HttpContext context,
                BranchAdministrationHandler handler,
                ICurrentUser caller,
                CancellationToken cancellationToken) =>
                await ApplyAsync(
                    branchId,
                    request?.Reason,
                    context,
                    handler,
                    (reason, token) => handler.ReconfigureAsync(
                        branchId, request!.Details(), reason, caller.UserId, token),
                    cancellationToken))
            .Produces<BranchPayload>(StatusCodes.Status200OK)
            .WithName("ReconfigureBranch")
            .WithSummary("Change a branch's name, timezone, address and contacts.")
            .WithTags(IdentityRoutes.AdminTag)
            .RequirePermission(IdentityPermissions.Branches, BranchScope.Organisation)
            .RequireStepUp()
            .TouchesNoBranchOwnedResource(NoBranchResource, Review)
            .RequireRateLimiting(RateLimitPolicyNames.Write)
            .Audited(BranchAdministrationHandler.ReconfiguredAction, reasonRequired: true)
            .RequireIdempotency()
            .RequireIfMatch()
            .WithRequestTimeout(RequestTimeoutPolicies.Command);

        foreach (var (segment, operationId, action, summary, apply) in Commands)
        {
            branches.MapPost($"/{{branchId:guid}}/{segment}", async Task<IResult> (
                    Guid branchId,
                    ReasonPayload request,
                    HttpContext context,
                    BranchAdministrationHandler handler,
                    ICurrentUser caller,
                    CancellationToken cancellationToken) =>
                    await ApplyAsync(
                        branchId,
                        request?.Reason,
                        context,
                        handler,
                        (reason, token) => apply(handler, branchId, reason, caller.UserId, token),
                        cancellationToken))
                .Produces<BranchPayload>(StatusCodes.Status200OK)
                .WithName(operationId)
                .WithSummary(summary)
                .WithTags(IdentityRoutes.AdminTag)
                .RequirePermission(IdentityPermissions.Branches, BranchScope.Organisation)
                .RequireStepUp()
                .TouchesNoBranchOwnedResource(NoBranchResource, Review)
                .RequireRateLimiting(RateLimitPolicyNames.Write)
                .Audited(action, reasonRequired: true)
                .RequireIdempotency()
                .RequireIfMatch()
                .WithRequestTimeout(RequestTimeoutPolicies.Command);
        }

        return branches;
    }

    private static (IResult? Refusal, string Reason) Reason(string? given, HttpContext context)
    {
        if (given is null || string.IsNullOrWhiteSpace(given))
        {
            return (Problems.From(IdentityApiErrors.ReasonRequired, context), string.Empty);
        }

        var trimmed = given.Trim();

        return trimmed.Length > AdminRequests.MaximumReasonLength
            ? (Problems.From(IdentityApiErrors.ReasonTooLong, context), string.Empty)
            : (null, trimmed);
    }

    private static async Task<IResult> ApplyAsync(
        Guid branchId,
        string? given,
        HttpContext context,
        BranchAdministrationHandler handler,
        Func<string, CancellationToken, Task<Result<AdministeredBranch>>> apply,
        CancellationToken cancellationToken)
    {
        var (refusal, reason) = Reason(given, context);
        if (refusal is not null)
        {
            return refusal;
        }

        var current = await handler.ReadAsync(branchId, cancellationToken);
        if (current.IsFailure)
        {
            return Problems.From(current.Error, context);
        }

        var precondition = ConcurrencyResults.CheckIfMatch(
            context,
            current.Value.Version,
            AdminRequests.VersionConflict,
            AdminRequests.VersionConflictDetail);

        if (precondition is not null)
        {
            return precondition;
        }

        var result = await apply(reason, cancellationToken);

        if (result.IsFailure)
        {
            return result.Error == IdentityErrors.ConcurrentChange
                ? ConcurrencyResults.VersionConflict(
                    context,
                    AdminRequests.VersionConflict,
                    AdminRequests.VersionConflictDetail,
                    current.Value.Version)
                : Problems.From(result.Error, context);
        }

        context.Response.SetEntityTag(result.Value.Version);

        return Results.Ok(BranchPayload.From(result.Value));
    }

    private static IReadOnlyList<(
        string Segment,
        string OperationId,
        string Action,
        string Summary,
        Func<BranchAdministrationHandler, Guid, string, Guid, CancellationToken, Task<Result<AdministeredBranch>>> Apply)>
        Commands
    { get; } =
    [
        ("close", "CloseBranch", BranchAdministrationHandler.ClosedAction,
            "Close a branch. Refused while anybody still works there.",
            (handler, id, reason, actor, token) => handler.CloseAsync(id, reason, actor, token)),
        ("reopen", "ReopenBranch", BranchAdministrationHandler.ReopenedAction,
            "Reopen a closed branch.",
            (handler, id, reason, actor, token) => handler.ReopenAsync(id, reason, actor, token)),
    ];
}
