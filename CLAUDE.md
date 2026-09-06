# HyFib Tailor 360 — repository guide

The standing engineering rules for this repository, for every contributor and every coding agent working in it.
Read this before the first change of a session. It is deliberately short: each section links to the document that
owns the detail instead of restating it, so when this file and a linked document disagree, **the linked document
wins and this one is corrected**.

Before working inside one of these trees, read its guide as well — they carry the rules this file does not:

| Tree | Guide | Covers |
| --- | --- | --- |
| `src/Modules/` | [`src/Modules/CLAUDE.md`](src/Modules/CLAUDE.md) | the five layers, the `Contracts` boundary, schemas, migrations, endpoints |
| `clients/pwa/` | [`clients/pwa/CLAUDE.md`](clients/pwa/CLAUDE.md) | components, accessibility, localisation, state, client testing |
| `infra/` | [`infra/CLAUDE.md`](infra/CLAUDE.md) | compose stacks, images, secrets, published ports, deployment |

---

## 1. What the system is

A tailoring-shop operations platform: customer intake and measurements, configurable stitching categories, design
and material selection, multi-garment orders with barcode chain of custody, inventory on an immutable stock ledger,
GST billing and payments, reporting, delivery and customer feedback. The client is an installable progressive web
application for phones, tablets and desktop browsers.

It is a **modular monolith** ([ADR-0001](docs/adr/0001-modular-monolith.md)): one solution, `HyFib.Tailor360.slnx`,
deployed as two hosts and a command-line tool.

```
src/Platform/   Abstractions, Persistence, Security, Observability — shared kernel, no business rules
src/Modules/    Identity, Customers, Catalog, Media, Orders, Custody, Inventory, Billing, Reporting,
                Notifications, Integration — eleven modules, five projects each
src/Hosts/      Tailor360.Web (API, BFF, serves the built client)  ·  Tailor360.Worker (background processing)
src/Tools/      Tailor360.Cli (migrate, init-reference-data, seed-synthetic, replay-outbox, flags)
clients/pwa/    React 19 + TypeScript, Vite, pnpm
tests/          UnitTests · ArchitectureTests · ContractTests · IntegrationTests
docs/           prd, architecture, adr, nfr, dev, platform, process
infra/          compose stacks, Dockerfiles, reverse proxy, observability
```

**Every module owns exactly one PostgreSQL schema, and no module reads another module's tables.** That single rule
is what the architecture tests in section 3 exist to protect. Ownership per module — schema, tables, storage
prefix, published contracts — is
[`docs/architecture/module-ownership.md`](docs/architecture/module-ownership.md).

---

## 2. Commands

`./scripts/dev <verb>` (bash) and `.\scripts\dev.ps1 <verb>` (PowerShell) are equivalent: same verbs, same output,
so a transcript from either can be pasted into a pull request. Both resolve the repository from their own location
and run from the root — which matters, because `dotnet` reads `global.json` from the working directory and
`global.json` is what selects the SDK and the Microsoft.Testing.Platform test runner. Run `dotnet test` from
elsewhere and it silently falls back to VSTest.

| Verb | Does |
| --- | --- |
| `doctor` | Toolchain report and which test tiers can run in this environment. **Run this first in a new environment.** |
| `up` | `restore`, then start the compose backing services (PostgreSQL, MinIO, Mailpit) |
| `restore` | `dotnet restore` the solution and `pnpm install` the client |
| `build` | `dotnet build` the solution and build the client |
| `test [tier]` | `all` (default), `unit`, `architecture`, `contract`, `integration`, `pwa`, `e2e` |
| `run` | Web host, worker and client dev server together, logging to `artifacts/logs/` |
| `migrate [--dry-run]` | Apply outstanding migrations through the command-line tool. Safe to re-run; `--dry-run` only reports |
| `reset` | Destructive: compose `down -v`, then `migrate`, `init-reference-data`, `seed-synthetic`. Refuses in Production |
| `status` | Probe the five components; non-zero exit when an essential one is down |
| `docs` | Check that every relative link in the documentation resolves |

The scripts deliberately wrap nothing that is already a one-liner. Run these directly:

| Task | Command |
| --- | --- |
| Build the solution | `dotnet build HyFib.Tailor360.slnx --configuration Debug` |
| Format check (must be clean before pushing) | `dotnet format --verify-no-changes` |
| One test tier | `dotnet test --project tests/Tailor360.UnitTests/Tailor360.UnitTests.csproj -- --filter-trait Category=Unit` |
| Whole solution | `dotnet test --solution HyFib.Tailor360.slnx` |
| Apply migrations | `dotnet run --project src/Tools/Tailor360.Cli -- migrate` (add `--dry-run` first) |
| Reference data / synthetic data | `dotnet run --project src/Tools/Tailor360.Cli -- init-reference-data` \| `-- seed-synthetic` |
| Client lint, types, tests, build | `pnpm --dir clients/pwa lint` \| `typecheck` \| `test` \| `build` |
| Client formatting | `pnpm --dir clients/pwa format:check` (`format` rewrites) |

A tier names its project as well as its trait on purpose: filtering the whole solution by trait makes projects that
hold no test of that tier report "zero tests ran", which the test platform returns as exit code 8 — a green tier
reported as a failure. Full detail: [`docs/dev/commands.md`](docs/dev/commands.md); setup per operating system:
[`docs/dev/setup.md`](docs/dev/setup.md).

---

## 3. Module boundaries

The rules below have stable identifiers, one assertion each, and a test that fails on them. The catalogue —
rationale, allowed exceptions and how an exception is registered — is
[`docs/architecture/architecture-rules.md`](docs/architecture/architecture-rules.md) and is authoritative.
**Do not work around a failing rule; either change the design, or change the catalogue in the same pull request
with the reason.**

| Rule | Assertion | Status |
| --- | --- | --- |
| ARCH-001 | A module's `Domain` project references `Tailor360.Platform.Abstractions` and nothing else | Enforced |
| ARCH-002 | A `Domain` project references no EF Core, no ASP.NET Core and no third-party SDK package | Enforced |
| ARCH-003 | An `Application` project never references another module's `Infrastructure` or `Api` | Enforced |
| ARCH-004 | Only a module's `Contracts` project and the `Platform.*` libraries cross a module boundary | Enforced |
| ARCH-005 | No `DbContext` maps a table in another module's schema | Enforced |
| ARCH-006 | A host composes a module only through its registration extensions, and references only `Api`, `Infrastructure` or `Contracts` | Enforced |
| ARCH-007 | Every endpoint declares an authorisation policy or a justified anonymous exposure | Enforced |
| ARCH-008 | Every state-changing endpoint carries the audit filter | Enforced |
| ARCH-009 | Provider SDK packages are referenced only by `Integration.Infrastructure` and by test projects | Enforced |
| ARCH-010 | No Billing project references any Orders project | Enforced |
| ARCH-011 | Reporting references only the `Contracts` projects of other modules | Enforced |
| ARCH-012 | No project except a test project references `Tailor360.Web` or `Tailor360.Worker` | Enforced |
| ARCH-013 | Public payload types are declared in an `Api` project; no `Domain` type is ever returned | Specified (#25, #53) |
| ARCH-014 | `DateTime.Now`, `DateTime.UtcNow`, `DateTime.Today`, `DateTimeOffset.Now`, `DateTimeOffset.UtcNow` appear nowhere in `src/` outside the clock abstraction | Enforced |
| ARCH-015 | `Guid.NewGuid()` appears nowhere in `src/` outside the identifier generator | Enforced |
| ARCH-016 | `HttpClient` is never constructed directly; outbound calls go through `IOutboundHttp` | Enforced |
| ARCH-017 | Every endpoint declares exactly one rate-limit policy from the catalogue | Specified (#53) |
| ARCH-018 | Every endpoint whose permission is marked `RequiresStepUp` declares `.RequireStepUp()` | Enforced |
| ARCH-019 | No endpoint accepts more than one authentication scheme | Enforced |
| ARCH-020 | Only the worker and the command-line hosts reference `IWorkerScopeFactory` | Enforced |
| ARCH-021 | Every background job declares its permissions and branch scope with `[WorkerJob]` | Enforced |
| ARCH-022 | No endpoint declares both a permission and a justified anonymous exposure | Enforced |
| ARCH-023 | A branch-scoped permissioned endpoint with a route parameter declares a resource scope, or records why it names none | Enforced |

Crossing a boundary has exactly five sanctioned mechanisms — a read contract, a versioned integration event through
the transactional outbox, a confirmation-participant hook, composition in the web host, or a platform port. A new
shared table, a cross-schema view over base tables, or a reference to another module's `Infrastructure` needs an
architecture decision record in [`docs/adr/`](docs/adr/) **before** it may be merged.

---

## 4. Security rules

These are not review preferences; each has a test, an architecture rule or a release gate behind it.

1. **Deny by default.** Every endpoint declares `RequirePermission(key, scope)` or
   `AllowAnonymousWithJustification(justification, reviewedIn)`. An endpoint with neither fails ARCH-007.
2. **Branch scope is evaluated, never inferred.** Resource ownership and branch reach are checked server-side; a
   caller's claims say what to check, not what to allow.
3. **Validate everything server-side.** Errors are RFC 9457 problem details with field errors — never a stack
   trace, never a raw exception message.
4. **Idempotency where a client may retry.** The endpoint accepts `Idempotency-Key`, replays return the first
   outcome, and "same key, different body" and "duplicate while in flight" both have defined behaviour.
5. **Audit every state change.** `.Audited("action")` on every `POST`/`PUT`/`PATCH`/`DELETE` endpoint (ARCH-008);
   a read is audited explicitly when the read itself is sensitive, such as exporting personal data.
6. **No secrets anywhere but a mounted file.** Secrets arrive as files under `/run/secrets` (the file name is the
   configuration key, `__` is the section separator) — never an environment variable, never a build argument, never
   a committed value. See [`docs/platform/secrets.md`](docs/platform/secrets.md).
7. **No secrets and no personal data in logs, traces, metrics or client telemetry.** No request bodies, tokens,
   measurements, image bytes, rendered message bodies, recipient addresses or card data. Personal data appears as
   an identifier only; sensitive personal data never appears at all. `LogRedaction` masks by property name — extend
   it rather than trusting call sites. Correlation and causation identifiers go everywhere.
8. **Personal data is never an identifier.** Not a key, not a barcode payload, not an object-storage key, not a
   filename, not a URL segment. Resource identifiers are UUIDv7 from `IIdGenerator`; sequential integers are never
   exposed.
9. **Media is never given a URL.** Every object is streamed by an endpoint that re-authorises the request and logs
   the access.
10. **Synthetic data only outside production**, and no production data is ever copied into development, staging or
    a fixture.

Classification of every field, and the handling rules per class, are
[`docs/nfr/data-classification.md`](docs/nfr/data-classification.md).

---

## 5. Testing

Four .NET tiers plus the client suite. A test carries the `[Trait("Category", "…")]` of its tier — without it the
test does not run in that tier at all.

| Tier | Project | Holds |
| --- | --- | --- |
| Unit | `tests/Tailor360.UnitTests` | Domain rules, invariants, property tests. No database and no host; time and identifiers come from injected abstractions |
| Architecture | `tests/Tailor360.ArchitectureTests` | The `ARCH-…` catalogue over the project graph and the source text |
| Contract | `tests/Tailor360.ContractTests` | Endpoint inventory over the composed route table; OpenAPI lint and diff |
| Integration | `tests/Tailor360.IntegrationTests` | Persistence, API behaviour, the authorisation matrix, against real PostgreSQL |
| Client | `clients/pwa` | Vitest in jsdom — see the client guide |

Rules that hold across all of them:

- **Integration tests need a database.** They use `TAILOR360_TEST_DATABASE_URL` when it is set, otherwise a
  container when a Docker daemon is reachable, otherwise they skip with a visible reason — and **`CI=true` turns
  that skip into a failure**, so nothing merges unverified. Check with `./scripts/dev doctor`.
- **Test the negative case.** Deny-by-default, other-branch access, a replayed idempotency key, an expired link, an
  unpaid dispatch attempt. Positive-path-only coverage is the most common review finding.
- **Run the suite twice** before asking for review; a test that passes only in one order is a defect.
- **Synthetic data only.** Never a real customer name, phone number or measurement, in any fixture.

The full pyramid and per-layer gates are plan Section 5.3 in
[`docs/IMPLEMENTATION_PLAN.md`](docs/IMPLEMENTATION_PLAN.md).

---

## 6. Git workflow

**One issue, one branch, one pull request.** An issue too large for that is split into sub-issues first, each with
its own branch and pull request; the parent closes when they are all merged. Target under about 1,500 changed
lines, excluding generated code and tests.

- **Branch**: `feat|fix|docs/eXX-fYY[a-c]-<slug>`, all lower case — for example `feat/e02-f03-ci-quality-gates`.
  The policy check also accepts `chore` as a type and a plain slug in place of the `eXX-fYY` identifier, and
  exempts the reserved automation prefixes. `.github/workflows/pr-policy.yml` holds the exact pattern.
- **Commits**: Conventional Commits — `type(scope): subject in the imperative`, for example
  `feat(orders): confirm an order and allocate its barcodes`. The body says why, and carries `Refs #NN`.
- **Pull request**: exactly one issue linked, as `Refs #NN` while the issue stays open or `Closes #NN` when merging
  completes it. `Fixes` and `Resolves` are deliberately not accepted, so the link reads the same everywhere.
  Fill in [`.github/pull_request_template.md`](.github/pull_request_template.md); delete a section only when it
  genuinely cannot apply, and say why in one line.
- **Evidence before review.** Paste the run output, the screenshots and the migration output — a description of
  them is not evidence, and a reviewer will not start without them.
- **Merge**: squash, after a CODEOWNERS review and a green required check. Never merge your own unreviewed work
  around the check.
- **Scope discipline**: no unrelated refactor, no drive-by formatting of untouched files. Anything discovered out
  of scope becomes a new issue linked to the epic.

The pull-request policy check (`.github/workflows/pr-policy.yml`) enforces the mechanical parts and is the
authority on the exact patterns it applies; `.github/workflows/ci.yml` runs the build, the tests and the scans.

---

## 7. Definition of Done

The nine-item checklist a pull request is reviewed against is
[`docs/process/definition-of-done.md`](docs/process/definition-of-done.md), mirrored by the pull-request template.
It is not repeated here — read it there, because it is the wording a review comment cites.

Two things about it are worth knowing before you start rather than at the end:

- **Items 1, 3, 6 and 9 always apply** — linked issue and branch, tests, secrets and personal data, evidence.
  Items 2, 4, 5, 7 and 8 may be deleted when they genuinely cannot apply, with a one-line reason.
- **A pull request is done when a second person can see that it is done.** Every item resolves to an artefact — a
  command output, a screenshot, a migration log, a scan result, a document diff. "Tested locally" is not evidence.

Before a branch is cut, the issue must pass
[`docs/process/definition-of-ready.md`](docs/process/definition-of-ready.md). Before a release ships,
[`docs/process/release-gates.md`](docs/process/release-gates.md) applies, and a gate is skipped only through a
recorded waiver in [`docs/process/waivers.md`](docs/process/waivers.md).

---

## 8. When a rule is in the way

Nothing here is meant to be worked around silently. The route out of each is written down:

| Situation | Route |
| --- | --- |
| An architecture rule fails and the design is right | Change the rule in `docs/architecture/architecture-rules.md` in the same pull request, with the rationale and the exception register entry |
| A decision changes | An architecture decision record in `docs/adr/`, from [`docs/adr/0000-template.md`](docs/adr/0000-template.md) |
| A security or quality gate cannot be met now | A recorded, dated, owned waiver in `docs/process/waivers.md` — never a disabled check |
| Something is undecided | It is an open decision: check `docs/prd/assumptions-and-open-decisions.md` and record the question there rather than inventing an answer |
| A number, retention period, rounding rule or role grant has no source | Do not invent one. It is a product decision, and a reviewer will treat an unsourced number as a change to the product |

Terminology is [`docs/prd/glossary.md`](docs/prd/glossary.md); conventions for money, time, identifiers,
concurrency and versioning are [`docs/architecture/conventions.md`](docs/architecture/conventions.md). Prose in
markdown hard-wraps at 120 columns and uses British spelling.
