using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using Tailor360.Platform.Abstractions.Multitenancy;
using Tailor360.Platform.Security.Authorisation;

namespace Tailor360.UnitTests.Platform;

/// <summary>
/// What a refused request is actually told.
/// </summary>
/// <remarks>
/// A status code alone cannot carry the difference between "answer your second factor", "confirm it is
/// you again" and "this account may never do that", and a client that guesses will offer the wrong
/// dialog. These assert the stable codes the client keys on — and the one place the answer is
/// deliberately blunt, where a record in another branch and a record that does not exist are told apart
/// by nobody.
/// </remarks>
[Trait("Category", "Unit")]
public sealed class AuthorisationProblemResponseTests
{
    [Fact]
    public async Task AnswersAStaleStepUpWithACodeTheClientCanOfferReauthenticationFor()
    {
        var (status, code) = await RefuseAsync(AuthorisationRefusal.StepUpRequired);

        status.ShouldBe(StatusCodes.Status403Forbidden);
        code.ShouldBe(AuthorisationProblemResultHandler.StepUpRequiredCode);
    }

    [Fact]
    public async Task AnswersAMissingSecondFactorDifferentlyFromAStaleOne()
    {
        var factor = await RefuseAsync(AuthorisationRefusal.SecondFactorRequired);
        var stepUp = await RefuseAsync(AuthorisationRefusal.StepUpRequired);

        factor.Code.ShouldBe(AuthorisationProblemResultHandler.SecondFactorRequiredCode);
        factor.Code.ShouldNotBe(stepUp.Code);
    }

    [Fact]
    public async Task AnswersAResourceInAnotherBranchAsNotFound()
    {
        // 403 would confirm the record exists. The whole defence against an edited identifier is that
        // the answer carries no information, and a status code is information.
        var (status, code) = await RefuseAsync(AuthorisationRefusal.ResourceUnreachable);

        status.ShouldBe(StatusCodes.Status404NotFound);
        code.ShouldBe(AuthorisationProblemResultHandler.ResourceNotFoundCode);
    }

    [Fact]
    public async Task AnswersSomebodyElsesWorkAsForbiddenAndSaysWhoCanChangeIt()
    {
        var (status, code) = await RefuseAsync(AuthorisationRefusal.NotAssigned);

        status.ShouldBe(StatusCodes.Status403Forbidden);
        code.ShouldBe(AuthorisationProblemResultHandler.NotAssignedCode);
    }

    [Fact]
    public async Task AnswersAnOrdinaryRefusalWithoutNamingWhatWasMissing()
    {
        var (status, code) = await RefuseAsync(AuthorisationRefusal.PermissionNotHeld);

        status.ShouldBe(StatusCodes.Status403Forbidden);
        code.ShouldBe(AuthorisationProblemResultHandler.ForbiddenCode);
    }

    [Fact]
    public async Task AnswersAMisconfiguredPipelineWithoutTellingTheCallerTheHostIsBroken()
    {
        // The operator learns about it from the log; the caller learns nothing that would distinguish
        // a broken deployment from an ordinary refusal, which is one fewer thing to probe for.
        var (status, code) = await RefuseAsync(AuthorisationRefusal.PipelineIncomplete);

        status.ShouldBe(StatusCodes.Status403Forbidden);
        code.ShouldBe(AuthorisationProblemResultHandler.ForbiddenCode);
    }

    [Fact]
    public async Task ReportsTheCoarsestFailureWhenSeveralRequirementsFailAtOnce()
    {
        // A caller who holds neither the permission nor the row is told about the permission. It is the
        // first gate they would have to pass, and it says nothing at all about the row.
        var context = await RefuseAsync(
            AuthorisationRefusal.ResourceUnreachable,
            AuthorisationRefusal.PermissionNotHeld);

        context.Status.ShouldBe(StatusCodes.Status403Forbidden);
        context.Code.ShouldBe(AuthorisationProblemResultHandler.ForbiddenCode);
    }

    [Fact]
    public async Task ChallengesAnUnauthenticatedRequestRatherThanRefusingIt()
    {
        var context = NewContext();

        await Handler().HandleAsync(
            _ => Task.CompletedTask,
            context,
            new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build(),
            PolicyAuthorizationResult.Challenge());

        var (status, code) = await ReadAsync(context);
        status.ShouldBe(StatusCodes.Status401Unauthorized);
        code.ShouldBe(AuthorisationProblemResultHandler.AuthenticationRequiredCode);
    }

    private static async Task<(int Status, string? Code)> RefuseAsync(params AuthorisationRefusal[] refusals)
    {
        var handler = new StubHandler();
        var failure = AuthorizationFailure.Failed(
            [.. refusals.Select(refusal => new RefusalReason(handler, refusal, "test"))]);

        var context = NewContext();

        await Handler().HandleAsync(
            _ => Task.CompletedTask,
            context,
            new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build(),
            PolicyAuthorizationResult.Forbid(failure));

        return await ReadAsync(context);
    }

    private static AuthorisationProblemResultHandler Handler()
        => new(NullLogger<AuthorisationProblemResultHandler>.Instance);

    private static DefaultHttpContext NewContext()
    {
        var services = new ServiceCollection();
        services.AddProblemDetails();
        services.AddLogging();

        return new DefaultHttpContext
        {
            RequestServices = services.BuildServiceProvider(),
            Response = { Body = new MemoryStream() },
        };
    }

    private static async Task<(int Status, string? Code)> ReadAsync(HttpContext context)
    {
        context.Response.Body.Position = 0;
        using var reader = new StreamReader(context.Response.Body);
        var body = await reader.ReadToEndAsync();

        if (body.Length == 0)
        {
            return (context.Response.StatusCode, null);
        }

        using var document = JsonDocument.Parse(body);
        return (context.Response.StatusCode, document.RootElement.GetProperty("code").GetString());
    }

    /// <summary>A handler to attribute a failure reason to; it never evaluates anything.</summary>
    private sealed class StubHandler : IAuthorizationHandler
    {
        public Task HandleAsync(AuthorizationHandlerContext context) => Task.CompletedTask;
    }
}
