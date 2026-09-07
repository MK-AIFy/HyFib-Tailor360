using System.ComponentModel.DataAnnotations;
using Shouldly;
using Tailor360.Worker;

namespace Tailor360.UnitTests.Worker;

/// <summary>
/// The bounds on the worker's own configuration.
/// </summary>
/// <remarks>
/// The ranges are not decoration. A heartbeat interval of zero would spin the loop against the database
/// as fast as it can answer; one of an hour would let a wedged instance sit undetected for three hours,
/// because staleness is three intervals. The attributes are what turn either into a start-up failure
/// rather than a production symptom, and they only do that if they are actually there — which is what
/// these assert.
/// </remarks>
[Trait("Category", "Unit")]
public sealed class WorkerOptionsTests
{
    [Fact]
    public void TheDefaultsAreValid()
        => Validate(new WorkerOptions()).ShouldBeEmpty();

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(65536)]
    public void APortOutsideTheRangeIsRefused(int port)
        => Validate(new WorkerOptions { HealthPort = port })
            .ShouldContain(result => result.MemberNames.Contains(nameof(WorkerOptions.HealthPort)));

    /// <summary>
    /// Five seconds is the floor because staleness is three intervals and the readiness probe has to
    /// answer sooner than the orchestrator gives up; five minutes is the ceiling for the same reason
    /// from the other end.
    /// </summary>
    [Theory]
    [InlineData("00:00:01")]
    [InlineData("00:00:00")]
    [InlineData("01:00:00")]
    public void AHeartbeatIntervalOutsideTheRangeIsRefused(string interval)
        => Validate(new WorkerOptions { HeartbeatInterval = TimeSpan.Parse(interval, null) })
            .ShouldContain(result => result.MemberNames.Contains(nameof(WorkerOptions.HeartbeatInterval)));

    [Theory]
    [InlineData("00:00:05")]
    [InlineData("00:00:15")]
    [InlineData("00:05:00")]
    public void AHeartbeatIntervalInsideTheRangeIsAccepted(string interval)
        => Validate(new WorkerOptions { HeartbeatInterval = TimeSpan.Parse(interval, null) })
            .ShouldBeEmpty();

    /// <summary>
    /// The heartbeat row is per instance, so the name is the key. Defaulting it to the machine name is
    /// what makes a deployment that sets nothing still distinguish its instances from each other.
    /// </summary>
    [Fact]
    public void TheInstanceNameDefaultsToSomethingThatIdentifiesTheInstance()
        => new WorkerOptions().InstanceName.ShouldNotBeNullOrWhiteSpace();

    private static List<ValidationResult> Validate(WorkerOptions options)
    {
        var results = new List<ValidationResult>();
        Validator.TryValidateObject(options, new ValidationContext(options), results, validateAllProperties: true);
        return results;
    }
}
