using System.Net;
using System.Text.Json;
using Shouldly;
using Tailor360.IntegrationTests.Identity;

namespace Tailor360.IntegrationTests.Billing;

/// <summary>What the Billing route tests share: a branch of the run's own, opened through Identity's own route.</summary>
internal static class BillingHarness
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    /// <summary>
    /// Opens a branch and returns its identifier. Billing checks every branch it is given against Identity's
    /// register, so a test cannot invent one; a branch of the run's own also keeps registrations and price
    /// lists left by earlier runs from overlapping this one's.
    /// </summary>
    /// <param name="client">A client holding <c>admin.branches</c>.</param>
    /// <returns>The branch identifier.</returns>
    public static async Task<Guid> OpenBranchAsync(AdministrationHarness.AdministratorClient client)
        => (await OpenBranchWithCodeAsync(client)).BranchId;

    /// <summary><see cref="OpenBranchAsync"/>, and the code the branch was opened under, which every number drawn at it carries.</summary>
    /// <param name="client">A client holding <c>admin.branches</c>.</param>
    public static async Task<(Guid BranchId, string Code)> OpenBranchWithCodeAsync(AdministrationHarness.AdministratorClient client)
    {
        // A branch code is upper-case letters and digits, at most sixteen of them; a fresh token is one.
        var code = $"B{AdministrationHarness.UniqueToken(12).ToUpperInvariant()}";
        var response = await client.PostAsync(
            "/api/v1/admin/branches/",
            new
            {
                code,
                name = $"Synthetic branch {code}",
                timeZoneId = "Asia/Kolkata",
                addressLine1 = "12 Example Street",
                city = "Madurai",
                state = "Tamil Nadu",
                postalCode = "625001",
                contactPhone = "+91 90000 00000",
                contactEmail = "branch@synthetic.invalid",
                gstRegistrationReference = (string?)null,
                reason = "Opened by an integration test.",
            },
            ("Idempotency-Key", Guid.CreateVersion7().ToString()));
        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync(Token));
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Token));
        return (body.RootElement.GetProperty("branchId").GetGuid(), code);
    }
}
