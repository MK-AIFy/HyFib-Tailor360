# Release notes template

Copy this file to the GitHub Release description for the tag, or attach it as `release-notes.md` in
`docs/process/release-evidence.md`'s **RE-14** row. One release, one set of notes — it is what
[`../process/release-gates.md`](../process/release-gates.md) section 6 item 14 requires and what an operator and a
reader use to decide whether, and how, a deployment affects them. The policy behind every field here is
[`../process/versioning.md`](../process/versioning.md); this file states values, that one states the rules.

---

## How to use this template

1. **Delete this "How to use" section** once the file is filled in for a real release. What remains must read as
   notes about this release, not as a form.
2. **Every field is a value, not a claim.** "The interruption window" is a measured number or the honest words "not
   yet measured" — never a guess dressed as a fact. The same applies to the compatibility matrix: a component's
   version here is what actually shipped, read from `VERSION`, `GET /api/version` and the compose file, never typed
   from memory.
3. **One release, whatever its size.** A release with nothing user-visible still fills in "What changed" — "no
   user-visible change; internal refactor and dependency updates" is a complete and honest answer.

---

## Release `<VERSION>`

| Field | Value |
| --- | --- |
| Version | `<VERSION>` (from the repository's `VERSION` file) |
| Build | `<VERSION>+<SourceRevisionId>` (from `GET /api/version`'s `current`) |
| Tagged | `<date>` |
| Released to production | `<date>`, or **not yet released** |

## 1. What changed

<!-- One bullet per user-visible change, in plain language a shop owner or a tailor would recognise — not a
     changelog of commit messages. Group by area (intake, custody, billing, reporting, …) if there are several.
     Link the issue each change closes. -->

-

## 2. Expected interruption window

<!-- The number a release note states here is not invented by this template (docs/process/versioning.md section 4
     names why): it is the measured record of a deployment rehearsal (E15-F01-6), and until that record exists the
     honest value is "not yet measured", with the issue that will measure it named so a reader knows it is coming
     rather than forgotten. docs/nfr/traceability.md NFR-AV-06 is the target this measurement proves. -->

**Not yet measured** — the deployment rehearsal that measures a container swap's interruption window is
[E15-F01-6](https://github.com/MK-AIFy/HyFib-Tailor360/issues/464). Until it has run at least once, this field
carries no number.

## 3. Compatibility matrix for this release

<!-- One row per component, per docs/process/versioning.md section 3. "May run with" states what this release's
     build of that component tolerates during a rollback or a staged deployment, not what it prefers. -->

| Component | Version | May run with |
| --- | --- | --- |
| Progressive web application | `<VERSION>` | The API and worker of this release, and the API of the previous release for the length of a rollback |
| API | `<VERSION>` (`v1`) | Any PWA build whose `X-Client-Version` is at or above the minimum supported client below |
| Database (schema) | `<schemaVersion from GET /api/version>` | The API of this release, and the API of the release immediately before it |
| Worker | `<VERSION>` | The database and API of this release |

### 3.1 Third-party components carried by this release

<!-- Read from infra/compose/docker-compose.staging.yml at the tagged commit; do not restate from memory. Only list
     a row that changed since the previous release, or write "unchanged from the previous release". -->

| Component | Image |
| --- | --- |
| | |

## 4. Minimum supported client

<!-- The value of ClientCompatibilityOptions:MinimumClientVersion this release configures, and whether it moved.
     docs/process/versioning.md section 4 is the two-release rule this field is governed by: a release only raises
     this the release AFTER the change that requires it. -->

| Field | Value |
| --- | --- |
| `minimumClient` after this release | `<value, or empty for "answers every build">` |
| Raised in this release | **Yes** / **No** |
| If yes, which release introduced the requirement | `<version>` |

## 5. Rollback

<!-- What an operator does if this release needs to be rolled back, and what stays compatible while they do it —
     read docs/process/versioning.md section 5 before writing this, and do not restate its two rules; cite them. -->

Rolling back to the previous release's image redeploys the previous `VERSION`. The database schema is expand-only
within the compatibility window (`docs/process/versioning.md` section 3), so the previous release's API continues
to read it unchanged. No manual data step is required unless this release's own migration notes say otherwise.
