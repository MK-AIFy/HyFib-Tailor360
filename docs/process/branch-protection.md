# Branch protection and repository settings

This document states exactly how `main` is protected: which status checks must pass before a merge, who may
approve and merge, whether force-pushes and deletions are blocked, where deployment secrets are allowed to exist,
and how anybody can verify that the settings actually are what this page says. It exists because
[`../nfr/traceability.md`](../nfr/traceability.md) target **NFR-MQ-02** — "no direct commits to `main`; every change
arrives through a reviewed pull request linked to one issue" — is proved by a configuration, and a configuration
nobody wrote down is a configuration nobody can audit. Read it with
[`definition-of-done.md`](definition-of-done.md) (what the reviewer checks) and
[`release-gates.md`](release-gates.md) (what a release must carry).

---

## 1. Status and scope

| Field | Value |
| --- | --- |
| Status | **Draft — proposed settings**; binding once the Owner applies them and the verification of section 9 is recorded |
| Drafted | 2026-09-05, issue #22, wave W1 |
| Owner of the document | Technical reviewer, with the repository Owner as the only person who can apply the settings |
| Applies to | The `main` branch of `MK-AIFy/HyFib-Tailor360`, and to every `release/*` branch once #59 creates the first one |
| Proves | **NFR-MQ-02** (no direct commits to `main`) and closes **DOD-OD-01** once the review count is confirmed |
| Depends on | The plan the repository is hosted under — see section 6 — and on [`../../.github/CODEOWNERS`](../../.github/CODEOWNERS) carrying handles GitHub accepts |
| Review cadence | At every wave exit gate, and immediately after any change to a workflow job name |

**The rule that governs the rest.** Every setting below is stated as a value, not as an intention, and section 9
gives the command that reads the value back. A protection rule that cannot be read back has not been verified; it
has been assumed.

---

## 2. What the settings must achieve

Four properties, in the order they matter. Each row of sections 3 to 5 exists to hold one of them up.

| # | Property | Broken by |
| --- | --- | --- |
| **P1** | No change reaches `main` except through a pull request | A direct push, an administrator bypass, an unprotected branch pattern |
| **P2** | No pull request merges while a quality gate is red | A missing required check, a renamed job nobody re-required, a check that never reports |
| **P3** | Every merge has been read by a second pair of eyes, or the single-maintainer exception is a recorded, dated decision rather than a habit | A self-approval nobody agreed to, an inert `CODEOWNERS` file |
| **P4** | History is not rewritten and the branch cannot vanish | A force-push, a branch deletion, a merge strategy that leaves a non-linear history |

---

## 3. Required status checks on `main`

A required status check is matched **by the name GitHub displays**, which is the workflow job's `name:` value, not
the job's key. The names below are read from the workflow files and are the complete set as of this document's
date.

| Check name to require | Workflow | Job key | What it gates |
| --- | --- | --- | --- |
| `Pull-request policy` | [`../../.github/workflows/pr-policy.yml`](../../.github/workflows/pr-policy.yml) | `policy` | Exactly one open linked issue, the branch-name convention, and the mandatory evidence-checklist items once the pull request is out of draft |
| `.NET build and tests` | [`../../.github/workflows/ci.yml`](../../.github/workflows/ci.yml) | `build-test-dotnet` | Restore, format check, build with warnings as errors, and the unit, architecture, contract and integration tiers with coverage collection |
| `PWA build and tests` | [`../../.github/workflows/ci.yml`](../../.github/workflows/ci.yml) | `build-test-pwa` | Lint, type-check, unit tests and the production build of `clients/pwa` |
| `Documentation links` | [`../../.github/workflows/ci.yml`](../../.github/workflows/ci.yml) | `documentation` | The link checker's self-test, then every relative link in `docs/`, `.github/`, the repository-root markdown and the per-tree `CLAUDE.md` and `README.md` guides |
| `Security checks` | [`../../.github/workflows/ci.yml`](../../.github/workflows/ci.yml) | `security` | Secret scanning over the whole history, and dependency review when the dependency graph is available. The scanners live in the four jobs below, not in this one |
| `Database migrations` | [`../../.github/workflows/ci.yml`](../../.github/workflows/ci.yml) | `migration-check` | The migrations apply to an empty PostgreSQL 16 database, a second run changes nothing, and the model has no pending changes against the snapshot. Proves **RG-12** per pull request |
| `CodeQL (csharp)` | [`../../.github/workflows/ci.yml`](../../.github/workflows/ci.yml) | `codeql` | Static analysis of the .NET code, gated at high severity by `scripts/sarif-gate.py` whether or not the alerts page is available. Proves **RG-10** |
| `CodeQL (javascript-typescript)` | [`../../.github/workflows/ci.yml`](../../.github/workflows/ci.yml) | `codeql` | The same over `clients/pwa`. The job is a matrix over two languages and GitHub reports **one check run per language**, so both names are required separately — requiring only the job key requires neither |
| `Container and dependency scan` | [`../../.github/workflows/ci.yml`](../../.github/workflows/ci.yml) | `infrastructure-scan` | Trivy over the container definitions (gated at critical) and over the dependency tree (gated at high, fixed advisories only). Proves **RG-08** while dependency review is unavailable |

**Deliberately not required**, and both omissions are decisions rather than oversights:

- `release-ready label` (`pr-policy.yml`, job `label`). It is skipped on a pull request from a fork and on a
  cancelled run, and a required check that can legitimately be skipped never reports, which blocks every merge
  behind it for ever. Its output is a label, and the label is advisory to the human merging.
- `Software bill of materials` (`ci.yml`, job `sbom`). It produces an artefact rather than a verdict, and
  [`release-gates.md`](release-gates.md) places **RG-11** — the gate the document serves — at the release and the
  nightly run, not at every pull request. It still runs on every pull request so that a build which cannot produce
  the document is visible early, and its failure is read rather than merged past. Requiring it would put a second
  full build of the 66-project solution on the merge path for a gate that is not a per-pull-request gate.

### 3.1 Keeping this list correct

Two failure modes, both silent, both worth more care than they look:

1. **A job is renamed.** The old name is still required, never reports again, and every pull request stalls with
   "Expected — waiting for status to be reported".
2. **A job is added.** It runs, it can fail, and nothing stops the merge, because only the listed names are
   required.

The rule that prevents both: **a pull request that adds, renames or removes a job in `ci.yml` or `pr-policy.yml`
changes the table in section 3 in the same pull request, and the Owner updates the branch-protection setting before
that pull request merges.** The reviewer checks this as part of Definition-of-Done item 8.

---

## 4. Protection rules on `main`

| Setting | Value | Why |
| --- | --- | --- |
| Require a pull request before merging | **On** | P1. This is the setting that makes every other one meaningful |
| Required approving reviews | **1** — **proposed, to be confirmed** (**BP-OD-01**) | P3. One reviewer is the most a two-person project can sustain; see section 5 for the single-maintainer period |
| Dismiss stale pull-request approvals when new commits are pushed | **On** | An approval describes the commits the reviewer read, not the branch's name |
| Require review from Code Owners | **On, once section 5.2 is resolved** | P3. Inert until `CODEOWNERS` carries handles GitHub accepts |
| Require approval of the most recent reviewable push | **On** | Stops "approve, then push the real change" — including by the author themselves |
| Require status checks to pass before merging | **On**, with exactly the nine check names in section 3 — the two CodeQL legs counted separately | P2 |
| Require branches to be up to date before merging | **Off** — **proposed** | A single maintainer merging serially would re-run the whole pipeline for every merge, twice, against a 15-minute budget. `ci.yml` also runs on `push` to `main`, so a semantic conflict is caught within one pipeline of landing. Revisit when #59 introduces a merge queue |
| Require conversation resolution before merging | **On** | An unresolved review thread is an open question, and merging past it is how questions get lost |
| Require signed commits | **Off** — **proposed** (**BP-OD-04**) | Signing is worth having and costs a key-management decision this project has not made; #59 signs *artefacts*, which is the property releases actually rely on |
| Require linear history | **On** | P4, and it pairs with squash-only merging in section 7 so that one merge is one commit is one issue |
| Allow force pushes | **Off** (blocked) | P4. A force-push to `main` destroys the history every audit, revert and `git bisect` depends on |
| Allow deletions | **Off** (blocked) | P4 |
| Do not allow bypassing the above settings | **On** — **proposed** (**BP-OD-01**) | An administrator exemption is the setting that quietly turns every rule above into a suggestion |
| Restrict who can push to matching branches | Not used | With pull requests required and bypass off, there is nobody left to restrict |
| Lock branch (read-only) | **Off** | `main` is the integration branch |

---

## 5. Who may approve, and who may merge

### 5.1 The rule, and the single-maintainer exception

The merging person is the author or the reviewer; the *approving* person is never the author. That is the whole
rule, and the honest problem is that this project currently has one maintainer, so applying it literally would stop
all work.

**The interim arrangement — proposed, to be confirmed as DOD-OD-01 / BP-OD-01:**

| Situation | Approval | Merge |
| --- | --- | --- |
| Two or more maintainers with write access | One approving review from a person who is not the author, and from a code owner where `CODEOWNERS` claims the path | The author, after the approval |
| A single maintainer, before a second one is added | The author records the self-review explicitly in the pull request — what they re-read, and what they checked against the Definition of Done — and the bypass setting stays **on** only for the accounts named in the decision | The maintainer |
| Any change to `/.github/`, `/infra/` or `/src/Platform/` | The same, plus the code-owner rule of section 5.2 once it is live | The maintainer |

The exception is recorded, not silent, and it ends when a second maintainer is added. A self-approval that is not
written down is indistinguishable from no review at all.

### 5.2 The `CODEOWNERS` prerequisite

[`../../.github/CODEOWNERS`](../../.github/CODEOWNERS) currently names two **team** handles. Team handles exist only
in an organisation; this repository is owned by a personal account, where GitHub rejects the file as invalid and
applies **none** of its rules — including the broad `*` rule. The consequence matters: "Require review from Code
Owners" can be switched on today and will change nothing.

Two ways out, and the choice is **BP-OD-03**:

| Option | What changes | Consequence |
| --- | --- | --- |
| Replace every team handle with the personal handle `@MK-AIFy` | One line per rule in `CODEOWNERS` | Code-owner review works immediately; the path rules become a review-routing hint rather than a separation of duties, because the owner owns every path |
| Transfer the repository to an organisation and create the two teams | Repository ownership, and the plan in section 6 | Code-owner review works and the paths route to different people; this is the end state #59 assumes |

Until one of them is done, section 4's "Require review from Code Owners" row is **not yet applicable**, and the
pull-request template plus the reviewer's obligations in
[`definition-of-done.md`](definition-of-done.md) are what actually enforce review.

---

## 6. What this repository's plan can and cannot enforce

This matters more than any individual setting, and stating it plainly is the point of this section: **a document
that describes protection the account cannot apply is worse than no document, because it reads as proof.**

| Mechanism | Availability on a **private** repository | Consequence here |
| --- | --- | --- |
| Classic branch protection rules | GitHub Pro, Team or Enterprise. Not available on GitHub Free for a private repository | Everything in section 4 depends on the account holding at least Pro |
| Repository rulesets | GitHub Team or Enterprise for private repositories | Not available on a personal account; section 4 is written for classic protection for that reason |
| Required status checks | Part of the mechanisms above | The checks still **run** and still **report** on every pull request whatever the plan; only the ability to *block a merge* is plan-gated |
| Environments with required reviewers and secrets | Public repositories on any plan; private repositories need Pro or above | Section 8's deployment policy needs the same plan as section 4 |
| GitHub Actions, Dependabot, secret-scanning push protection | Available | The gates themselves are not at risk |
| Code scanning alerts, dependency review API, dependency-graph submission | Advanced Security — organisation and enterprise only | Why `ci.yml` gates its dependency review behind a repository variable rather than failing on every run |

**If the repository is on a plan without branch protection**, the degradation is explicit and is not a secret:

1. Every check in section 3 still runs on every pull request and its result is visible before the merge button.
2. The `release-ready` label of section 7 is the single visible "the policy check passed" signal, applied by the
   check alone; the standing rule is that **nothing merges without it**, and a merge without it is a finding for the
   wave retrospective, not an ordinary event.
3. `ci.yml` runs again on `push` to `main`, so a bad merge is red within one pipeline rather than at the next
   release.
4. The gap is recorded as **BP-OD-02** and carried in the release evidence, because "the process is enforced by
   convention" is a risk statement, not a process.

---

## 7. Repository settings this document also fixes

| Setting | Value | Why |
| --- | --- | --- |
| Merge button: allow merge commits | **Off** | One merge is one commit is one issue, which is what makes `git revert` a usable rollback |
| Merge button: allow squash merging | **On**, default commit message "Pull request title and description" | The squash commit carries `Refs #NN`, so the merge is traceable from `git log` alone |
| Merge button: allow rebase merging | **Off** | It splits one pull request across several commits on `main`, which breaks the one-commit revert |
| Automatically delete head branches | **On** | A merged branch that lingers gets pushed to again |
| Allow auto-merge | **On** | Safe only in combination with section 4: auto-merge waits for the required checks. On a plan without branch protection, leave it **off** |
| Actions: default workflow token permissions | **Read repository contents and packages permissions** | Both workflows declare their own `permissions:` blocks; the repository default is the floor for anything that forgets to |
| Actions: allow GitHub Actions to create and approve pull requests | **Off** | An automation that can approve a pull request defeats section 5 entirely |
| Label `release-ready` | Created once, colour `#0e8a16`, description "Applied only by the pull-request policy check" | The `label` job creates it on first use where the token allows; if it cannot, create it by hand once and the job stops failing. **No person applies or removes this label** — it means "the policy check passed on the current head", and a hand-applied one is a lie the merge button believes. **One exemption, and it is deliberate:** a pull request from an automation account (Dependabot) never gets the label, because the policy job does not evaluate its rules against a bot — branch names and evidence checklists describe human work — so a green `Pull-request policy` there means "not checked", not "passed". Those pull requests are read by a person instead |

---

## 8. Deployment secrets, Environments and workflow tokens

The plan's rule, restated here because this is the document a person reads before adding a secret:

| Rule | Where it is enforced |
| --- | --- |
| Deployment secrets exist **only** in GitHub Environments, never as repository or organisation secrets | Repository settings; a repository-level deployment secret is a review finding |
| Every Environment that can reach a real system has **required reviewers**; `production` additionally has a wait timer and a deployment-branch rule limiting it to tags | Environment settings, applied with the release workflow of #59 |
| A pull request from a fork receives **no secrets** and a read-only token | A property of the `pull_request` event, which both workflows use. Neither workflow uses `pull_request_target`, and the one job that needs to write (`label`) skips fork pull requests rather than reaching for a token that could |
| `id-token: write` appears **only** in the release workflow of #59, and in no other job | Read at every workflow change by the reviewer. It is **not** enforced by tooling: the `CODEOWNERS` rule on `/.github/` that would route the review is inert until **BP-OD-03** (section 5.2), so this is a convention today, not a control. Nothing in `ci.yml` or `pr-policy.yml` requests it |
| Every third-party action is pinned by full commit SHA with the tag in a trailing comment | The policy block at the top of `ci.yml`; Dependabot raises the pins |
| The staging environment holds no production secret and no live provider credential | [`../../infra/compose/docker-compose.staging.yml`](../../infra/compose/docker-compose.staging.yml), which reads every secret from a file under a staging-only directory |

The current state, for the avoidance of doubt: **no Environment exists, no deployment secret exists, and no
workflow requests `id-token`.** This section is the rule the first one must be created under.

---

## 9. How the settings are verified

Verification is a command whose output is pasted into the evidence, not a screenshot of a settings page — a
screenshot proves what one tab looked like once.

### 9.1 Read the protection back

```bash
# Requires admin rights on the repository.
gh api repos/MK-AIFy/HyFib-Tailor360/branches/main/protection --jq '{
  required_checks: .required_status_checks.contexts,
  strict: .required_status_checks.strict,
  reviews: .required_pull_request_reviews.required_approving_review_count,
  dismiss_stale: .required_pull_request_reviews.dismiss_stale_reviews,
  code_owner_reviews: .required_pull_request_reviews.require_code_owner_reviews,
  last_push_approval: .required_pull_request_reviews.require_last_push_approval,
  conversation_resolution: .required_conversation_resolution.enabled,
  linear_history: .required_linear_history.enabled,
  force_pushes: .allow_force_pushes.enabled,
  deletions: .allow_deletions.enabled,
  enforce_admins: .enforce_admins.enabled
}'
```

The expected answer, matching section 4:

| Field | Expected |
| --- | --- |
| `required_checks` | The nine names of section 3, and no others. A tenth name, or a name section 3 does not list, means the table and the setting have drifted |
| `strict` | `false` |
| `reviews` | `1` |
| `dismiss_stale`, `last_push_approval`, `conversation_resolution`, `linear_history`, `enforce_admins` | `true` |
| `code_owner_reviews` | `true` once section 5.2 is resolved; `false` until then |
| `force_pushes`, `deletions` | `false` |

A `404` from this command means one of two things and they are not interchangeable: the branch has no protection at
all, or the account's plan does not offer it. Section 6 says which, and the answer goes into the evidence either
way.

### 9.2 Prove the protection actually blocks

A configuration read-back proves the setting exists; only an attempt proves it bites. Once per wave exit gate, and
recorded in the wave's evidence:

| Attempt | Expected result |
| --- | --- |
| `git push` a trivial commit directly to `main` from a clean clone | Rejected by the remote |
| Open a pull request with a deliberately unchecked mandatory checklist item | `Pull-request policy` red, merge blocked, no `release-ready` label |
| Open a pull request whose branch is named `my-branch` | `Pull-request policy` red, merge blocked |
| Force-push to `main` | Rejected |
| Attempt to delete `main` | Rejected |

These attempts are part of the deliberately-broken-branch evidence #22 records in
[`workflow-demo.md`](workflow-demo.md), so they are run once and cited from both places.

### 9.3 Cadence and evidence

| When | What is done | Where it is recorded |
| --- | --- | --- |
| When the settings are first applied | 9.1 and 9.2 in full | This document's status row, and the issue #22 evidence |
| Every wave exit gate | 9.1, plus 9.2 for anything that changed | The wave's exit-gate record |
| Every release | 9.1 output attached | [`release-evidence.md`](release-evidence.md), alongside the waiver register |
| Whenever a workflow job is added or renamed | Section 3's table and the required-check list, in the same pull request | The pull request itself |

---

## 10. Open decisions recorded by this document

| ID | Question | Blocks | Owner | Status |
| --- | --- | --- | --- | --- |
| **BP-OD-01** (= **DOD-OD-01**) | The number of approving reviews on a pull request into `main`, and whether the maintainer may approve their own work during the single-maintainer period — and if so, under what recorded conditions | Section 4's review count and bypass rows; the W1 exit gate | Business owner, with the technical reviewer | **Open** — section 5.1 states the proposed interim arrangement |
| **BP-OD-02** | Whether the repository moves to a plan (or to an organisation) that can enforce branch protection on a private repository, and by when | Whether P1 to P4 are enforced or merely documented | Business owner | **Open** — section 6 states the degradation until it is answered |
| **BP-OD-03** | Personal handles in `CODEOWNERS` now, or an organisation with two teams | Code-owner review; the separation of duties on `/infra/`, `/.github/` and `/src/Platform/` | Business owner | **Open** — section 5.2 |
| **BP-OD-04** | Whether commits must be signed, and who manages the keys | Section 4's signed-commits row | Technical reviewer | **Proposed** — off for now; artefact signing in #59 is the property releases depend on |

---

## 11. Related documents

| Document | Why it matters here |
| --- | --- |
| [`definition-of-done.md`](definition-of-done.md) | The nine items the `Pull-request policy` check partly enforces, and the rule that items 1, 3, 6 and 9 always apply |
| [`definition-of-ready.md`](definition-of-ready.md) | What must be true of the issue before the branch this document protects is cut |
| [`release-gates.md`](release-gates.md) | The gates a release passes; several are the same checks run over a whole release |
| [`release-evidence.md`](release-evidence.md) | The per-release record the section 9 output is attached to |
| [`waivers.md`](waivers.md) | Where a deliberate exception to any rule above is written down, with an expiry |
| [`workflow-demo.md`](workflow-demo.md) | The worked example that exercises these settings end to end, including the rejections of section 9.2 |
| [`../../.github/CODEOWNERS`](../../.github/CODEOWNERS) | The file section 5.2 depends on |
| [`../../.github/workflows/pr-policy.yml`](../../.github/workflows/pr-policy.yml) | The required check, and the only thing that applies `release-ready` |
| [`../../.github/workflows/ci.yml`](../../.github/workflows/ci.yml) | The eight other required check names, and the action-pinning policy of section 8 |
| [`../nfr/traceability.md`](../nfr/traceability.md) | **NFR-MQ-02**, the target this configuration proves |
