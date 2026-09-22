using System.Globalization;
using System.Text.RegularExpressions;
using Shouldly;
using Tailor360.Modules.Customers.Application.Abstractions;

namespace Tailor360.ContractTests;

/// <summary>
/// The shortest customer search the server will run, and the client's idea of the same number.
/// </summary>
/// <remarks>
/// <para>
/// The minimum is written twice — once in the Customers module, once in the client's
/// <c>customersApi.ts</c> — and the second cannot be generated from the first. It is not carried by
/// the OpenAPI document in a form the generated types keep: <c>minLength</c> is a validation
/// keyword, and <c>openapi-typescript</c> renders the parameter as <c>string</c> either way. So the
/// tie has to be asserted rather than derived, and this is where it is asserted.
/// </para>
/// <para>
/// Without it the pair drifts silently, and the drift is invisible at both ends. A server that
/// raised its minimum to four would leave the client sending three-character terms and rendering the
/// refusal as an unexpected error; a client that raised its own would refuse searches the server
/// would happily have run. Neither breaks a build, and neither looks like a defect from the counter
/// — it looks like the customer not being there.
/// </para>
/// <para>
/// In the contract tier because this is where the promises the client is entitled to rely on are
/// kept, and because it needs no database.
/// </para>
/// </remarks>
[Trait("Category", "Contract")]
public sealed class CustomerSearchMinimumTests
{
    private static readonly string ClientApiFile = Path.Combine(
        RepositoryFiles.Root, "clients", "pwa", "src", "customers", "customersApi.ts");

    [Fact]
    public void TheClientAndTheServerAgreeOnTheShortestSearch()
    {
        File.Exists(ClientApiFile).ShouldBeTrue(
            $"The client's customer API module was expected at {ClientApiFile}. If it moved, move "
            + "this test's path with it rather than deleting the test: the number it guards is "
            + "still written in two places.");

        var source = File.ReadAllText(ClientApiFile);
        var match = Regex.Match(
            source,
            @"CUSTOMER_SEARCH_MINIMUM_LENGTH\s*=\s*(?<value>\d+)",
            RegexOptions.None,
            TimeSpan.FromSeconds(5));

        match.Success.ShouldBeTrue(
            "CUSTOMER_SEARCH_MINIMUM_LENGTH was not found in the client's customersApi.ts. The "
            + "client states the minimum it pre-checks against so that this test can hold it equal "
            + "to the server's; if the constant was renamed, rename it here too.");

        var client = int.Parse(match.Groups["value"].Value, CultureInfo.InvariantCulture);

        client.ShouldBe(
            CustomerSearchQuery.MinimumTermLength,
            $"The client refuses a search shorter than {client} characters and the server refuses "
            + $"one shorter than {CustomerSearchQuery.MinimumTermLength}. Whichever moved, move the "
            + "other: while they disagree, one of them is refusing a search the other would have "
            + "run, and at the counter that reads as the customer not existing.");
    }
}
