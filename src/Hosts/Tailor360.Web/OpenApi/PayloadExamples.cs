using System.Text.Json.Nodes;

namespace Tailor360.Web.OpenApi;

/// <summary>
/// One request example per operation that takes a body, keyed by operation identifier.
/// </summary>
/// <remarks>
/// <para>
/// An example is the difference between a schema a reader can parse and a request a reader can send. It
/// is also what makes a generated client's tests and a reviewer's <c>curl</c> agree with the server, so
/// the lint fails an operation that accepts a body and offers no example.
/// </para>
/// <para>
/// They are keyed by operation identifier rather than by payload type on purpose: the examples are part
/// of the published documentation, which the backend-for-frontend owns, and keying on the type would
/// oblige the host to name a payload class from every module it composes.
/// </para>
/// <para>
/// <b>Every value here is invented.</b> Nothing in this file is a real address, a real name, a real
/// credential or a real device response, and nothing in it may become one: the file is published in the
/// API document and committed to the repository.
/// </para>
/// </remarks>
public static class PayloadExamples
{
    private static readonly Dictionary<string, string> Examples = new(StringComparer.Ordinal)
    {
        ["SuspendStaffUser"] = """
            {
              "reason": "Left the company on 5 September; access withdrawn at the manager's request."
            }
            """,

        ["ReinstateStaffUser"] = """
            {
              "reason": "Returned from unpaid leave; the manager confirmed the start date."
            }
            """,

        ["DeactivateStaffUser"] = """
            {
              "reason": "Resigned; last working day was 5 September."
            }
            """,

        ["ReactivateStaffUser"] = """
            {
              "reason": "Rejoined the shop; identity confirmed in person by the branch manager."
            }
            """,

        ["ResetStaffUserMfa"] = """
            {
              "reason": "Lost the phone holding the authenticator; identity confirmed in person."
            }
            """,

        ["RevokeStaffUserSessions"] = """
            {
              "reason": "Tablet left on a bus; signing every device out while it is recovered."
            }
            """,

        ["ReplaceStaffUserRoles"] = """
            {
              "roleKeys": ["tailor", "tailor_master"],
              "reason": "Promoted to master tailor; approved by the branch manager."
            }
            """,

        ["ReplaceStaffUserBranches"] = """
            {
              "branches": [
                { "branchId": "0199c000-0000-7000-8000-00000000000a", "isPrimary": true },
                { "branchId": "0199c000-0000-7000-8000-00000000000b", "isPrimary": false }
              ],
              "reason": "Covering the second branch two days a week from October."
            }
            """,

        ["InviteStaffUser"] = """
            {
              "userName": "priya.counter",
              "email": "priya.counter@synthetic.invalid",
              "displayName": "Priya R",
              "homeBranchId": "0199c000-0000-7000-8000-00000000000a",
              "reason": "Joining the counter team on 15 September; approved by the branch manager."
            }
            """,

        ["OpenBranch"] = """
            {
              "code": "MADURAI1",
              "name": "Madurai Main",
              "timeZoneId": "Asia/Kolkata",
              "addressLine1": "12 Example Street",
              "addressLine2": "Near the bus stand",
              "city": "Madurai",
              "state": "Tamil Nadu",
              "postalCode": "625001",
              "contactPhone": "+91 90000 00000",
              "contactEmail": "madurai@synthetic.invalid",
              "gstRegistrationReference": "GSTIN-EXAMPLE-0001",
              "reason": "Second location opening on 1 October."
            }
            """,

        ["ReconfigureBranch"] = """
            {
              "name": "Madurai Main",
              "timeZoneId": "Asia/Kolkata",
              "addressLine1": "14 Example Street",
              "addressLine2": "Near the bus stand",
              "city": "Madurai",
              "state": "Tamil Nadu",
              "postalCode": "625001",
              "contactPhone": "+91 90000 00001",
              "contactEmail": "madurai@synthetic.invalid",
              "gstRegistrationReference": "GSTIN-EXAMPLE-0001",
              "reason": "Moved two doors down; address and telephone updated."
            }
            """,

        ["CloseBranch"] = """
            {
              "reason": "Lease ended on 30 September; the counter has moved to Madurai Main."
            }
            """,

        ["ReopenBranch"] = """
            {
              "reason": "Reopening after the refit, from 1 December."
            }
            """,

        ["SetFeatureFlag"] = """
            {
              "enabled": true,
              "reason": "Enabling the new measurement sheet for the pilot branch trial."
            }
            """,

        ["SetModuleEnabled"] = """
            {
              "enabled": false,
              "reason": "Inventory is not in use until the stock count in November."
            }
            """,

        ["ExportAuditTrail"] = """
            {
              "entityType": "StaffUser",
              "entityId": "0192f3c1-9b1e-7a44-9a1b-1f9a0c2e77d1",
              "actorId": null,
              "action": "identity.user.",
              "from": "2026-09-01T00:00:00+05:30",
              "to": "2026-10-01T00:00:00+05:30",
              "cursor": null,
              "limit": 500,
              "reason": "Quarterly access review requested by the owner."
            }
            """,

        ["SignIn"] = """
            {
              "identifier": "counter.demo",
              "password": "example-passphrase-not-a-real-credential",
              "captchaResponse": null
            }
            """,

        ["AnswerMultiFactorChallenge"] = """
            {
              "factor": "totp",
              "code": "000000",
              "rememberDevice": false
            }
            """,

        ["ConfirmMultiFactorEnrolment"] = """
            {
              "code": "000000"
            }
            """,

        ["CompletePasskeyRegistration"] = """
            {
              "ceremonyId": "0192f3c1-9b1e-7a44-9a1b-1f9a0c2e77d1",
              "credential": {
                "id": "ZXhhbXBsZS1jcmVkZW50aWFsLWlkZW50aWZpZXI",
                "rawId": "ZXhhbXBsZS1jcmVkZW50aWFsLWlkZW50aWZpZXI",
                "type": "public-key",
                "response": {
                  "attestationObject": "ZXhhbXBsZS1hdHRlc3RhdGlvbi1vYmplY3Q",
                  "clientDataJSON": "ZXhhbXBsZS1jbGllbnQtZGF0YQ"
                }
              },
              "label": "Counter tablet"
            }
            """,

        ["CompletePasskeyAssertion"] = """
            {
              "ceremonyId": "0192f3c1-9b1e-7a44-9a1b-1f9a0c2e77d1",
              "credential": {
                "id": "ZXhhbXBsZS1jcmVkZW50aWFsLWlkZW50aWZpZXI",
                "rawId": "ZXhhbXBsZS1jcmVkZW50aWFsLWlkZW50aWZpZXI",
                "type": "public-key",
                "response": {
                  "authenticatorData": "ZXhhbXBsZS1hdXRoZW50aWNhdG9yLWRhdGE",
                  "clientDataJSON": "ZXhhbXBsZS1jbGllbnQtZGF0YQ",
                  "signature": "ZXhhbXBsZS1zaWduYXR1cmU",
                  "userHandle": null
                }
              }
            }
            """,

        ["DefineRole"] = """
            {
              "key": "senior_cashier",
              "name": "Senior Cashier",
              "description": "A cashier who may also approve a refund and close the day's session.",
              "reach": "Branch",
              "reason": "The Erode counter needs somebody who can close the day when the manager is away."
            }
            """,

        ["DescribeRole"] = """
            {
              "name": "Senior Cashier",
              "description": "A cashier who may also approve a refund and close the day's session.",
              "reason": "The old description said 'counter lead', which nobody in the shop calls it."
            }
            """,

        ["ReplaceRolePermissions"] = """
            {
              "permissionKeys": [
                "billing.invoice.read",
                "billing.payment.record",
                "billing.session.close"
              ],
              "reason": "Approved at the September operations review; the counter closes its own session from Monday."
            }
            """,

        ["DeleteRole"] = """
            {
              "reason": "The trial of a separate measurement role ended; nobody was ever assigned to it."
            }
            """,

        ["ReplayOutboxMessage"] = """
            {
              "reason": "The notification provider outage was resolved at 09:40; the customer was never told their order was ready."
            }
            """,

        ["RegisterCustomer"] = """
            {
              "displayName": "Lakshmi Ramanathan",
              "nativeName": "\u0BB2\u0B9F\u0BCD\u0B9A\u0BC1\u0BAE\u0BBF",
              "phone": "+91 90000 00021",
              "alternatePhone": null,
              "email": "lakshmi.demo@example.invalid",
              "addressLine": "12 Second Street, Demo Nagar",
              "locality": "Peelamedu",
              "postcode": "641004",
              "language": "ta-IN",
              "duplicatesReviewed": false
            }
            """,

        ["CorrectCustomer"] = """
            {
              "displayName": "Lakshmi Sundaram",
              "nativeName": "\u0BB2\u0B9F\u0BCD\u0B9A\u0BC1\u0BAE\u0BBF",
              "phone": "+91 90000 00021",
              "alternatePhone": "+91 90000 00022",
              "email": "lakshmi.demo@example.invalid",
              "addressLine": "12 Second Street, Demo Nagar",
              "locality": "Peelamedu",
              "postcode": "641004",
              "language": "ta-IN",
              "reason": "Married in August and asked for the new surname on her receipts."
            }
            """,

        ["DeactivateCustomer"] = """
            {
              "reason": "Moved out of the city and asked us not to contact her about new offers."
            }
            """,

        ["ReactivateCustomer"] = """
            {
              "reason": "Moved back and came in for a blouse; she asked us to use the old record."
            }
            """,

        ["RecordCustomerConsent"] = """
            {
              "purposeKey": "photo_capture",
              "decision": "Granted",
              "source": "counter, verbal"
            }
            """,

        ["ReplaceCustomerCommunicationPreferences"] = """
            {
              "allowedChannels": ["Sms", "WhatsApp"],
              "language": "ta-IN",
              "quietHoursStart": "21:30:00",
              "quietHoursEnd": "08:00:00"
            }
            """,

        ["RequestPasswordRecovery"] = """
            {
              "email": "counter.demo@example.invalid"
            }
            """,

        ["ConfirmPasswordRecovery"] = """
            {
              "token": "bm90LWEtcmVhbC1yZWNvdmVyeS10b2tlbg",
              "newPassword": "example-passphrase-not-a-real-credential"
            }
            """,
    };

    /// <summary>The example for an operation, or <see langword="null"/> when none is registered.</summary>
    /// <param name="operationId">The operation identifier, as declared by <c>WithName</c>.</param>
    /// <returns>A parsed example, or <see langword="null"/>.</returns>
    public static JsonNode? For(string operationId)
        => Examples.TryGetValue(operationId, out var json) ? JsonNode.Parse(json) : null;

    /// <summary>The operation identifiers an example is registered for.</summary>
    public static IReadOnlyCollection<string> RegisteredOperations => Examples.Keys;
}
