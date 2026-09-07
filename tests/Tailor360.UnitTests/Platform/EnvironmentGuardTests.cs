using Shouldly;
using Tailor360.Cli.Commands;

namespace Tailor360.UnitTests.Platform;

/// <summary>
/// The refusal that keeps synthetic data out of production.
/// </summary>
/// <remarks>
/// <para>
/// CLAUDE.md section 4 rule 10 is "synthetic data only outside production", and the seeding command is
/// the one place in the system that could break it in a single keystroke: fixed identifiers and test
/// identities written into live records cannot be distinguished afterwards from real ones. The guard is
/// deliberately unconditional — there is no override switch — and these assert that the refusal is
/// there, says why, and exits with a code a deployment script can branch on rather than a bare 1.
/// </para>
/// <para>
/// <see cref="EnvironmentGuard.IsProduction"/> is not asserted here: it reads process environment
/// variables through <c>CliHost.ResolveEnvironmentName</c>, and a unit test that set them would be
/// racing every other test in the assembly. What it resolves to is covered where the command is run.
/// </para>
/// </remarks>
[Trait("Category", "Unit")]
public sealed class EnvironmentGuardTests
{
    [Fact]
    public void TheRefusalExitsWithItsOwnCodeRatherThanAGenericFailure()
    {
        using var error = new StringWriter();

        var exitCode = EnvironmentGuard.RefuseSyntheticDataInProduction(error);

        exitCode.ShouldBe(ExitCodes.RefusedInProduction);
        exitCode.ShouldNotBe(ExitCodes.Failure, "a script has to tell this refusal from a crash");
        exitCode.ShouldNotBe(ExitCodes.Success);
    }

    /// <summary>
    /// The message has to answer the question the operator will ask next — "how do I force it?" — with
    /// "you cannot, and here is what you wanted instead". A refusal that only says no gets worked
    /// around.
    /// </summary>
    [Fact]
    public void TheRefusalSaysThereIsNoOverrideAndNamesTheCommandToUseInstead()
    {
        using var error = new StringWriter();

        EnvironmentGuard.RefuseSyntheticDataInProduction(error);

        var message = error.ToString();
        message.ShouldContain("Production");
        message.ShouldContain("no override");
        message.ShouldContain("init-reference-data");
    }

    [Fact]
    public void TheRefusalIsWrittenToTheErrorStreamThatWasGivenToIt()
    {
        using var error = new StringWriter();

        EnvironmentGuard.RefuseSyntheticDataInProduction(error);

        error.ToString().ShouldNotBeNullOrWhiteSpace();
    }

    [Fact]
    public void TheExitCodesAreDistinct()
    {
        int[] codes =
        [
            ExitCodes.Success,
            ExitCodes.Failure,
            ExitCodes.RefusedInProduction,
            ExitCodes.WrongDatabaseRole,
        ];

        codes.Distinct().Count().ShouldBe(codes.Length, "a script branches on these");
    }

    [Fact]
    public void TheCurrentEnvironmentIsAlwaysNamed()
        => EnvironmentGuard.CurrentEnvironment.ShouldNotBeNullOrWhiteSpace();
}
