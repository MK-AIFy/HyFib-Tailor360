# Versioning — the one source, the policy, and what may run together

Sub-issue of [#59](https://github.com/MK-AIFy/HyFib-Tailor360/issues/59). This document names the single version a
release is built from, states the semantic-versioning policy for each of the four components, and carries the
compatibility matrix format a release note fills in. It is the policy; [`release-gates.md`](release-gates.md) and
[`release-evidence.md`](release-evidence.md) are the checklist and the form that reference it.

---

## 1. The one version source

The repository root carries a `VERSION` file holding a semantic version and nothing else — no comment, no prefix, no
trailing content beyond a newline. It is **versioned configuration, not code** (DOR-11): the number is changed by a
release pull request, never written by a build.

`Directory.Build.props` reads `VERSION` into `<VersionPrefix>` for every .NET project in the solution, so
`BuildInformation.Version` (`src/Hosts/Tailor360.Web/Configuration/BuildInformation.cs`) reports the release version
without a build argument. `clients/pwa/package.json`'s `"version"` field is the same number for the client build,
and the two are asserted equal by an architecture test
(`tests/Tailor360.ArchitectureTests/VersionParityTests.cs`) — a pull request that changes one without the other
fails the architecture tier, naming both files and both values.

### 1.1 Build metadata

The .NET SDK appends `+<SourceRevisionId>` to the compiled `AssemblyInformationalVersion` whenever a source revision
is available — every build inside this repository's working copy, including a plain `dotnet build` and every CI
run, because the repository is a git working copy and `Deterministic` is enabled. `BuildInformation.Version` is
therefore `<VERSION>` with no revision supplied, or `<VERSION>+<revision>` when one is: `GET /api/version`'s
`current` field carries whichever the build produced, verbatim. `infra/docker/Dockerfile.web` and
`Dockerfile.worker` already pass `SourceRevisionId` as a build argument and are unchanged by this document.

## 2. Semantic-versioning policy, per component

One number, four components sharing it. What each part of the number means, and what raising it costs the other
three, so a reviewer can decide from this table alone whether a change needs a major.

| Component | What a **major** means | What a **minor** means | What a **patch** means | Who raises it |
| --- | --- | --- | --- | --- |
| Progressive web application (PWA) | A breaking change to what the client expects from the API within the major it targets stops being tolerated — practically, `v2` of the HTTP API ships | A new screen, journey or optional capability | A fix with no visible behaviour change | Whoever's pull request changes the client or the API surface it depends on |
| API (`/api/v1/**`) | The path segment changes, `v2`; see [`../architecture/conventions.md`](../architecture/conventions.md) section 5.2 for what counts as breaking inside a major | Never, on its own — the API has no minor of its own within a major version; additive change ships continuously under the same major | Never, on its own | The API has no independent version number: it is versioned by path (section 5.1), and its compatibility with a given release is read from `schemaVersion` and `minimumClient` in `GET /api/version`, not from this number |
| Database (schema) | A contracting migration removes something an older build reads — see [`../dev/migrations.md`](../dev/migrations.md) for the expand/contract rule | An expanding migration adds a table or column | Never — a migration either expands the schema or it does not exist | Whoever's pull request adds the migration; `schemaVersion` in `GET /api/version` is the newest migration timestamp this build carries, not this number |
| Worker | Same rule as the PWA and the API: a change only the worker half of a deployment needs to understand before the web half does | A new background job or a materially changed one | A fix with no visible behaviour change | Whoever's pull request changes worker-side behaviour |

**In practice, one number moves for the whole release.** The API and the database do not carry an independent
semantic version — their own compatibility facts (`schemaVersion`, the additive-only rule) are already published
elsewhere, named in the table above rather than duplicated here. Raising the shared `VERSION` is a **major** bump
when any component's change inside the release would be a major by its own row above, a **minor** when the highest
is a minor, and a **patch** only when every component's change is a patch.

## 3. The compatibility matrix

A release note ([`../templates/release-notes.md`](../templates/release-notes.md)) carries one row per component,
naming the versions of the other three it may run with. For most releases every cell reads "this release", because
the four components are built, tagged and deployed together (plan #59's blueprint); the matrix exists for the
window during a rollback or a staged deployment when they briefly are not, per section 5 below.

| Component | Version | May run with |
| --- | --- | --- |
| Progressive web application | `<VERSION>` | The API and worker of this release, and the API of the release before it for the length of a rollback (section 5) |
| API | `<VERSION>` (path major `v1`) | Any PWA build whose `X-Client-Version` is at or above `minimumClient` |
| Database (schema) | `schemaVersion` from `GET /api/version` | The API of this release, and the API of the release immediately before it — expand-in-N, contract-no-earlier-than-N+2 ([`../dev/migrations.md`](../dev/migrations.md)) means two adjacent releases' API can always read the same schema |
| Worker | `<VERSION>` | The database and API of this release |

### 3.1 Third-party components

The plan's #59 blueprint names "patched PostgreSQL/MinIO/ClamAV/proxy versions" as part of the compatibility
picture. Their versions are not invented here — they are read from the pins already declared in
[`../../infra/compose/docker-compose.staging.yml`](../../infra/compose/docker-compose.staging.yml), quoted rather
than restated so this table cannot drift from what actually ships:

| Component | Image | Digest-pinned |
| --- | --- | --- |
| PostgreSQL | `postgres:18.6-alpine` | Yes |
| Object storage (MinIO server) | `quay.io/minio/minio:RELEASE.2025-09-07T16-13-09Z` | **No** — closed by [E15-F01-2] |
| Object storage (MinIO client) | `quay.io/minio/mc:RELEASE.2025-08-13T08-35-41Z` | **No** — closed by [E15-F01-2] |
| Malware scanner (ClamAV) | `clamav/clamav:1.5.4` | Yes |
| Reverse proxy (Caddy) | `caddy:2.11.4-alpine` | Yes |
| Telemetry collector | `otel/opentelemetry-collector-contrib:0.160.0` | Yes |
| Mail capture (Mailpit) | `axllent/mailpit:v1.31.1` | Yes |

[E15-F01-2]: https://github.com/MK-AIFy/HyFib-Tailor360/issues/460

A third-party component's own version is unrelated to `VERSION` — it moves on its own upgrade cadence, patched per
the schedule of [`../nfr/security-operations-targets.md`](../nfr/security-operations-targets.md) section 3, and is
recorded here as "what shipped with this release" rather than as a number this repository controls.

## 4. `minimumClient` — the two-release rule

Quoted from [`../nfr/security-operations-targets.md`](../nfr/security-operations-targets.md), the staff-client row
of the patch-cadence table: **`minimumClient` is raised only in the release after the change that requires it; an
outdated client receives `426`.** [`../architecture/conventions.md`](../architecture/conventions.md) section 5.4
carries the mechanism; this document states only when the number may move. Raising it for a real release is a
release decision made at that release, not a number this document proposes in advance.

## 5. Rollback compatibility

Two rules from the plan's Section 4.7 that no other document in this repository states as policy, quoted rather
than invented:

- **The API ignores unknown request fields.** An N+1 client can talk to an N server during a rollback because a
  field the newer client sends and the older server does not recognise is simply not read. This is true today only
  because nothing sets `System.Text.Json`'s `JsonUnmappedMemberHandling.Disallow` anywhere in the codebase — **it is
  not asserted by any test**. An assertion, if one is added, belongs on the API-contract surface
  ([`../api/conventions.md`](../api/conventions.md)) rather than invented here.
- **The client treats `404` on a new endpoint as "not available in this version"**, not as an error. This is a
  client-side behaviour the progressive web application does not implement yet; [#51] is the open issue that
  implements it. [`../architecture/conventions.md`](../architecture/conventions.md) sections 5.2 (additive-only
  change) and 5.4 (the version handshake) are the mechanisms these two rules depend on and are not restated here.

[#51]: https://github.com/MK-AIFy/HyFib-Tailor360/issues/51

## 6. Component lifecycle

Already written, not researched here — quoted from
[`../nfr/security-operations-targets.md`](../nfr/security-operations-targets.md) section 3, "Runtime and database
major versions": **a component inside twelve months of end of support is a `release-blocker`**, reviewed annually,
with the current runway recorded there rather than repeated in two places that could disagree.

## 7. Release cadence

`docs/process/versioning.md` refers to "a release train" wherever a window is measured in trains rather than days.
What that means in calendar terms is **RG-OD-04** in [`release-gates.md`](release-gates.md) section 9 —
**proposed, to be confirmed**, a fortnightly train assumed — and is cited from there rather than restated as a
second number here.

## 8. See also

| For | Read |
| --- | --- |
| What counts as a breaking change inside a major API version | [`../architecture/conventions.md`](../architecture/conventions.md) section 5.2 |
| The client version handshake and `426` | [`../architecture/conventions.md`](../architecture/conventions.md) section 5.4 |
| Expand/contract migration compatibility | [`../dev/migrations.md`](../dev/migrations.md) |
| The browser and device support matrix (not this document's concern — that is *our* four components) | [`../nfr/support-matrix.md`](../nfr/support-matrix.md) |
| The sixteen-item release evidence checklist this document's artefact feeds | [`release-gates.md`](release-gates.md) section 6, item 14 |
