# The API document and the gates around it

`docs/api/openapi.v1.json` is the published contract of the version 1 API. It is generated from the
composed application, committed to the repository, and held in place by four gates that fail on four
different ways for a contract to go wrong: drifting from the code, being under-described, losing an
endpoint, and changing in a way that breaks a client.

This document says how it is produced, what each gate asserts, how to regenerate the file, and where the
rules are written down twice on purpose. The conventions the gates enforce — URI versioning, the
additive-only rule, the deprecation window and the client version handshake — are
[`../architecture/conventions.md`](../architecture/conventions.md) section 5, which is authoritative;
nothing here overrides it.

---

## 1. How the document is produced

It is generated, never edited. Editing it by hand would put the contract and the application into a
disagreement that the first gate below then reports as a failure of the document.

| Piece | Where | What it adds |
| --- | --- | --- |
| The generator | `Microsoft.AspNetCore.OpenApi`, registered by `ApiDocument.AddTailor360OpenApi` | Paths, operations, request schemas and parameters, from the route table |
| Document transformer | `src/Hosts/Tailor360.Web/OpenApi/ApiDocumentTransformer.cs` | Title, version, description, server, the two security schemes, the shared `ProblemDetails` schema, the reusable error responses and headers, the tag descriptions |
| Operation transformer | `src/Hosts/Tailor360.Web/OpenApi/EndpointContractTransformer.cs` | Per-operation security requirements, the applicable error responses, request examples, and the `x-tailor360-…` annotations |
| Examples | `src/Hosts/Tailor360.Web/OpenApi/PayloadExamples.cs` | One request example per operation that takes a body, keyed by operation identifier |
| Canonical form | `tests/Tailor360.ContractTests/ApiDocumentSource.cs` | Members sorted, two-space indent, one trailing newline |

Everything the transformers add is read from the endpoint's own metadata — its authorisation policy, its
anonymous justification, its audit action, its rate-limit policy. Nothing is configured twice, so the
document cannot describe a surface the pipeline does not enforce.

#### 1.0 What the document is consumed by

The progressive web application's types come from it. `pnpm --dir clients/pwa generate:api` runs
`openapi-typescript` over this file into `clients/pwa/src/api/schema.d.ts`, and
`clients/pwa/src/api/contract.ts` pins each hand-written payload type against the generated one — so an
endpoint whose response gains a required member, loses one or retypes one fails `pnpm typecheck`.

The generated file is committed, and `pnpm generate:api:check` regenerates and diffs it. A document that
moved without a regeneration would leave the conformance assertions checking yesterday's contract and
still passing, which is the failure mode the check exists for; CI runs it in the client job.

### 1.1 Which credentials an operation names

Two security schemes are declared, and they are not two ways of authenticating.

- `sessionCookie` — the opaque server-side session identifier in `__Host-t360.session`. It is the one
  authentication scheme the application registers (ARCH-019).
- `antiForgeryToken` — the request half of the anti-forgery pair, in `X-CSRF-Token`. It is a second thing
  a state-changing request must carry, not a second identity.

An operation that needs both names both **inside one requirement object**, which OpenAPI reads as "all of
these"; a list of two requirements would read as "either of these" and would describe a surface where the
anti-forgery token alone is enough. An operation that needs neither declares an empty list, which is how
OpenAPI says "nothing required" — as opposed to omitting the member, which says nothing at all.

### 1.2 Which failures an operation publishes

The set is derived per operation rather than applied uniformly, because a documented response that cannot
occur misleads as effectively as a missing one.

| Status | Published when |
| --- | --- |
| 400, 429, 500 | Always. Any request can be malformed, throttled, or meet an unexpected failure |
| 401, 403 | The endpoint declares an authorisation policy — a permission, a self-service assurance level or a step-up demand |
| 404 | The route names a resource with a path parameter |
| 415 | The operation accepts a request body |
| 426 | Always, except on `GET /api/version`. The client-version handshake runs before routing reaches the handler, so every operation it can refuse documents the refusal; the handshake exempts its own endpoint, because that is where a refused client reads `minimumClient` from |

An endpoint that answers something outside this set declares it on itself — `409` on a version conflict,
`422` on a reused idempotency key — and the transformer leaves the declaration alone. The shared
components in `components.responses` are referenced rather than repeated, so adding a sentence to the
description of "forbidden" changes one place and one line of the diff.

Successes are declared, not derived. A minimal-API handler whose branches return `IResult` publishes no
response type at all, so an endpoint says what it returns with `.Produces<T>(status)`; the
`success-response-schema` rule fails an operation that documents a success with no schema, and fails one
that documents a body on `204`. Without it the document describes every failure precisely and every
success as the word "OK", which is the state a generated client cannot be built from.

### 1.3 The annotations

Each operation carries `x-tailor360-…` members so the document doubles as the endpoint inventory:
`authorisation` (`permission:<key>`, `session` or `anonymous`), `anonymous` (where the exposure was
reviewed), `audit` (the action a state change is recorded under) and `rate-limit` (the declared policy).
They are documentation, not contract: the diff gate ignores them.

---

## 2. Regenerating it

```bash
TAILOR360_WRITE_OPENAPI=1 dotnet test --project tests/Tailor360.ContractTests/Tailor360.ContractTests.csproj
```

The run rewrites `docs/api/openapi.v1.json` from the composed application instead of comparing against it.
Read the resulting diff — it is the change to the published contract, and it is the part of a pull request
a reviewer looks at first — and commit it with the change that caused it.

There is no `./scripts/dev` verb for this, because the scripts deliberately wrap nothing that is already a
one-liner.

---

## 3. The four gates

| Gate | Runs in | Asserts | Fails when |
| --- | --- | --- | --- |
| Committed document | Contract tier, `OpenApiDocumentTests.TheCommittedDocumentIsTheGeneratedDocument` | The committed file is byte-for-byte what the application produces | An endpoint or a payload changed and the document was not regenerated |
| Lint | Contract tier, `OpenApiDocumentTests.TheGeneratedDocumentPassesTheLint`; and Spectral in CI | Every operation is identified, summarised, tagged, secured, and publishes its errors and an example | An operation is added without a summary, a security declaration, an example or its error responses |
| Endpoint inventory | Contract tier, `EndpointInventoryTests` | Every mapped route appears in the document, and every documented operation is mapped | A route is added and not published, is hidden without a reason, or the document keeps an operation that no longer exists |
| Breaking change | CI job `api-contract`, `scripts/openapi-diff.py` | The contract changed additively since the base branch | A field, operation, status or guarantee was removed, renamed, narrowed or retyped without approval |

### 3.1 The lint exists twice

The rules are implemented in `tests/Tailor360.ContractTests/OpenApiLint.cs` **and** declared in
`.spectral.yaml`, and the duplication is deliberate.

- The tests run on every developer machine and in the .NET job with no network and no toolchain beyond the
  SDK. A gate that can only run when a package registry is reachable is a gate that gets skipped on the day
  it matters.
- The Spectral ruleset is the format every OpenAPI editor, tool and reviewer already reads, and it is what
  makes the rules legible to somebody who never opens the test project.

They are kept in step by the table below. **A rule added to one and not the other is a defect in the gate.**

| Rule | In `OpenApiLint.cs` | In `.spectral.yaml` |
| --- | --- | --- |
| Every operation has an identifier, and no two share one | `operation-id`, `operation-id-unique` | `operation-operationId`, `operation-operationId-unique` (from `spectral:oas`) |
| Every operation has a summary | `operation-summary` | `operation-summary-present` |
| Every operation names a described tag | `operation-tag`, `tag-described` | `operation-tagged` |
| Every operation declares its security requirements | `operation-security` | `operation-security-declared` |
| An operation requiring nothing records why it is anonymous | `anonymous-justified` | — (asserted through `operation-authorisation-annotated`) |
| Every operation says how it is authorised | — (asserted by `EveryDocumentedOperationCarriesTheAnnotations…`) | `operation-authorisation-annotated` |
| Every operation publishes 400, 429 and 500 | `problem-response-coverage` | `problem-response-coverage` |
| 401 and 403 are published together or not at all | `problem-response-pairing` | — |
| Every error is `application/problem+json` | `problem-media-type` | `problem-details-media-type` |
| Every error references the shared error schema | `problem-schema` | `problem-details-shared-schema` |
| Every success that returns a body documents its schema, and `204` documents none | `success-response-schema`, `success-response-body` | `success-response-schema` |
| Every request body carries an example | `request-example` | `request-body-example` |
| Every path is under `/api/v1/`, except `/api/version` | `path-prefix`, `path-version` | `versioned-path` |
| A deprecated operation names its replacement | `deprecation-names-replacement` | — |
| The document has a title, a version, a description, a server, a security scheme and the error schema | `document-info`, `document-servers`, `document-security-schemes`, `document-problem-schema` | `info-*`, `oas3-*` (from `spectral:oas`) |

Both are negative-controlled. Each rule in `OpenApiLint.cs` has a control in `OpenApiDocumentTests` that
breaks exactly one thing in an otherwise sound document and asserts that the rule it breaks is the rule
that fires; a lint that has quietly stopped detecting is indistinguishable from a document that passes it.

### 3.2 Endpoints that publish no contract

A route may be absent from the document for exactly two reasons.

1. **It declares `.InternalEndpoint(reason, reviewedIn)`.** The verb records why the route publishes no
   contract, and excludes it from the document, so the two facts cannot drift apart. `ExcludeFromDescription`
   on its own is **not** accepted — otherwise hiding an endpoint would be the way to avoid documenting it,
   and the endpoint nobody documents is the endpoint nobody reviews.
2. **It is one of the three health probes.** They answer before authentication is a meaningful concept, at a
   fixed cadence, to an orchestrator and an external uptime check. This is the same standing exemption
   ARCH-017 and the authorisation matrix record.

The internal set today is the OpenAPI route itself (Development only), the two 404 catch-alls under `/api`
and `/health`, and the application shell. Each is declared in `src/Hosts/Tailor360.Web/Program.cs` with its
reason.

### 3.3 The breaking-change gate

`scripts/openapi-diff.py` compares the document on the base branch with the one in the branch under review
and classifies every difference against the table in
[`../architecture/conventions.md`](../architecture/conventions.md) section 5.2. Additive changes pass;
breaking ones fail unless **both**:

- a CODEOWNER of `docs/api/` has applied the `api-breaking-approved` label — a label leaves no record on its
  own, so it is not enough by itself; and
- the change is recorded in [`breaking-changes.md`](breaking-changes.md) naming the pull request — a record
  nobody approved is a note to self, so that is not enough either.

CODEOWNERS applies none of its rules on this repository yet
(`../process/branch-protection.md`, BP-OD-03), so the "CODEOWNER of `docs/api/`" half of that is a
convention until it is answered, not an enforced control. The label and the register are enforced.

The classifier's own control is `scripts/openapi-diff.py --self-test`, which runs it over twenty-three
changes whose answers are known — an operation removed, a field retyped, a response enum widened, validation
tightened, a credential demanded — and fails unless each is classified as expected. CI runs the self-test
before it trusts the real diff.

**Why not `oasdiff`, which the plan names?** Two reasons, both about the gate rather than the tool. A gate
that needs a Go toolchain and a module proxy is a gate that is skipped the first time either is unavailable,
and a skipped contract gate is how a breaking change reaches a client. And the rules here are this
repository's rules: `oasdiff` cannot know that a new value in a response enum is breaking for us, that an
operation identifier is a generated client's method name, or that an approved break needs a row in a
register. Running it as well would add a second opinion with no authority. What is borrowed from it is its
shape — compare two documents, classify, report, exit non-zero.

---

## 4. What the document does not describe yet

Recorded here rather than left to be discovered, because a contract that quietly under-describes is worse
than one that says where it stops.

| Gap | Consequence | Closed by |
| --- | --- | --- |
| Endpoints return `IResult`, so no operation declares a **response body type**. Every operation documents a bare `200` | A generated client knows what to send and not what it receives | Typed responses on the Identity endpoints (`.Produces<T>()` / `TypedResults`), which also gives ARCH-013 its response side |
| No endpoint declares `Idempotency-Key`, `If-Match` or `ETag` | The `409`/`422` idempotency contract and the concurrency contract are in the conventions but not in the document | The idempotency filter and the concurrency helpers of issue #53 |
| Anonymous credential endpoints answer `401` on a bad credential, which is outside the derived set and is not declared per endpoint | `POST /api/v1/auth/login` documents no `401` | A `.Produces` declaration on those endpoints |
| `x-tailor360-rate-limit` is absent from the operations that declare no policy | Seven routes, all internal or health, carry no policy | ARCH-017 |

---

## 5. Maintenance

- **Adding an endpoint**: map it, declare its policy and its rate-limit policy, name it with `.WithName`,
  summarise it with `.WithSummary`, tag it, register a request example if it takes a body, then regenerate
  the document and commit it. Four gates will tell you which of those you forgot.
- **Adding a tag**: describe it in `ApiDocumentTransformer.TagDescriptions`, or the lint fails on the first
  operation that uses it.
- **Adding a lint rule**: add it to `OpenApiLint.cs` with its negative control, to `.spectral.yaml`, and to
  the table in section 3.1.
- **Making a breaking change**: don't, inside `v1`. If it is genuinely unavoidable, follow section 3.3 and
  the deprecation policy in [`../architecture/conventions.md`](../architecture/conventions.md) section 5.3.
