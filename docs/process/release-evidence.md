# Release evidence checklist — the per-release record

This is the artefact a release fills in. [`release-gates.md`](release-gates.md) says *which* gates a release must
pass and section 6 of that document lists the sixteen items the record must carry; this document turns that list
into a form with one row per item, a place to put the link, a place to name who confirmed it and a date. Gate
**RG-14** is passed by a completed copy of this form and by nothing else: the deployment workflow of #59 refuses to
run without one.

It exists because every other gate produces evidence and this is the gate that makes the evidence findable a year
later — when the accountant asks how a GST figure was arrived at, when an auditor asks what was scanned, or when
the next engineer asks why a release shipped with a known gap.

---

## 1. Status and scope

| Field | Value |
| --- | --- |
| Status | **Draft — proposed form**; binding once the stakeholder review in [`../nfr/reviews/stakeholder-review.md`](../nfr/reviews/stakeholder-review.md) is signed |
| Drafted | 2026-09-05, issue #22, wave W1 |
| Owner of the document | Technical reviewer, with the Owner as approver |
| Applies to | Every tagged release deployed to production, and to the release rehearsals of #59, #60 and #61b |
| Enforces | **RG-14** of [`release-gates.md`](release-gates.md) |
| Completed by | The technical reviewer, with the confirmations of section 5 collected from their named owners |
| Where a completed copy lives | The body of the GitHub Release for the tag, and indexed in `docs/launch/release-evidence-index.md` (#61c) |

---

## 2. How to use this form

1. **Copy sections 3 to 9 into the release's draft record** when the release candidate is cut — not after the
   deployment. A form filled in afterwards records what happened; this one is meant to decide whether it happens.
2. **Every row resolves to a link or to a pasted artefact.** "Green", "done" and "as usual" are not evidence. A row
   whose evidence column says only "yes" is an unfilled row.
3. **A named person confirms each row**, and it is not always the same person: the security rows are the security
   owner's, the restore row is the operations owner's, and the financial rows are the Owner's with the accountant.
   The gate owners are defined in [`release-gates.md`](release-gates.md) section 2.2.
4. **Nobody confirms their own work** where a second person exists to confirm it. During a single-maintainer
   period the same exception as [`branch-protection.md`](branch-protection.md) section 5.1 applies: the
   self-confirmation is written down, with what was re-checked.
5. **An item that does not apply is marked `n/a` with a one-line reason** — never deleted. A deleted row is
   indistinguishable from a forgotten one, which is the failure this form exists to prevent.
6. **An unfinished row blocks the release.** RG-14 carries no waiver, because a release nobody can reconstruct is
   the one failure the waiver register itself could not survive.

---

## 3. Release identification

| Field | Value |
| --- | --- |
| Release tag | |
| Release name | |
| Commit SHA on `main` | |
| Previous released tag | |
| Image digests promoted (web, worker, PWA, CLI) | |
| Target environment | staging rehearsal / production |
| Planned deployment window and expected interruption | |
| Release manager | |
| Rollback decision owner | |
| Date the record was completed | |

---

## 4. Gate results

One row per gate in [`release-gates.md`](release-gates.md) section 4. `pass`, `fail`, `waived` (with the waiver
identifier) or `n/a` (with the reason).

| Gate | What it proves | Result | Run or report link |
| --- | --- | --- | --- |
| **RG-01** | Build, analyzers, format, lint, type-check | | |
| **RG-02** | Unit and architecture tests | | |
| **RG-03** | Integration tests and the authorisation matrix | | |
| **RG-04** | Contract tests and the OpenAPI difference | | |
| **RG-05** | End-to-end journeys across the browser and device matrix | | |
| **RG-06** | Accessibility scan | | |
| **RG-07** | Performance budget | | |
| **RG-08** | Dependency and licence scan | | |
| **RG-09** | Secret scan | | |
| **RG-10** | Static analysis | | |
| **RG-11** | Image and infrastructure-as-code scan, bill of materials, signature, provenance | | |
| **RG-12** | Migration compatibility | | |
| **RG-13** | Backup restore verification | | |
| **RG-14** | This record | | |

---

## 5. The evidence checklist

The sixteen items of [`release-gates.md`](release-gates.md) section 6, keyed so that a review comment can say
"RE-09" and mean one thing. **Present** is `yes`, `no` or `n/a`; a `no` blocks the release.

| ID | Item | Produced by | Present | Where the artefact is | Confirmed by | Date |
| --- | --- | --- | --- | --- | --- | --- |
| **RE-01** | The promoted image digest, its signature and its provenance attestation | RG-11 | | | Technical reviewer | |
| **RE-02** | Build, analyzer, format, lint and type-check results | RG-01 | | | Technical reviewer | |
| **RE-03** | Test tier summary — unit, property, architecture, integration, contract — with counts and coverage | RG-02 to RG-04 | | | Technical reviewer | |
| **RE-04** | The authorisation-matrix report: every endpoint with its role and branch expectations | RG-03 | | | Technical reviewer | |
| **RE-05** | The OpenAPI difference report and the endpoint inventory | RG-04 | | | Technical reviewer | |
| **RE-06** | The end-to-end regression report with the per-journey pass matrix by browser and device profile | RG-05 | | | Technical reviewer | |
| **RE-07** | The accessibility report and the accepted-violation list | RG-06 | | | Owner | |
| **RE-08** | The performance report: Lighthouse budgets, k6 summary, measured percentiles against the service level objectives | RG-07 | | | Technical reviewer | |
| **RE-09** | The security scan summary — dependencies, licences, secrets, static analysis, image and infrastructure as code — with every finding's severity and status | RG-08 to RG-11 | | | Security owner | |
| **RE-10** | The software bill of materials | RG-11 | | | Security owner | |
| **RE-11** | The migration dry-run output, the rollback note and the recorded recovery target | RG-12 | | | Technical reviewer | |
| **RE-12** | The most recent restore verification record, with its measured recovery point and recovery time and its age in days | RG-13 | | | Operations owner | |
| **RE-13** | Every active waiver applying to this release, with its expiry and review date — see section 6 | [`waivers.md`](waivers.md) | | | Owner | |
| **RE-14** | The release notes: what changed, the expected interruption window, the compatibility matrix for the progressive web application, the API, the database and the worker, and the minimum supported client | #59 | | | Technical reviewer | |
| **RE-15** | The user-acceptance-testing sign-off link, and — for any release changing pricing, tax, invoice layout, rounding or a financial report — the accountant's approval of the GST output | #61c | | | Owner, with the accountant | |
| **RE-16** | The go or no-go record naming the decision owner, the rollback decision owner and the criteria used — see section 8 | #61c | | | Owner | |

**RE-15 and RE-16** apply to the go-live release and to every subsequent release that touches the financial
surface. A release that touches neither records that fact in the row rather than omitting it.

---

## 6. Waivers carried by this release

Copied from [`waivers.md`](waivers.md). **An expired waiver blocks the release**, so the expiry column is checked
against the deployment date and not against the date the waiver was granted.

| Waiver | Gate | Risk carried | Owner | Expiry | Corrective issue | Expired? |
| --- | --- | --- | --- | --- | --- | --- |
| | | | | | | |

If the release carries no waiver, write "None" in the first cell rather than leaving the table empty — the two
statements look identical on the page and mean very different things.

---

## 7. Configuration attested for this release

The evidence above describes the code. This section describes the machinery that produced it, because a green
pipeline proves nothing if the pipeline itself was changed to get there.

| Item | Value or link | Confirmed by |
| --- | --- | --- |
| Branch-protection read-back for `main` — the output of [`branch-protection.md`](branch-protection.md) section 9.1 | | Technical reviewer |
| Required status checks at the time of the merge, matching `branch-protection.md` section 3 | | Technical reviewer |
| Any gate re-run on this candidate, with the reason for each re-run | | Technical reviewer |
| Any test quarantined during this release, with its linked issue and date | | Technical reviewer |
| Workflow or gate configuration changed since the previous release, with the pull request | | Technical reviewer |
| Environments and their required reviewers, with the deployment-branch rule | | Owner |

---

## 8. Go or no-go

| Field | Value |
| --- | --- |
| Decision | **go** / **no-go** |
| Decision owner | |
| Rollback decision owner | |
| Criteria used | |
| Known gaps accepted, and the waiver each is recorded under | |
| Deployment start and end | |
| Post-deployment verification performed, and by whom | |
| Rollback executed? If so, why, and the record | |

### Sign-off

| Role | Name | Confirms | Date |
| --- | --- | --- | --- |
| Technical reviewer | | RE-01 to RE-06, RE-08, RE-11, RE-14 and section 7 | |
| Security owner | | RE-09 and RE-10 | |
| Operations owner | | RE-12 | |
| Owner | | RE-07, RE-13, RE-15, RE-16 and the go or no-go decision | |
| Accountant | | The GST output, where RE-15 requires it | |

---

## 9. Where this record goes afterwards

| Step | Who | When |
| --- | --- | --- |
| The completed record is pasted into the body of the GitHub Release for the tag | Release manager | Before the deployment workflow runs |
| The release is linked from every issue closed in it | Release manager | At the release |
| The record is indexed in `docs/launch/release-evidence-index.md` | #61c | At the release |
| Any `n/a` row and any waiver is reviewed | Technical reviewer, with the gate owner | At the next release train |
| A row that is `n/a` in three consecutive releases is challenged: either the gate does not apply to this product and the checklist changes, or it is quietly being skipped | Technical reviewer | Wave retrospective |

---

## 10. Related documents

| Document | Why it matters here |
| --- | --- |
| [`release-gates.md`](release-gates.md) | The gates in section 4, the sixteen items in section 5, and the severities that decide whether a `fail` can be waived |
| [`waivers.md`](waivers.md) | The register section 6 copies from, and the expiry rule that blocks the release |
| [`definition-of-done.md`](definition-of-done.md) | The per-pull-request evidence that adds up to this record |
| [`branch-protection.md`](branch-protection.md) | The configuration attested in section 7, and the read-back command that produces it |
| [`../nfr/slo.md`](../nfr/slo.md) | The recovery-point, recovery-time and latency targets RE-08 and RE-12 are measured against |
| [`../nfr/security-operations-targets.md`](../nfr/security-operations-targets.md) | The vulnerability service levels RE-09 reports against |
| [`../nfr/traceability.md`](../nfr/traceability.md) | Which non-functional target each item proves |
| [`../templates/threat-model.md`](../templates/threat-model.md) | The residual-risk table RE-09 draws its accepted risks from |
