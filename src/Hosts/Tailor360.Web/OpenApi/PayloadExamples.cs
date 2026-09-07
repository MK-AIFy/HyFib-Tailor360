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
