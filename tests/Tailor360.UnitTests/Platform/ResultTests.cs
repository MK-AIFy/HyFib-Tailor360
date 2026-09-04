using Shouldly;
using Tailor360.Platform.Abstractions.Results;

namespace Tailor360.UnitTests.Platform;

/// <summary>The result type application services return instead of throwing for expected failures.</summary>
[Trait("Category", "Unit")]
public sealed class ResultTests
{
    [Fact]
    public void SuccessCarriesNoError()
    {
        var result = Result.Success();

        result.IsSuccess.ShouldBeTrue();
        result.IsFailure.ShouldBeFalse();
        result.Error.ShouldBe(Error.None);
    }

    [Fact]
    public void FailureCarriesTheError()
    {
        var error = Error.Conflict("orders.job.already_dispatched", "The job has already been dispatched.");

        var result = Result.Failure(error);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("orders.job.already_dispatched");
        result.Error.Type.ShouldBe(ErrorType.Conflict);
    }

    [Fact]
    public void ReadingTheValueOfAFailureThrowsRatherThanReturningDefault()
    {
        var result = Result.Failure<string>(Error.NotFound("customers.not_found", "No such customer."));

        // Returning default here would let a caller that forgot to check the result silently proceed
        // with a null, which is exactly the class of defect the type exists to prevent.
        Should.Throw<InvalidOperationException>(() => result.Value);
    }

    [Fact]
    public void TryGetValueReadsASuccessWithoutThrowing()
    {
        Result.Success("stitched").TryGetValue(out var value).ShouldBeTrue();
        value.ShouldBe("stitched");

        Result.Failure<string>(Error.Unavailable("x", "y")).TryGetValue(out var missing).ShouldBeFalse();
        missing.ShouldBeNull();
    }

    [Fact]
    public void AValueConvertsImplicitlyToASuccess()
    {
        Result<int> result = 42;

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldBe(42);
    }

    [Fact]
    public void ErrorFactoriesSetTheTransportClassification()
    {
        Error.Validation("a", "b").Type.ShouldBe(ErrorType.Validation);
        Error.NotFound("a", "b").Type.ShouldBe(ErrorType.NotFound);
        Error.Forbidden("a", "b").Type.ShouldBe(ErrorType.Forbidden);
        Error.PreconditionFailed("a", "b").Type.ShouldBe(ErrorType.PreconditionFailed);
        Error.Unavailable("a", "b").Type.ShouldBe(ErrorType.Unavailable);
    }
}
