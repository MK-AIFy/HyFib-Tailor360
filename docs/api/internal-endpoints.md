# Internal endpoints

Every route this application maps appears in [`openapi.v1.json`](openapi.v1.json), or is declared an
**internal endpoint** — a route that is part of the plumbing rather than of the published contract. This
document is the register of those declarations and the rule that governs them.

The endpoint-inventory gate in `tests/Tailor360.ContractTests/EndpointInventoryTests.cs` is what makes
the register real: it walks the composed route table and fails on any route that is neither in the
document nor declared internal.

---

## 1. Why the declaration exists

Without it the inventory rule would be unsatisfiable. A catch-all that answers `404` has no contract to
publish; the application shell answers with markup, not a payload. Both are routes, and neither belongs
in a document a client generates from.

The obvious alternative — letting `ExcludeFromDescription` be its own excuse — makes the rule toothless
in the one direction that matters. Hiding an endpoint from the document would become the way to avoid
documenting it, and the first endpoint to take that route would be one somebody preferred not to
explain. So the exclusion is said out loud instead:

```csharp
.InternalEndpoint(
    "Why this route publishes no contract, and why a client never calls it directly.",
    "#53, docs/api/openapi-gates.md")
```

`InternalEndpoint` both excludes the route from the document and records why, in one call, so the two
facts cannot drift apart. An internal endpoint is exactly one that is both excluded and explained.

## 2. What it does not exempt

Nothing but the document. An internal endpoint still:

- declares an authorisation policy or a justified anonymous exposure (**ARCH-007**);
- carries the audit filter when it changes state (**ARCH-008**);
- declares exactly one rate-limit policy from the catalogue (**ARCH-017**);
- accepts exactly one authentication scheme (**ARCH-019**).

An endpoint that is internal *and* unauthorised is two defects, and the architecture tests report both.

## 3. The register

| Route | Why it publishes no contract | Reviewed in |
| --- | --- | --- |
| `GET /openapi/{documentName}.json` | The document cannot describe itself, and it is mapped in Development only. The published contract is the committed copy at [`openapi.v1.json`](openapi.v1.json), which is what clients and the diff gate read | #53, [`openapi-gates.md`](openapi-gates.md) |
| `GET|POST|… /api/{**path}` (fallback) | A catch-all that answers `404` and nothing else. It has no contract to publish, and publishing one would document every path in the API as though it existed | #53, [`openapi-gates.md`](openapi-gates.md) |
| `GET|POST|… /health/{**path}` (fallback) | A catch-all that answers `404` and nothing else. The probes it guards are operational routes for the orchestrator and the uptime check, not part of the client contract | #53, [`openapi-gates.md`](openapi-gates.md) |
| The application shell fallback | The shell is an HTML document, not an API operation: it answers with markup rather than with a payload, and no client calls it as an interface. What the shell then calls is the documented surface | #53, [`openapi-gates.md`](openapi-gates.md) |

The health probes `/health/live`, `/health/ready` and `/health/startup` are **not** in this register.
They are not `/api/**` routes at all, so the inventory rule does not reach them; they carry
`HealthProbeMetadata`, which is how the rate-limit rule recognises the one exemption it allows. Their
contract is the readiness and liveness behaviour in [`../nfr/slo.md`](../nfr/slo.md), read by the
orchestrator rather than by a client.

## 4. Adding one

1. Ask first whether the route has a contract after all. Most do: an endpoint that returns a payload a
   client parses belongs in the document, however internal it feels.
2. Declare it with `.InternalEndpoint(reason, reviewedIn)`, where the reason says what a reader needs in
   order to agree — what the route is for, and why no client calls it as an interface.
3. Add a row here in the same pull request. A declaration with no row is a route nobody outside the diff
   will ever see.
