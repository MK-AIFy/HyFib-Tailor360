# Work breakdown — the open tracker turned into session-sized units

This document breaks the open feature issues of [`../IMPLEMENTATION_PLAN.md`](../IMPLEMENTATION_PLAN.md) into units
one coding session can finish, orders them, and records what the breakdown found wrong with the tracker on the way.
It is a planning aid, not a specification: **the authority for the scope of any piece of work is the GitHub issue**,
and where this document and an issue body disagree, the issue wins and this document is corrected.

---

## 1. Purpose and status

| Field | Value |
| --- | --- |
| Status | **Draft — proposed breakdown.** Binding on a session only once the children below are filed and the owner decisions of [section 7.4](#74-decisions-the-owner-must-make-before-a-session-starts) are answered |
| Drafted | 2026-09-13, against `main` at `3bcd74c`, from branch `claude/github-issues-planning-v1mu7r` |
| Owner of the document | Technical reviewer |
| Authority for scope | The GitHub issue. This document indexes; the issue body carries scope, out of scope, acceptance criteria and the evidence list |
| Read with | [`definition-of-ready.md`](definition-of-ready.md), [`definition-of-done.md`](definition-of-done.md), [`release-gates.md`](release-gates.md), plan Sections 6.2, 6.4, 7 and 8–9 |
| Covers | **256 units across 24 parents**, about 193,990 estimated production lines, plus the 35 open issues already session-sized, and the schedule over all of them |
| Does not cover | The per-unit detail. Every unit has a full body — scope, out of scope, acceptance criteria with a negative and an exception case, data classification, permission keys, migration shape, test tiers and evidence list — filed on its own issue |

**What this is for.** Several Sonnet sessions will work this backlog concurrently, each in its own container, each able
to see only its own issue. A session cannot discover that the issue beside it is about to rewrite the same file, that
its dependency is a plan identifier rather than a real issue, or that the premise in its issue body is false against
`main`. This document is where those facts live, so that a human can approve the shape of the work once and each
session can then be handed exactly one unit of it.

**What it is not.** It is not a substitute for reading the issue. The tables here carry only what is needed to choose
and order the work: what a unit delivers in one line, what it waits on, and how big it is.

**The honest summary, in four sentences.** The 46 open feature issues and follow-ups decompose into **256 units of
about 193,990 production lines**, which is the real remaining size of this platform and is several hundred sessions of
work, not several dozen. The dependency graph is **acyclic and 12 levels deep**, with 46 units at level 0 and
**25 startable against `main` today**. Of the four files every schedule founders on — the OpenAPI document, the
permission matrix with `matrix.yaml`, `SystemRoles.cs` and the client router — the first is now claimed by 63 separate
units, which is why [section 6](#6-contended-files--the-register-that-replaces-pairwise-notes) exists and why real
concurrency is about five sessions rather than the eleven the level widths suggest. Seventeen of the parents had no
children at all before this pass, and they were not small: they hold the stock ledger, the custody chain, the
production workflow, the delivery gate, the Playwright suite and every operational gate for release.

---

## 2. How a session picks up work

These rules are additions to [`../../CLAUDE.md`](../../CLAUDE.md) section 6 and the Definition of Done, not
replacements for them. A session that follows only these and not those will fail review.

1. **One sub-issue, one branch, one pull request.** The branch is `feat|fix|docs/eXX-fYY[a-c]-<slug>`, all lower
   case, so the child keyed `e07-f01-5` takes `feat/e07-f01-5-durable-print-queue`. `.github/workflows/pr-policy.yml`
   holds the pattern it is matched against; the body links exactly one issue, as `Refs #NN` or `Closes #NN`.
2. **Read the issue body first, in full, before reading any code.** Each body states what is already merged and must
   not be rebuilt, what is deliberately out of scope, and which of its premises were verified against `main`. Several
   bodies exist specifically to stop a session re-implementing something that shipped.
3. **Check the Definition of Ready before cutting the branch.** [`definition-of-ready.md`](definition-of-ready.md)
   DOR-01..DOR-16 is the gate. DOR-05 (a threat model covers the flow) and DOR-10 (the permission and scope are
   named) are the two that fail most often in this batch — see
   [section 7.1](#71-re-scopes-needed-before-those-branches-are-cut) and
   [section 7.4](#74-decisions-the-owner-must-make-before-a-session-starts). If the issue does not pass,
   say so on the issue and stop; do not start and improvise.
4. **Respect the concurrency slot.** Two sessions may run at once only when they touch no common module, migration
   chain, `DbContext`, host registration file, or any of the serialisers in
   [section 6](#6-contended-files--the-register-that-replaces-pairwise-notes). The slot, not the module, is the unit
   of isolation.
5. **A new endpoint is a five-file change** — the route, the permission matrix at
   [`../security/permission-matrix.md`](../security/permission-matrix.md),
   `tests/Tailor360.IntegrationTests/Authorization/matrix.yaml`, `Identity.Application/Access/SystemRoles.cs` and
   `docs/api/openapi.v1.json`. Both matrix files are parsed by tests, so a careless merge of them is green and wrong.
   Append to your own block only.
6. **Rebase before regenerating.** Run `pnpm --dir clients/pwa generate:api` after rebasing, never before, or
   `generate:api:check` fails on drift that is not yours.
7. **A module-founding session owns its schema's first migration.** Two founding migrations for one schema is the one
   collision this schedule cannot recover from cheaply.
8. **When the issue turns out to be bigger than one session, split it and say so.** Post the split you propose on the
   issue — the sub-units, what each delivers, which one you are taking — open a pull request for that one unit only,
   and leave the rest as filed sub-issues. **Never silently grow the pull request.** The target is under about 1,500
   changed lines excluding generated code and tests, and a pull request that crosses it without a recorded reason is
   refused at review, not negotiated.
9. **Never tick a row you did not run.** A manual screen-reader pass, a hardware scan, a measured restore, a
   signature: if the session cannot do it, the row stays blank and the issue stays open.
   [Section 8](#8-what-cannot-be-verified-in-a-cloud-session) lists every such row and the honest substitute.
10. **Do not edit another child's traceability status cell.** [`../nfr/traceability.md`](../nfr/traceability.md) and
    [`../prd/traceability.md`](../prd/traceability.md) are contended; each child's body names the cells it owns.

---


---

## 3. The state of the tracker today

One row per feature issue #17–#61, with the verdict reached by checking the working copy rather than the issue
text. `parent-closeable` means the deliverables are on `main` and the issue needs verifying and closing, not
working. Where a row names a section of 4, that is where this breakdown adds its children.

| Issue | Feature | State | Verdict | Action now |
| --- | --- | --- | --- | --- |
| #17 | E01-F01 workflows, glossary, taxonomy | open | documentation-only | Delivered under `docs/prd/`. Outstanding item is the owner's approval record |
| #18 | E01-F02 architecture, ownership, ADR baseline | open | documentation-only | Delivered: ADR-0001..0015, ARCH-001..023 all enforced |
| #19 | E01-F03 NFRs, SLOs, data policy, Definition of Done | open | documentation-only | Delivered under `docs/nfr/` and `docs/process/` |
| #20 | E02-F01 solution scaffold and local environment | closed | parent-closeable | Nothing to plan |
| #21 | E02-F02 persistence, migrations, outbox, flags | open | parent-closeable | Verify and close; 73 migrations, outbox and flag surfaces shipped |
| #22 | E02-F03 CI gates, governance, Claude Code workflow | open | parent-closeable | Verify and close, minus the exception-process clause (section 7.1) |
| #23 | E03-F01 authentication, sessions, MFA, recovery | closed | parent-closeable | Nothing to plan |
| #24 | E03-F02 RBAC, branch scopes, authorisation tests | open | parent-closeable | Verify and close; matrix and `RoleMatrixTests` shipped |
| #25 | E03-F03 audited administration | open | parent-closeable | Verify and close; eight admin routes shipped |
| #26 | E04-F01 customer profiles, consent, search, timeline | open | fully-decomposed-ok | Server complete; one open child #182, **itself a split candidate** (section 7.3) |
| #27 | E04-F02 measurement-template administration | open | parent-closeable | All descendants closed; close the parent |
| #28 | E04-F03 measurement capture and versioning | closed | parent-closeable | Nothing to plan |
| #29 | E05-F01 stitching-category and service catalogue | open | parent-closeable | Child #85 closed with the backend; close the parent |
| #30 | E05-F02 shape and design catalogue | open | fully-decomposed-ok | Four children closed; #141 and #142 open |
| #31 | E05-F03 customer material and reference images | open | fully-decomposed-ok | #187, #188, #189, #192 open; Media is a stub |
| #32 | E06-F01 estimates, orders, job cards | open | fully-decomposed-ok | #199, #201, #204 open. **#204 is a split candidate** (section 7.3) |
| #33 | E06-F02 production workflow, assignment, workboard | open | decomposed here | 16 children — section 4.1 |
| #34 | E06-F03 QC, rework, hold, cancellation | open | decomposed here | 13 children — section 4.2 |
| #35 | E07-F01 barcode identifiers, labels, reprints | open | decomposed here | 6 children — section 4.3 |
| #36 | E07-F02 camera, scanner and manual scanning | open | decomposed here | 8 children — section 4.4 |
| #37 | E07-F03 custody transfers, phase scans, reconciliation | open | decomposed here | 11 children — section 4.5 |
| #38 | E08-F01 inventory items, units, suppliers, locations | open | decomposed here | 6 children — section 4.6 |
| #39 | E08-F02 immutable stock ledger | open | decomposed here | 12 children — section 4.7 |
| #40 | E08-F03 low-stock alerts, stocktake, valuation | open | decomposed here | 16 children — section 4.8 |
| #41 | E09-F01 pricing, discounts, GST engine | open | decomposed here | 8 children — section 4.9 |
| #42 | E09-F02 invoices, numbering, PDFs, immutability | open | decomposed here | 7 children — section 4.10 |
| #43 | E09-F03 advances, payments, receipts, dispatch gate | closed | parent-closeable | Closed 2026-09-13 with all six children. Its remaining client work is #216 (section 4.24) |
| #44 | E10-F01 sales, GST, payment, receivables reporting | open | fully-decomposed-ok | #183..#186 open; Reporting is a stub |
| #45 | E10-F02 pipeline, workload, turnaround analytics | open | decomposed here | 16 children — section 4.11 |
| #46 | E10-F03 inventory and profitability analytics, exports | open | decomposed here | 13 children — section 4.12 |
| #47 | E11-F01 notification templates, consent, delivery | open | fully-decomposed-ok | #208..#213 open; Notifications is a stub |
| #48 | E11-F02 delivery queue, customer status, dispatch | open | decomposed here | 12 children — section 4.13 |
| #49 | E11-F03 feedback, alterations, service recovery | open | decomposed here | 9 children — section 4.14 |
| #50 | E12-F01 design system and role layouts | open | decomposed here | 7 children — section 4.15 |
| #51 | E12-F02 installable PWA, safe updates, resilience | open | decomposed here | 9 children — section 4.16 |
| #52 | E12-F03 accessibility, cross-browser, performance | open | decomposed here | 9 children — section 4.17 |
| #53 | E13-F01 versioned API, BFF, OpenAPI, idempotency | closed | parent-closeable | Nothing to plan |
| #54 | E13-F02 integration events and signed webhooks | open | fully-decomposed-ok | #202, #206, #207 open |
| #55 | E13-F03 payment, messaging, accounting, print adapters | open | fully-decomposed-ok | #200, #203, #205 open. **#205 needs re-scoping** (section 7.1) |
| #56 | E14-F01 threat models and ASVS baseline | open | decomposed here | 18 children — section 4.18 |
| #57 | E14-F02 privacy, audit, encryption, secrets | open | decomposed here | 12 children — section 4.19 |
| #58 | E14-F03 observability, SLO alerting, resilience | open | decomposed here | 14 children — section 4.20 |
| #59 | E15-F01 environments, CI/CD, safe database releases | open | decomposed here | 11 children — section 4.21 |
| #60 | E15-F02 backups, PITR, disaster recovery, runbooks | open | decomposed here | 7 children — section 4.22 |
| #61 | E15-F03 QA, UAT, training, pilot, go-live | open | decomposed here | 13 children — section 4.23 |

Counted: 46 feature issues and follow-ups, of which 5 are closed, 6 need only verifying and closing,
3 are documentation already delivered, 8 already have the children they need, and **23 are decomposed
here into 256 units**. No feature issue is left undecomposed.

---

## 4. The breakdown

One subsection per parent. A unit's **Lv** is its dependency depth (section 5.1); **Lane** is A
backend and modules, B client and progressive web application, C governance, security and operations. **Lines** is the
estimated production lines excluding tests and generated code. **Issue** is filled in when the unit is filed.
### 4.1 #33 — E06-F02 production workflow, assignment, workboard

16 units, about 15,400 production lines, in dependency order.

| Unit | Lv | Lane | What it delivers | Depends on | Lines | Issue |
| --- | --- | --- | --- | --- | --- | --- |
| `e06-f02-1` | 0 | A | **Model workflow definitions, versions and the validated phase graph** — Create workflow_definitions, workflow_versions and workflow_version_phases in the orders schema with one expand-only migration 33_workflow_definitions, the draft/published/retired version aggregate, the six-check phase-graph validator with CatalogFinding-shaped findings (seven… | — | 1300 | — |
| `e06-f02-2` | 1 | A | **Draft and edit a workflow version: the definition and version routes** — Five routes under catalog.workflows.edit at organisation scope — list and create a definition, read a version, PUT a draft's whole six-phase graph with If-Match, and create or clone a draft with an Idempotency-Key — with the handler, the permission source, the five matrix rows… | `e06-f02-1`, #199 | 1000 | — |
| `e06-f02-4` | 1 | A | **Instantiate job phases at start of production** — JobPhase, JobPhaseStatus (the four statuses the state diagram draws, plus Skipped which it does not — said so explicitly) and the job_phases table (migration 33_job_phases), the StartProduction handler calling the already-written Order.StartProduction and publishing the existi… | `e06-f02-1`, #201, #199 | 1050 | — |
| `e06-f02-3` | 2 | A | **Validate, publish, retire and seed the default workflow** — The validation report, publish and retire routes under catalog.workflows.publish with RequireStepUp and a reason, the atomic published-version swap, Orders' ICatalogDependencyValidator.ValidatePublicationAsync with that interface's stale "Orders implements only ValidateRetirem… | `e06-f02-2`, `e06-f02-1`, #199 | 1000 | — |
| `e06-f02-5` | 2 | A | **Authorised phase transitions: start, pause, resume and complete** — The phase state machine of docs/prd/state-transitions.md lines 186-212 with the out-of-order check against the pinned graph, one transition route under orders.phase_transition with the scan-burst policy, a mandatory Idempotency-Key and If-Match, the new orders.job-phase-change… | `e06-f02-4`, `e06-f02-1`, #201 | 1100 | — |
| `e06-f02-6` | 2 | A | **Assignee capabilities: the capability ledger and its administration** — assignee_capabilities and the append-only assignments table with its UPDATE/DELETE trigger (migration 33_assignments_and_capabilities), the per-user Covers predicate with valid_from/to and nullable capacity, three capability routes under orders.assign with mandatory reasons, a… | `e06-f02-4`, `e06-f02-1`, #199 | 1000 | — |
| `e06-f02-12` | 3 | B | **Client: the workflow register, the client workflow module and the read-only version view** — /admin/workflows with the definitions, their published version and their draft; the clients/pwa/src/workflows/ module over all eight routes; the pure graph helpers unit-tested without a DOM; the two appended adminPermissions keys and one appended destination; and a READ-ONLY v… | `e06-f02-2`, `e06-f02-3` | 950 | — |
| `e06-f02-7` | 3 | A | **Assign, reassign and unassign a phase with eligibility validation** — The five named eligibility rules over IUserDirectory and the capability predicate, assign/reassign as a PUT and unassign as a POST .../assignment/removal (never a DELETE with a required body), a new append-only row every time, and the three job-assigned/reassigned/unassigned e… | `e06-f02-6`, `e06-f02-4`, `e06-f02-1`, #201 | 900 | — |
| `e06-f02-12b` | 4 | B | **Client: the workflow draft editor, the transition matrix, and publish and retire** — The Draft editor at the address e06-f02-12 established — phase list with duration and SLA empty unless typed, a transition matrix where every cell is a keyboard-operable labelled control naming both phases, a graph preview labelling unreachable phases, dead ends, a missing or … | `e06-f02-12`, `e06-f02-3`, `e06-f02-2` | 1250 | — |
| `e06-f02-8` | 4 | A | **The work-queue, planning-board and workload reads, with their response views and due indicators** — GET /work-queue and /planning-board under orders.read and GET /workload under orders.assign, with orders.work_queue EXTENDED and orders.planning_board and orders.workload declared new — each mirrored into the three generated blocks of docs/security/field-visibility.md and Resp… | `e06-f02-5`, `e06-f02-7`, `e06-f02-4`, `e06-f02-1`, #201 | 1150 | — |
| `e06-f02-10` | 5 | B | **Client: the ScannerSource abstraction and the Tailor work queue on a phone** — The ScannerSource interface of plan Section 4.4 with ManualEntrySource only (camera and wedge are #36, deliberately not stubbed), the client production module, and the /production route roleNavigation.ts:74 already points at but the router never defines — A11Y-RJ-03 steps 1, 3… | `e06-f02-8`, `e06-f02-5`, #201 | 1000 | — |
| `e06-f02-9` | 5 | A | **The due-soon, overdue and phase-SLA evaluator in the worker** — A leased 15-minute [WorkerJob("orders.due_and_sla_evaluation", WorkerBranchScope.Organisation, OrdersPermissions.Read)] pass modelled on DispatchExceptionExpiryService — Organisation and not None, because it reads every branch's garment_jobs — raising job-due-soon, job-overdue… | `e06-f02-8`, `e06-f02-5`, `e06-f02-1` | 900 | — |
| `e06-f02-11` | 6 | B | **Client: the Tailor Master workboard, with assignment and start production** — /workboard as one MasterDetail route with assignment from the row's own control and never a drag (A11Y-RJ-04 step 3), start production stating the pinned version and that revision is refused (step 5), and each of the five server eligibility refusals rendered as its own sentenc… | `e06-f02-10`, `e06-f02-8`, `e06-f02-7`, `e06-f02-4` | 1000 | — |
| `e06-f02-11b` | 7 | B | **Client: the desktop planning board and the per-assignee workload** — /workboard/planning grouped by phase then assignee with unassigned first, each assignee's open count beside their stated capacity or "no stated capacity" and never 0 — gated on orders.assign and not orders.read, because this is the screen DC-11 and section 5.15 are about, with… | `e06-f02-11`, `e06-f02-8`, `e06-f02-10`, `e06-f02-7` | 950 | — |
| `e06-f02-13` | 7 | B | **Client: the Team screen — capabilities, workload and who may take what** — /workboard/team gated on orders.assign rather than orders.read because the whole screen is section 5.15 staff data: live capabilities per person, stated capacity or "no stated capacity", the open-phase count from e06-f02-8's workload read, grant and revoke with reasons, revoke… | `e06-f02-6`, `e06-f02-8`, `e06-f02-7`, `e06-f02-11`, `e06-f02-10` | 850 | — |
| `e06-f02-14` | 8 | A | **Evidence only, not a coding session: the A11Y-RJ-03/04 device run sheets and the peak-volume performance measurement** — The three #33 verification lines no container can satisfy, carved out so no coding child is blocked on hardware: A11Y-RJ-03 steps 1-8 on a physical phone and A11Y-RJ-04 steps 1-5 on a physical tablet with a screen reader (with 4, 5, 6-9 and offline marked not applicable naming… | `e06-f02-8`, `e06-f02-10`, `e06-f02-11`, `e06-f02-11b`, `e06-f02-12b`, `e06-f02-13`, `e06-f02-5`, `e06-f02-7` | — | — |

### 4.2 #34 — E06-F03 QC, rework, hold, cancellation

13 units, about 14,100 production lines, in dependency order.

| Unit | Lv | Lane | What it delivers | Depends on | Lines | Issue |
| --- | --- | --- | --- | --- | --- | --- |
| `e06-f03-1a` | 0 | A | **Catalog: the QC checklist schema and the template and version lifecycle** — All four catalog QC tables (qc_checklist_templates, qc_checklist_versions, qc_criteria, defect_codes) in ONE expand-only migration with both published-version immutability triggers, the draft/published/retired template and version aggregate mirroring Customers' MeasurementTemp… | — | 1250 | — |
| `e06-f03-3` | 0 | A | **Orders: holds, resume, reschedule and the EX-08 cancellation guard** — The holds and cancellations history tables with one migration and the cancellations_append_only trigger, handlers over the PUBLIC Order.Hold/Resume/Reschedule/CancelJob/Cancel commands (GarmentJob's are internal and unreachable from Application), five reason-mandatory routes, … | #201 | 1050 | — |
| `e06-f03-1b` | 1 | A | **Catalog: typed QC criteria and the version's defect-code vocabulary** — The three criterion kinds (PassFail, MeasurementTolerance, FitCheck) with their measurement field key and administrator-entered tolerance, the version-owned defect-code vocabulary, the add/change/remove commands refused on a published version, and one whole-set PUT plus one ve… | `e06-f03-1a` | 1000 | — |
| `e06-f03-4a` | 1 | A | **Orders: the alteration request and the IAlterationRequests hand-off** — The alterations table with its decision columns and append-only decision trigger created in one migration, the request command, the first implementation of the published Orders.Contracts.IAlterationRequests with its 'is there a job at open' question settled as null under SQ-03… | `e06-f03-3`, #201 | 1050 | — |
| `e06-f03-1c` | 2 | A | **Catalog: publish and retire a checklist version, the publication finding, IQcChecklistQuery and the seeded drafts** — Publish and retire behind catalog.checklists.publish with .RequireStepUp() (ARCH-018, matrix line 159), the catalog.qc-checklist-not-published finding added to the already-registered BuiltInCatalogValidator, the Catalog.Contracts.Catalogue.IQcChecklistQuery read contract Order… | `e06-f03-1a`, `e06-f03-1b` | 1300 | — |
| `e06-f03-4b` | 2 | A | **Orders: the alteration decision, the garment reopen and the completion** — GarmentJob.Reopen as one more internal transition plus the public Order.ReopenJob, accepting only Delivered or Closed under SQ-03's default and keeping delivered_at because ck_garment_jobs_delivered_is_consistent was already weakened for exactly this case, the decision with it… | `e06-f03-4a`, #201, #33 | 1100 | — |
| `e06-f03-2a` | 3 | A | **Orders: the immutable QC result and its pinned criteria snapshot** — qc_results and qc_result_criteria with their two append-only triggers (including the parent-still-exists arm the order cascade needs), the QcResult aggregate with no mutator at all carrying the pinned checklist version plus a JSON criteria snapshot, the record-QC command resol… | `e06-f03-1c`, #201 | 1300 | — |
| `e06-f03-2b` | 4 | A | **Orders: the rework task, its single open row and the return phase** — rework_tasks with a partial unique index on (garment_job_id) WHERE status = 'Open' — the merged CashierSessions shape — so INV-JOB-06's 'no rework task is open' is a database fact rather than a racing query, the open and complete commands with their mandatory reasons at Garmen… | `e06-f03-2a`, #201, #33 | 950 | — |
| `e06-f03-5` | 5 | A | **Orders: the ready-for-delivery gate evaluator, the custody contract and the recomputation port** — The application half of the already-merged 454-line ReadyGate domain: a facts reader filling all eleven ReadyGateInputs from job_phases, qc_results, rework_tasks, holds and ICustodyStateQuery, a service evaluating the whole deliver_together parcel through ReadyGate.EvaluateSet… | `e06-f03-2a`, `e06-f03-2b`, `e06-f03-3`, #201, #33 | 1000 | — |
| `e06-f03-6` | 5 | A | **Orders: the five exception-dashboard reads and the Orders timeline source** — Five query-time reads over the source tables — failed QC by latest-result-is-a-fail, repeated rework at two or more, every open hold oldest first with its ageInDays, pending alterations separated by source, cancelled jobs — branch-scoped under orders.read with the CustomerSear… | `e06-f03-2a`, `e06-f03-2b`, `e06-f03-3`, `e06-f03-4a`, `e06-f03-4b`, #201 | 1050 | — |
| `e06-f03-7` | 5 | B | **Client: the orders feature foundation and the QC capture screen** — The new clients/pwa/src/orders feature folder in the billing four-file shape and one route /orders/:orderId/garment-jobs/:jobId/qc that renders the pinned checklist version as named criterion groups with pass/fail, MeasurementField tolerance entry in the tailor's units and def… | `e06-f03-2a`, `e06-f03-2b`, `e06-f03-1c` | 1250 | — |
| `e06-f03-8a` | 6 | B | **Client: the exception dashboard and its five read-only views** — One route /orders/exceptions with five tabbed DataTable views over the E06-F03-6 reads, each independently loaded, filtered and paged with an explicit show-more rather than infinite scroll, each code rendered as its name and every count taken from the server — no action, no di… | `e06-f03-6`, `e06-f03-7` | 900 | — |
| `e06-f03-8b` | 7 | B | **Client: the exception row actions — resume, reschedule, cancel and the alteration decision** — The five row actions a Branch Manager takes from the dashboard — resume a hold, reschedule, cancel a garment, cancel an order, accept or reject an alteration — each its own dialogue component, each permission-gated as decoration with the server's refusal asserted beside the hi… | `e06-f03-8a`, `e06-f03-3`, `e06-f03-4b` | 900 | — |

### 4.3 #35 — E07-F01 barcode identifiers, labels, reprints

6 units, about 3,610 production lines, in dependency order.

| Unit | Lv | Lane | What it delivers | Depends on | Lines | Issue |
| --- | --- | --- | --- | --- | --- | --- |
| `e07-f01-5` | 0 | A | **Make the print queue durable: platform.print_jobs and the station drain port** — Create platform.print_jobs with one expand-only migration, replace LoggingPrintQueue with a durable DatabasePrintQueue that publishes platform.print-job-queued.v1 in the same save, add the IPrintStationQueue drain port with its one-way status machine and audit entries, and wri… | — | 650 | — |
| `e07-f01-8` | 0 | C | **Label content policy and a repeatable label test-sheet command** — docs/custody/labels.md (the label content policy #190 forward-references), a label-sheet CLI command that deterministically renders every template plus the five deliberately invalid payload cases from synthetic payloads, the committed sheets, and the empty record, index and du… | #191, #190 | 450 | — |
| `e07-f01-5b` | 1 | A | **Serve the branch print station: the queue API, its permission and the print frame** — Publish the four host routes a station drains through (list, document stream, printed, failed) under the new print.station permission with PlatformResourceKinds/PrintJobScopeResolver for ARCH-023, the matrix and matrix.yaml rows, and the single frame-src 'self' widening that m… | `e07-f01-5` | 750 | — |
| `e07-f01-6` | 2 | B | **Client: the print station screen that drains its branch's queue** — The /print-station screen: lists its branch's queued jobs through useAdminResource, prints in a same-origin frame with the new-tab and Download-PDF rungs behind it, marks each job printed or failed with a reason, and runs the wedge-scanner Verify step — proved against a stubbe… | `e07-f01-5b`, #191, #190 | 900 | — |
| `e07-f01-7` | 3 | B | **Client: the label print screen, the shared print action and the reprint dialog** — The /labels/print screen (select by order or by job number, preview template/size/orientation, queue single or batch within the cap), apiBlob plus the shared Send-to-print-station-then-Download-PDF action retrofitted into ReceiptPanel, and the reason-tier reprint dialog with t… | `e07-f01-6`, #191, #194, #197, #201 | 800 | — |
| `e07-f01-9` | 4 | C | **Printed label evidence: printer, device and durability records** — NOT A CODING SESSION — the hardware rehearsal a person runs at the head branch: one record per printer and template with the photograph and Android, iPhone/iPad and wedge-scanner scan results, the label queued from a phone and printed by a station, the invalid-payload refusals… | `e07-f01-8`, `e07-f01-5b`, `e07-f01-6`, `e07-f01-7`, #194 | 60 | — |

### 4.4 #36 — E07-F02 camera, scanner and manual scanning

8 units, about 5,150 production lines, in dependency order.

| Unit | Lv | Lane | What it delivers | Depends on | Lines | Issue |
| --- | --- | --- | --- | --- | --- | --- |
| `e07-f02-1` | 0 | B | **The scan core: normalised scan results, the payload parser and the keyboard-wedge source** — clients/pwa/src/scanning/ founded: ScanResult and the ScannerSource abstraction, the D9 parser with the Damm check character, the I-L-O folds and the GS1 digit-string case the server also accepts, the same-payload debounce, KeyboardWedgeSource inert while the user types, and t… | #190, #33 | 700 | — |
| `e07-f02-2` | 1 | B | **The camera engine: capability detection, the permission ladder and the cross-browser decoder pair** — Capability detection without prompting, the five-state permission machine keyed off the four getUserMedia rejection names with one permission.denied per denial, the BarcodeDetector-then-@zxing/browser adapter pair in its own lazy vendor-zxing chunk (plan D2 forbids depending s… | `e07-f02-1` | 700 | — |
| `e07-f02-2b` | 2 | B | **The Scanner check screen under Settings, and the scanning message family** — The settings/device diagnostic that decodes any barcode and shows raw, normalised, namespace, check-character validity, source and decode latency while resolving and committing nothing — deliberately unguarded beside settings/display, because the person setting up a device has… | `e07-f02-2` | 650 | — |
| `e07-f02-3` | 3 | B | **The /scan screen in single mode: resolve, visual-first feedback and the rejection remedies** — The route four roles' navigation already points at: one-handed viewfinder in the upper 60% with a thumb-reach bottom bar, #194's resolve pinned with Conforms<> and not retried because of scan-burst, the green/red flash published into the shell's persistent regions, and the fiv… | `e07-f02-2b`, #194 | 900 | — |
| `e07-f02-3b` | 4 | B | **The commit path: the sensitive-transition set, the confirmation card, the undo window and the command port** — CONFIRM_BEFORE_COMMIT holding exactly FBH-02's five proposed actions with the continuous set derived as its complement once, the confirmation card through ConfirmDialog at the confirm tier with the refusal rendered inside the modal, the UNDO_WINDOW_MS delay before anything is … | `e07-f02-3` | 550 | — |
| `e07-f02-4` | 5 | B | **Continuous mode: the running scan list and the sequential Commit N pipeline** — The viewfinder stays open: a reducer-backed list with per-item status, duplicates shown rather than dropped, removal before commit, and a Commit N driver submitting one item at a time with its own client event UUID and Idempotency-Key, continuing past a mid-batch failure becau… | `e07-f02-3b`, #194 | 700 | — |
| `e07-f02-5` | 5 | B | **Authorised manual entry and the camera-denied recovery that keeps the flow alive** — ManualEntrySource with the job-number and raw-payload rungs over #194's two endpoints, the mandatory custody.manual_lookup reason read out of the published operation rather than invented, the one narrow typed X-Scan-Source option apiClient.ts does not have today, the rung gate… | `e07-f02-3b`, #194, #201 | 900 | — |
| `e07-f02-6` | 6 | C | **Evidence, not a coding session: real-device, hardware-scanner and screen-reader rehearsal of the scan flows** — Not a coding session and it says so in its title: the docs/custody/scan-tests/ record convention mirroring E07-F01-8's label-tests, the observed readings per Tier 1 class in a tab and installed, three printed rejections plus a wrong-branch job and wrong custodian deferred to #… | `e07-f02-1`, `e07-f02-2`, `e07-f02-2b`, `e07-f02-3`, `e07-f02-3b`, `e07-f02-4`, `e07-f02-5`, `e07-f01-8`, #194, #201 | 50 | — |

### 4.5 #37 — E07-F03 custody transfers, phase scans, reconciliation

11 units, about 9,250 production lines, in dependency order.

| Unit | Lv | Lane | What it delivers | Depends on | Lines | Issue |
| --- | --- | --- | --- | --- | --- | --- |
| `e07-f03-1` | 0 | A | **The scan-event spine: immutable scan_events, the materialised custody row and POST /custody/scans** — custody.scan_events with its append-only trigger plus custody.job_custody_state as the SELECT FOR UPDATE serialisation point, a ScanAction vocabulary every member of which is sourced to state-transitions 4.1 or 3.2 (no invented QcIn, QcOut or ReadyForDelivery), RecordScanHandl… | #190, #201 | 1250 | — |
| `e07-f03-2` | 1 | A | **Two-sided custody transfers: transfer out, receive, reject and the pending queue read** — custody_transfers plus custody_transfer_jobs with a customer_merges_no_rewrite-style narrow-update trigger, a pure CustodyTransfer state machine, the transfer-out, receive and reject-with-reason routes each appending a scan event through E07-F03-1, the GET queue read under cus… | `e07-f03-1`, #190, #201 | 1250 | — |
| `e07-f03-3` | 2 | A | **Reconciliation cases: the four opening arms, the manual open, the withdrawal and the case reads** — reconciliation_cases and reconciliation_evidence (media references only, never a URL), the six EX-13 types, the scan pipeline's mismatch, stale, duplicate and unknown-location arms each opening exactly one case behind a partial unique index, the rejected-or-expired transfer ar… | `e07-f03-1`, `e07-f03-2`, #190 | 1150 | — |
| `e07-f03-3b` | 3 | A | **The compensating CORRECTION event and the second approver under step-up** — RecordCorrection and Approve on E07-F03-3's domain type, CustodyReconciliationOptions with AlwaysRequireApproval defaulting to true as the strictest reading of the threshold IOD-05 has not set, the correction and approval routes with the different-user check and step-up copied… | `e07-f03-3`, `e07-f03-1`, #190 | 750 | — |
| `e07-f03-6` | 3 | A | **Scan-driven phase advance: the Orders scan participant and auto start-production** — JobPhaseScanParticipant in Orders.Infrastructure, registered as the first participant so a phase scan moves the phase inside Custody's T1 (FBH-01's proposed arm, closed here with the asynchronous fallback written down), with PhaseStart, PhasePause, PhaseResume and PhaseComplet… | `e07-f03-1`, #33 E06-F02-4, #33 E06-F02-5, #201 | 450 | — |
| `e07-f03-4` | 4 | A | **The cross-branch transfer grant and the overdue-transfer sweep** — TransferScopeRequirement with its ITransferGrantSource platform port, applied to exactly receive, reject and E07-F03-3b's correction route while a cross-branch transfer is pending (BR-8, INV-CDY-04, ADR-0007 line 249), custody moving branch on acceptance, an empty-by-default C… | `e07-f03-2`, `e07-f03-3`, `e07-f03-3b`, #190 | 800 | — |
| `e07-f03-5` | 6 | A | **The real ICustodyStateQuery, the custody timeline, the operational search and the gate switch** — CustodyStateQuery replacing #34's Unknown placeholder so the fail-closed CustodyReconciled predicate can be enabled in host configuration, the chronological GET timeline whose payload pairs a correction with the event it corrects through TimelineEntry.detail, the operational s… | `e07-f03-1`, `e07-f03-2`, `e07-f03-3`, `e07-f03-3b`, #34 E06-F03-3, #34 E06-F03-5 | 700 | — |
| `e07-f03-7` | 7 | B | **Client: the job custody screen — timeline, transfer out, receive and reject** — clients/pwa/src/custody/ (api module, types, custodyPermissions.ts) and the route custody/jobs/:garmentJobId built on the merged Timeline, Dialog and ConfirmDialog primitives with no new StatusKind and no borrowed glyph, the in-memory same-key retry, StateRegion announcements … | `e07-f03-2`, `e07-f03-5` | 1000 | — |
| `e07-f03-8` | 8 | B | **Client: the pending and overdue transfer queue, and the one navigation destination** — custody/transfers with incoming, outgoing, pending and overdue filters and row-level Receive and Reject reusing E07-F03-7's dialogues, plus the one navigation destination done properly — DESTINATION_IDS, DESTINATIONS with icon 'list' rather than inventory's 'package', SECTION_… | `e07-f03-7`, `e07-f03-4` | 650 | — |
| `e07-f03-8b` | 9 | B | **Client: the exception queue and the case detail with the correction and the approval** — custody/exceptions filtered by the six EX-13 types and four statuses, and custody/cases/:caseId with the correction form, the Approve action absent without its permission and disabled for the recorder, and the step-up challenge answered through setSessionChallengeHandler rathe… | `e07-f03-8`, `e07-f03-3`, `e07-f03-3b`, `e07-f03-5` | 900 | — |
| `e07-f03-9` | 10 | C | **Evidence only, needs hardware: the physical custody rehearsal, the seeded scenario and the replay suite** — The evidence child, and the only one that cannot be completed on a laptop: a repeatable custody scenario in seed-synthetic (four jobs, a resolved correction, one pending cross-branch transfer, and the two state-shaped rejection fixtures a printed sheet cannot carry), ScanRepla… | `e07-f03-1`, `e07-f03-2`, `e07-f03-3`, `e07-f03-3b`, `e07-f03-4`, `e07-f03-5`, `e07-f03-6`, `e07-f03-7`, `e07-f03-8`, `e07-f03-8b`, #190, #191, #194, #197, #36 | 350 | — |

### 4.6 #38 — E08-F01 inventory items, units, suppliers, locations

6 units, about 6,130 production lines, in dependency order.

| Unit | Lv | Lane | What it delivers | Depends on | Lines | Issue |
| --- | --- | --- | --- | --- | --- | --- |
| `E08-F01-5` | 0 | B | **Client: the Inventory module foundation and the location master screens** — Creates the client Inventory module — types pinned against the generated schema, one API module carrying the location calls plus the listSuppliers and listLocations calls every later picker needs, the four permission constants, one message family, one stylesheet — and the loca… | #193 | 1250 | — |
| `E08-F01-5b` | 1 | B | **Client: the supplier master screens** — The supplier list and editor under /admin/inventory/suppliers over #193's four statuses (draft, active, suspended, retired), and the one screen in all of #38 that renders Personal data — supplier contacts — so it carries the data-classification 5.9 handling rules and the test … | #193, `E08-F01-5` | 850 | — |
| `E08-F01-6` | 1 | B | **Client: the item register — list, and the editor for identity, base unit, suppliers, tax, availability and status** — A filtered, cursor-paged item list and a sectioned item editor covering identity, the base unit, supplier references with a preferred one, the HSN and tax reference as copied strings, location availability, tracking method and the draft-to-active publish plus retire and reacti… | #195, `E08-F01-5` | 1350 | — |
| `E08-F01-6b` | 2 | B | **Client: item units, conversions and the client mirror of invertibility and cycle detection** — Replaces the item editor's read-only units block with an editable purchase- and issue-unit panel over a new pure module unitConversion.ts that answers round-trip invertibility, acyclicity, the conversion in words for the accessible name, and which field a failure belongs to — … | #195, `E08-F01-6` | 780 | — |
| `E08-F01-7` | 2 | B | **Client: bulk CSV item import — preview, error report and commit** — One route with four phases — choose a file, preview, the per-row outcome report with duplicate and failure counts, commit exactly the previewed batch once — enforcing the 10 MB cap cited from plan Section 5.2 client-side, parsing nothing in the browser, adding no export path, … | #195, `E08-F01-6` | 850 | — |
| `E08-F01-8` | 2 | B | **Client: the reorder-rule list and editor, per item and location** — A rule list filterable by item and location and a rule editor over the five fields configurable-vs-fixed.md line 70 fixes, with the minimum ≤ reorder point ≤ target invariant mirrored onto the field that must change, the location picker offering the caller's branch only, no de… | #196, `E08-F01-5`, `E08-F01-6` | 1050 | — |

### 4.7 #39 — E08-F02 immutable stock ledger

12 units, about 11,000 production lines, in dependency order.

| Unit | Lv | Lane | What it delivers | Depends on | Lines | Issue |
| --- | --- | --- | --- | --- | --- | --- |
| `e08-f02-1` | 0 | A | **The immutable stock ledger and the balance it keeps in the same transaction** — ledger_entries and balances with the append-only trigger, the two-rule entry-type fold, StockQuantity, ILedgerPoster with its fixed-order FOR UPDATE lock, and BalanceRebuild — no HTTP route at all | #193, #195 | 1250 | — |
| `e08-f02-4` | 0 | A | **Purchase orders: the intent to buy, with no ledger effect** — purchase_orders and purchase_order_lines with their status machine and five routes, proposing the one new permission key #39 needs — inventory.manage_purchase_orders, catalogue entry included — and posting nothing to the ledger | #193, #195 | 1050 | — |
| `e08-f02-2` | 1 | A | **Reservations that cannot be oversubscribed, and the release that always resolves them** — the reservations table and its three-state machine, three routes, the twenty-against-five concurrency proof, stock-reserved and stock-released, and the orders.job-cancelled.v1 consumer that releases on cancellation | `e08-f02-1`, #193, #195 | 1100 | — |
| `e08-f02-5` | 1 | A | **Purchase receipts posted into the ledger, all or nothing** — purchase_receipts and lines, one PurchaseReceipt entry per line in one transaction with cost and lot discipline, the order-closing rule, purchase-received.v1 with its eight fields, and the no-half-post proof for first, middle and last line | `e08-f02-1`, `e08-f02-4`, #193, #195 | 1000 | — |
| `e08-f02-3` | 2 | A | **Movements: issue, consumption, return, wastage and the compensating correction** — the movements command for five types with the job-held-quantity guard, the correction rule, the inventory.allow_negative_stock flag plus its step-up override route, and stock-consumed.v1 — no migration | `e08-f02-1`, `e08-f02-2` | 950 | — |
| `e08-f02-6` | 2 | A | **Location transfers: the balanced pair, and why in-transit stays zero** — two routes posting a TransferOut/TransferIn pair sharing a transfer_group_id, the allowed-destination refusal, the opposite-direction deadlock test — no table, no migration and, deliberately, no approval control, because no document defines one | `e08-f02-1`, `e08-f02-2`, #193, #195 | 600 | — |
| `e08-f02-7` | 3 | A | **Balance and ledger reads, IStockBalanceQuery, and the scheduled drift detector** — three reads (item balances, item ledger, branch ledger) with the costIncluded gate on inventory.view_valuation and an explicit cost-read audit, the published IStockBalanceQuery contract, and the leased reconciliation job that reports drift and changes nothing | `e08-f02-1`, `e08-f02-3`, #193, #195 | 1150 | — |
| `e08-f02-8` | 4 | B | **Client: stock on hand and the movement history** — two read screens — the item stock card per location and the branch ledger browser with filters and cursor paging — with the cost column driven by costIncluded, full Tamil messages, state stories and A11Y-RJ-05 step 8 | `e08-f02-7`, `E08-F01-5` | 900 | — |
| `e08-f02-10` | 5 | B | **Client: the purchase-order list and the purchase-order editor** — the purchase-order list with its filters and cursor paging, and the multi-line order editor with per-line base-unit conversion in words, cost entry, save/place/cancel each with one key, and a placed order rendered read-only | `e08-f02-4`, `e08-f02-8` | 850 | — |
| `e08-f02-9` | 5 | B | **Client: record a stock movement, with the negative-stock approval path** — one write screen for issue, consumption, return and wastage — the conversion in words, one idempotency key per action, the approved-negative re-post with its step-up replayed in place, OfflineBlockedAction, and the empty state for a caller who holds the write but none of the r… | `e08-f02-3`, `e08-f02-7`, `e08-f02-8` | 850 | — |
| `e08-f02-10b` | 6 | B | **Client: the purchase-receipt entry form and the posted receipt** — the receipt entry form — multi-line, per-line conversion in words, the conditional lot field, optional pre-fill from a placed order, one posting with one key and no per-line save — plus the posted-receipt read with no cost column, and A11Y-RJ-05 step 1 | `e08-f02-5`, `e08-f02-10` | 800 | — |
| `e08-f02-9b` | 6 | B | **Client: record a location transfer** — one write screen for a transfer between two locations of the branch, offering only the source's allowed destinations, with the conversion in words, one idempotency key, OfflineBlockedAction, and every convention inherited from e08-f02-9 rather than re-decided | `e08-f02-6`, `e08-f02-9` | 500 | — |

### 4.8 #40 — E08-F03 low-stock alerts, stocktake, valuation

16 units, about 13,050 production lines, in dependency order.

| Unit | Lv | Lane | What it delivers | Depends on | Lines | Issue |
| --- | --- | --- | --- | --- | --- | --- |
| `e08-f03-1` | 0 | A | **Configure the branch alert policy and build the low-stock evaluator** — The per-branch alert_policies row exactly as configurable-vs-fixed.md line 69 defines it, one expand-only migration, the GET/PUT policy routes on inventory.manage_reorder_rules with ETag, If-Match and idempotency and no resource scope because neither route takes a parameter, t… | #193, #196 | 900 | — |
| `e08-f03-4` | 0 | A | **Open a stocktake session, freeze its expected quantities and record counts and recounts** — stocktakes and stocktake_counts with one session in Counting per location enforced by a partial unique index, the expected quantity frozen from #39's balances inside the opening transaction, append-only count rows so the first count is kept, and five routes on inventory.stockt… | #193, #195, #39 | 1100 | — |
| `e08-f03-2` | 1 | A | **Raise, de-duplicate and clear low-stock alerts from the worker, on the cadence and on ledger movement** — low_stock_alerts with a partial unique index that makes 'fires once per state change' true under concurrency, low_stock_evaluation_state carrying the ledger watermark so the pass evaluates on ledger movement as well as on each branch policy's cadence (the half #40's blueprint … | `e08-f03-1`, #193, #196, #39 | 1000 | — |
| `e08-f03-4b` | 1 | A | **Close counting, abandon a session, and hold the location while it is being counted** — CloseCounting and Abandon as two audited routes with no migration, the IStocktakeFreezeQuery port and the single call site in #39's movement command refusing inventory.location-frozen-for-stocktake, the decision written down that out-movements are refused and receipts permitte… | `e08-f03-4`, #39 | 550 | — |
| `e08-f03-3` | 2 | A | **Acknowledge, snooze and escalate a low-stock alert, and publish the escalation** — The acknowledge and snooze commands with a resource scope and an audited reason, the leased escalation pass on the policy's delay, inventory.low-stock-escalated.v1 plus the module-ownership.md section 5.7 amendment that makes a third published event legal, the proposed invento… | `e08-f03-2` | 800 | — |
| `e08-f03-5` | 2 | A | **Explain a stocktake variance, approve it by a second person and post it as ledger adjustments** — stocktake_variances on the ReconciliationBatch pattern with CountedBy copied at computation, the threshold as configuration defaulting to zero with a new open decision raised on the OD-24 model, FIVE routes not four — the fifth being the #220 fix, a purpose-built variance read… | `e08-f03-4b`, #39 | 1300 | — |
| `e08-f03-6` | 2 | A | **Serve the operational stock reports with reconciliation totals, freshness and CSV export** — Stock-on-hand and the low-stock/replenishment list served from Inventory's own tables and never the reporting schema, each with a reconciliation footer computed from the ledger at the cut-off and a freshness line, and the solution's first CSV writer with section 9's marking an… | `e08-f03-2`, #39, #196 | 1100 | — |
| `e08-f03-9` | 2 | B | **Client: the stocktake session list and the count sheet with recount** — /inventory/stocktakes and /inventory/stocktakes/:stocktakeId built to the wording the A11Y-RJ-05 prototype already fixes: the frozen-location banner, two separate count fields with the first locked once recorded, the variance as a signed pair in words and units, the Close coun… | `e08-f03-4`, `e08-f03-4b`, #38 | 1050 | — |
| `e08-f03-10` | 3 | B | **Client: review, explain, approve and post a stocktake variance** — /inventory/stocktakes/:stocktakeId/variances for the approver's journey, choosing its read by what the caller holds because an Owner reaches the sheet only through E08-F03-5's approval-scoped route — which carries RequireStepUp, so the step-up demand lands on page load and not… | `e08-f03-5`, `e08-f03-9` | 900 | — |
| `e08-f03-11` | 3 | B | **Client: the stock-on-hand and low-stock report screens, and the CSV download path** — Two read screens — stock on hand, and low stock with replenishment — each with the labelled reconciliation footer that says plainly when the two totals disagree and offers no corrective action, the freshness line, and the one non-JSON transport helper a CSV download needs (the… | `e08-f03-6`, #38 | 850 | — |
| `e08-f03-12` | 3 | B | **Client: wire the stocktake count sheet to #36's continuous scan mode (needs #36; hardware evidence deferred)** — Mount #36's ScannerSource in continuous mode on the count sheet, filling the slot E08-F03-9 reserved without moving a control: a decode selects a line and focuses its count field but never submits a count, an unknown decode is refused in words, the manual list and filter stay … | `e08-f03-9`, #36 | 250 | — |
| `e08-f03-3b` | 3 | A | **Route low-stock alerts to the right staff: the Notifications routing rule and inventory.* templates** — The one routing rule family plan 6.2 note 4 reserves for #40, mapping the raised and escalated events to a staff audience built from the event's own targetRoles (never a cross-schema read), the two inventory.* staff templates with their declared-variable allowlist and no conse… | `e08-f03-3`, #208, #209, #210 | 450 | — |
| `e08-f03-7` | 3 | A | **Value stock at a cut-off: valuation runs, weighted average, FIFO and IValuationQuery** — Append-only valuation_runs and valuation_lines recording the method they used (INV-STK-08), weighted average by default with FIFO optional under OD-05's recorded default, rounding per conventions.md 1.2 with one round half away from zero, customer material excluded by construc… | `e08-f03-6`, #39, #193, #195, #198 | 1250 | — |
| `e08-f03-8` | 3 | B | **Client: the low-stock alert queue and the branch alert policy screen** — /inventory/alerts as a flat operational route on the billing-routes pattern, with acknowledge and snooze row actions holding one Idempotency-Key across a retry and the snooze dialogue at the reason tier (there is no 'irreversible' tier), plus /inventory/alert-policy which pre-… | `e08-f03-1`, `e08-f03-3`, #38 | 900 | — |
| `e08-f03-11b` | 4 | B | **Client: the stock valuation screen behind inventory.view_valuation** — /inventory/valuation — a cut-off control, the Run action at the confirm tier, the run list and one run's lines — with the method and cut-off stated beside every total because INV-STK-08 makes the method part of what a figure is, a story for two runs at one cut-off standing sid… | `e08-f03-7`, `e08-f03-11` | 650 | — |
| `e08-f03-13` | 5 | C | **Evidence, not a coding session: the accountant's sign-off of the valuation method, rounding and golden fixtures** — The one part of #40 no coding session can do: walk docs/inventory/valuation.md with the accountant, get every golden fixture's numbers confirmed or replaced, remove the pending-sign-off markers, and record OD-05's valuation half in the register the way #147's rounding half is … | `e08-f03-7`, `e08-f03-11b` | — | — |

### 4.9 #41 — E09-F01 pricing, discounts, GST engine

8 units, about 9,950 production lines, in dependency order.

| Unit | Lv | Lane | What it delivers | Depends on | Lines | Issue |
| --- | --- | --- | --- | --- | --- | --- |
| `e09-f01-4` | 0 | B | **Client: the pricing administration foundation, the GST registration register and the tax configuration version register** — Creates the client foundation for pricing administration — the pricing message family, the hand-written payload types, both billing configuration permission keys, the transport and the two registers at /admin/gst-registrations and /admin/tax-configuration — so an Owner can rec… | #145, #24, #25, #50 | 1450 | — |
| `e09-f01-5` | 1 | B | **Client: draft and edit a tax configuration version and its tax codes** — The tax configuration version editor at /admin/tax-configuration/:versionId up to the last save — version details, tax codes with their CGST/SGST/IGST/Cess component rates, the register's start-a-draft and clone acts, the tag carried forward from every response including a 204… | `e09-f01-4`, #145 | 1400 | — |
| `e09-f01-6` | 1 | B | **Client: the price-list register, a list's versions, and the conventions form that starts a draft** — The price-list register at /admin/price-lists and a list's versions at /admin/price-lists/:priceListId, including the conventions form that starts a draft — first day, inclusive or exclusive, round-off rule, override threshold and branches, none of them defaulted by the client… | `e09-f01-4`, #146 | 1350 | — |
| `e09-f01-5b` | 2 | B | **Client: validate and publish a tax configuration version, and the shared billing findings list** — The publication checks read before a publish and the publish itself — one shared BillingFindingsList that keeps errors distinct from warnings, the typed findingsOf() reader over the problem's findings extension, publish on the second key with step-up and a reason, and the conc… | `e09-f01-5`, #145 | 1050 | — |
| `e09-f01-7` | 2 | B | **Client: the price-list version editor — conventions, items and the tax-code picker** — The price-list version editor at /admin/price-lists/versions/:versionId up to the last save — the conventions written whole-value through the form E09-F01-6 built, items with base rate, unit and a tax code picked from the published tax configuration, and a picker that says so … | `e09-f01-6`, `e09-f01-5`, #146, #147 | 1400 | — |
| `e09-f01-7b` | 3 | B | **Client: validate and publish a price-list version** — The publication report reusing E09-F01-5b's findings list — where warnings are real, so no-items and branch-left-unpriced must read as warnings — plus publish with step-up and a reason, the missing-tax-configuration error with its way out, and two genuinely different publish-c… | `e09-f01-7`, `e09-f01-5b`, #146, #147 | 950 | — |
| `e09-f01-8` | 4 | B | **Client: author the discount rules of a draft price-list version** — Discount rules on a draft version — code, kind, the maximum a counter may give on its own authority and the hard maximum, each bound labelled with its unit and neither pre-filled — authored here and evaluated nowhere here, so the client never restates the engine's ordering rule. | `e09-f01-7`, `e09-f01-7b`, #146 | 900 | — |
| `e09-f01-8b` | 5 | B | **Client: the pricing preview and the accountant's test-case shapes before publish** — The preview at /admin/pricing/preview: price a line against a draft version, run the golden master's seventeen case shapes before publishing, see the round-off as its own line, and see an unauthorised override or an over-maximum discount refused in words — the parent's accepta… | `e09-f01-8`, `e09-f01-7`, `e09-f01-6`, `e09-f01-5`, #146, #147 | 1450 | — |

### 4.10 #42 — E09-F02 invoices, numbering, PDFs, immutability

7 units, about 4,020 production lines, in dependency order.

| Unit | Lv | Lane | What it delivers | Depends on | Lines | Issue |
| --- | --- | --- | --- | --- | --- | --- |
| `E09-F02-4` | 0 | B | **Client: the invoice register and the invoice detail screen** — Open the invoice read surface against the three published read routes: the branch's register with its status filter and cursor paging, the detail screen with its lines, tax components, totals under the document's own zero-suppression rule, cancellation and notes, and the I- ba… | — | 1050 | — |
| `E09-F02-8` | 0 | A | **Contribute invoices, cancellations and notes to the customer timeline** — Implement the ITimelineSource the #42 blueprint names and no merged child built, reading Billing's own three tables through the platform port, filtering on permissions and assigned branches, paging across three record kinds on one total order, and carrying one expand-only inde… | — | 350 | — |
| `E09-F02-9` | 0 | C | **Prove the rendered documents against the accountant's golden master, in the contract tier** — Add the test-only PDF text-extraction capability and prove, without a database, that every figure printed on the page equals the figure it was given — all 17 golden-master cases at the renderer level, the Tamil code points in the text layer, and the machine-decidable half of t… | — | 40 | — |
| `E09-F02-10` | 1 | C | **Prove the stored PDF against the persisted calculation snapshot, and prepare the accountant's review pack** — The parent's last two verification items: PdfTotalsMatchSnapshot over two named live scenes (intra-state and inter-state) comparing the stored PDF's text layer with IPricingService.FindSnapshotAsync to the paisa, the three-document sample pack, the accountant's review record a… | `E09-F02-9` | 30 | — |
| `E09-F02-5` | 1 | B | **Client: the in-app invoice print view, the PDF download and the print-queue hand-off** — Close the blueprint's named gap with a real-text document view, a print stylesheet, three distinctly named controls and the first binary read path in the shared transport — plus the one additive server change the view cannot exist without: SupplierLegalName and SupplierTradeNa… | `E09-F02-4` | 900 | — |
| `E09-F02-6` | 2 | B | **Client: draft an invoice for an order, discard it, and post it** — Give a cashier the three write acts that exist today — draft from a stored calculation under billing.create_invoice, discard with a reason under billing.update_invoice, post behind an irreversible confirmation under billing.post_invoice — with one idempotency key per act, the … | `E09-F02-4`, `E09-F02-5` | 800 | — |
| `E09-F02-7` | 3 | B | **Client: cancel a posted invoice and issue credit and debit notes** — A posted invoice is immutable by trigger, so a compensating document is the only remedy: the cancel action with step-up and a reason, a per-garment-job credit/debit note screen, and the second named money-arithmetic exception (adjustmentNote.ts) the remaining-value column cann… | `E09-F02-4`, `E09-F02-5`, `E09-F02-6` | 850 | — |

### 4.11 #45 — E10-F02 pipeline, workload, turnaround analytics

16 units, about 11,950 production lines, in dependency order.

| Unit | Lv | Lane | What it delivers | Depends on | Lines | Issue |
| --- | --- | --- | --- | --- | --- | --- |
| `e10-f02-1` | 0 | A | **Project the garment-job pipeline, phase durations and held time** — Two rebuildable projections in the reporting schema — one row per garment job, one per job x phase — fed by the ten existing Orders events plus #33's orders.job-phase-changed.v1, with one expand-only migration and the nine metric-dictionary entries; OrderRevised writes revisio… | #183, #33 | 1150 | — |
| `e10-f02-7a` | 0 | B | **Client: the reporting area, the status strip and the /reports hub the navigation already points at** — The client reporting area in the shape of clients/pwa/src/billing/, the unowned-today status strip, and the /reports hub that roleNavigation.ts line 81 has pointed at since before any route existed — owning the router.tsx and messages/index.ts edits once, and asserting that a … | — | 560 | — |
| `e10-f02-2` | 1 | A | **Publish the pipeline, due/overdue, bottleneck and turnaround reads** — Four authenticated reads over the pipeline projection — summary with due buckets, the cursor-paged drill list, bottleneck queue age and WIP, and promised-versus-actual turnaround — each on reports.read with AssignedBranches, DefaultUser rate limiting, no route parameter, and a… | `e10-f02-1`, #183 | 1200 | — |
| `e10-f02-3a` | 1 | A | **Project tailor assignment and daily throughput from #33's assignment events** — The append-only phase-assignment projection and the daily assignee throughput roll-up, attributing each completion to the assignee in force at that instant, storing assignee_user_id and never a display name — one expand-only migration, no endpoint and no permission. | `e10-f02-1`, #183, #33 | 780 | — |
| `e10-f02-4a` | 1 | A | **Project QC outcomes, rework cycles and the daily reason-code roll-up** — A per-garment quality projection carrying a reached_qc flag so a garment with no QC phase leaves every denominator, plus a daily reason-code roll-up, from #34's seven events — one expand-only migration, no endpoint, no actor column anywhere and a test that asserts it. | `e10-f02-1`, #183, #34 | 820 | — |
| `e10-f02-5` | 1 | A | **Project customer retention, repeat interval and category mix** — The one #45 projection whose grain is the customer — three tables so the category-mix breakdown can never disagree with its own total — with the customers.customer-merged.v1 fold resolved transitively, one aggregate-only read that identifies nobody, and a test that the payload… | `e10-f02-1`, #183 | 850 | — |
| `e10-f02-6a` | 1 | A | **Add the source job-state count to IOrderSnapshotQuery and reconcile the pipeline against it** — The additive CountJobStatesAsync the contract's own remarks at lines 81-83 reserve for #45, its implementation inside Orders, and the comparison that writes reconciliation_runs and per-cell findings — it reports and never corrects (NFR-BG-06), with the Orders diff capped at tw… | `e10-f02-1`, #183 | 650 | — |
| `e10-f02-3b` | 2 | A | **Publish the workload reads behind a new reports.read_workload, and narrow the staff-throughput field to it** — Two audited workload reads with an explicit unassigned row and no ranking control, behind the new reports.read_workload granted to three roles — and the re-gate of the already-merged orders.work_queue.assigneeThroughput to the same key, so the catalogue stops giving two answer… | `e10-f02-3a`, `e10-f02-1`, #183 | 900 | — |
| `e10-f02-4b` | 2 | A | **Publish the quality and failure-reason reads, every rate with its denominator** — Two reads that never emit a bare percentage — numerator, denominator and excluded counts on every row, a distinct no-data state where a denominator is zero, and a contract test asserting neither payload type has any property a person could be named in. | `e10-f02-4a`, `e10-f02-1`, #183 | 620 | — |
| `e10-f02-6b` | 2 | A | **Schedule the reconciliation and freshness pass in the worker, and raise the lag and mismatch alerts** — A leased, bounded worker job modelled on DispatchExceptionExpiryService that runs the comparison and computes lag, raising exactly one de-duplicated alert per report x condition x cut-off — extending whatever #184 landed rather than coining a second evaluator, and never a thir… | `e10-f02-6a`, #183 | 620 | — |
| `e10-f02-7b` | 2 | B | **Client: the pipeline dashboard and its drill list** — The owner and manager pipeline dashboard and its shared-filter drill list, built from the OwnerDashboard prototype's rules — table-first charts, attribute-sized aria-hidden SVG because the CSP forbids inline styles, a screen that holds still, a forbidden state that never redir… | `e10-f02-2`, `e10-f02-7a` | 1250 | — |
| `e10-f02-6c` | 3 | A | **Prove rebuild equals live across every #45 projection at production-sized volume** — One deterministic synthetic-history generator at the cited volume (36,000 garment jobs a year, capacity-and-performance 2.2), one test proving rebuild equals live across all nine #45 projection tables, and the nine read routes measured against the section 3.3 and S9 rows with … | `e10-f02-1`, `e10-f02-2`, `e10-f02-3a`, `e10-f02-3b`, `e10-f02-4a`, `e10-f02-4b`, `e10-f02-5`, #183 | 300 | — |
| `e10-f02-8a` | 3 | B | **Client: the tailor workload view, its drill list and the withheld-columns state** — The one #45 screen where a figure about a named person reaches a browser: the workload view with its unassigned row and its unconditional DC-11 context panel, its drill list, the withheld-columns state for a caller holding reports.read alone, and tests that there is no ranking… | `e10-f02-3b`, `e10-f02-7a`, `e10-f02-7b` | 900 | — |
| `e10-f02-8b` | 3 | B | **Client: the quality dashboard that names nobody** — First-pass, failure, rework and alteration rates each rendered with numerator, denominator and excluded counts, the reason-code Pareto as an always-rendered table under reused attribute-sized bars, no-data as a state rather than 0%, no QC photograph anywhere, and a test that t… | `e10-f02-4b`, `e10-f02-7a`, `e10-f02-7b` | 650 | — |
| `e10-f02-9` | 4 | B | **Client: the phone Today summary, with a permission-denied state per tile** — The phone-first Today tiles composed from authorised reads, each rendering a figure, a named permission-denied state or a retry, with a tile whose read is absent from the generated schema not rendered at all — and the landingRoute shell change, the low-stock tile and the UAT a… | `e10-f02-2`, `e10-f02-7a`, `e10-f02-7b`, `e10-f02-8a`, `e10-f02-8b` | 700 | — |
| `e10-f02-10` | 5 | C | **Evidence: write the operational analytics UAT script, and record its run with the shop's people (the run is not a coding session)** — docs/process/uat-operational-analytics.md in the shape of uat-administration.md, covering the overdue, hold, QC-fail and reassignment journeys with a scripted refusal in each — plus honest prerequisites, because ./scripts/dev reset creates two branches and two flags and no use… | `e10-f02-2`, `e10-f02-3b`, `e10-f02-4b`, `e10-f02-7a`, `e10-f02-7b`, `e10-f02-8a`, `e10-f02-8b`, `e10-f02-9` | — | — |

### 4.12 #46 — E10-F03 inventory and profitability analytics, exports

13 units, about 12,600 production lines, in dependency order.

| Unit | Lv | Lane | What it delivers | Depends on | Lines | Issue |
| --- | --- | --- | --- | --- | --- | --- |
| `e10-f03-1` | 0 | A | **Project stock movements, purchases and daily stock position from the Inventory events** — projections_stock_movements and projections_stock_position_daily from four published inventory events plus orders.garment-job-created.v1 for the dimension, a branch-day IStockBalanceQuery snapshot job declaring inventory.view_reports (not view_valuation), and one quantity reco… | #183, #39, #201, #45 | 1150 | — |
| `e10-f03-4` | 0 | A | **Costing assumption versions: labour rates, overhead and allocation basis as versioned configuration** — the draft/published/retired assumption chain with its own append-only trigger, exactly five routes copied from PriceListEndpoints.cs, the new reports.manage_costing_assumptions key at PermissionScope.Organisation with no resource scope owed (ARCH-023 exempts organisation-scope… | #183, #24 | 1200 | — |
| `e10-f03-2` | 1 | A | **Project stocktake variance, stock aging and reorder status, and reconcile value against IValuationQuery** — the value half of the stock reports that the seven published Inventory events actually support — stocktake variance, aging over receipts and consumptions, reorder status, every valued figure stamped with its valuation run, and the IValuationQuery reconciliation; wastage is car… | `e10-f03-1`, #183, #40, #39 | 1000 | — |
| `e10-f03-3` | 2 | A | **Stock analytics read endpoints, trends and the report column policy** — four GET routes over the stock projections with AssignedBranches scope, 20/50 keyset paging copied from IInvoiceStore, day/week/month trends, and ReportingResponseViews on a new ViewSurface.Management composed into ApplicationResponseViews.All, so a Tailor Master sees quantiti… | `e10-f03-1`, `e10-f03-2`, #183, #186 | 1150 | — |
| `e10-f03-5` | 2 | A | **Project estimated order and garment-job profitability with an explicit completeness model** — revenue minus material, labour, overhead, discounts, credits and wastage, every component carrying its availability reason and provenance — order-level invoiced revenue from the four Billing events with an invoice-to-order map because the credit and debit notes carry no OrderI… | `e10-f03-1`, `e10-f03-2`, `e10-f03-4`, #183, #184, #185, #42, #201, #39, #40 | 1250 | — |
| `e10-f03-6` | 3 | A | **Profitability read endpoints with the method-and-assumptions panel and the margin column gate** — three GET routes answering the margin with the stored estimated-and-incomplete label, the assumption version and valuation run named, every money column a gated nullable field, and a real ARCH-023 resource scope — ReportingResourceKinds.JobProfitability resolved from the proje… | `e10-f03-5`, `e10-f03-4`, `e10-f03-3`, #186 | 800 | — |
| `e10-f03-9a` | 3 | B | **Client: the reporting foundation, the shared status strip and export control, and the stock position screen** — clients/pwa/src/reporting/ with its API module, types, permission constants and fixtures; ReportStatusStrip and the shared ReportExportAction inside OfflineBlockedAction; one screen at /reports/stock; the two-language message family; route tests and stories through withAdminAp… | `e10-f03-3`, #186, #184 | 1100 | — |
| `e10-f03-7` | 4 | A | **Govern the stock and profitability exports: column policy, classification header and export audit** — the report-definition registry whose column set is the caller's own field-visibility mask, applied at generation time with the requester's authority rebuilt by CreateScopeForAsync, plus the audit entries, the Confidential header, the data-as-of read from projection_checkpoints… | #186, `e10-f03-3`, `e10-f03-6`, `e10-f03-1`, `e10-f03-2`, `e10-f03-5` | 800 | — |
| `e10-f03-9b` | 4 | B | **Client: the stock movements-and-trends screen and the reorder-and-variance screen** — two screens on the e10-f03-9a foundation — /reports/stock/movements with its day/week/month trend view, and /reports/stock/reorder with the stocktake variances beside it — walked against A11Y-DB-01..09 including -04, -05 and -06, which are the low-stock items this is the first… | `e10-f03-9a`, `e10-f03-3`, `e10-f03-2` | 950 | — |
| `e10-f03-10a` | 5 | B | **Client: the estimated-profitability screen and its method-and-assumptions panel** — /reports/profitability — list, trends and a detail panel that names the metric definition, the assumption version and its window, the valuation method and run, and every unavailable component in words, with the estimated-and-incomplete label taken from the server and every mon… | `e10-f03-6`, `e10-f03-9a`, `e10-f03-9b`, `e10-f03-4` | 800 | — |
| `e10-f03-2b` | 5 | A | **BLOCKED on an additive Inventory movement event: wastage, return and adjustment analytics** — projections_stock_wastage, one GET /stock/wastage route and the wastage panel on the already-shipped movements screen — and it DOES NOT START until Inventory publishes a per-ledger-entry movement event, whose exact shape this body specifies so #39's own decomposition can carry… | #39, `e10-f03-2`, `e10-f03-3`, `e10-f03-7`, `e10-f03-9b`, #40 | 650 | — |
| `e10-f03-8` | 5 | A | **Bound reporting load: row-count refusal, export queue, concurrency limits and cancellation** — the 50,000-row cap (capacity section 2.3, verified) enforced as an RFC 9457 refusal that offers the asynchronous path, a leased queue with per-branch and global bounds, one cancel route with a real resource scope, a deterministic isolation test, and the k6 scenario S4 run defe… | #186, #183, `e10-f03-3`, `e10-f03-6`, `e10-f03-7`, #58 | 900 | — |
| `e10-f03-10b` | 6 | B | **Client: the costing-assumptions editor — draft, edit and publish with a mandatory reason** — /admin/costing-assumptions inside AdminShell — the version list, the draft editor for overhead, basis and the rate rows built on the shared form components, and publish behind ConfirmDialog with a mandatory reason, edited with apiRequestVersioned and If-Match (legitimate becau… | `e10-f03-4`, `e10-f03-9a`, `e10-f03-10a` | 850 | — |

### 4.13 #48 — E11-F02 delivery queue, customer status, dispatch

12 units, about 10,850 production lines, in dependency order.

| Unit | Lv | Lane | What it delivers | Depends on | Lines | Issue |
| --- | --- | --- | --- | --- | --- | --- |
| `e11-f02-5` | 0 | A | **The customer status page: the status purpose's content provider in the web host and its configured amount display** — Implement the status purpose's content provider in src/Hosts/Tailor360.Web against #211's merged per-purpose contract and render /c/status/{token} — minimal per-garment progress mapped totally from the seven GarmentJobState members, an amount block governed by CustomerStatus:A… | #211, #201 | 800 | — |
| `e11-f02-1` | 1 | A | **The delivery queue: project the ready gate into custody.delivery_queue_entries and read it** — Create custody.delivery_queue_entries with one expand-only migration named the way the repository actually names them, consume orders.job-ready-for-delivery.v1 into it with a CI-03 staleness guard, add the pure DeliveryDispatchPolicy and CustodyDispatchOptions (WholeOrder \| P… | #190, `E07-F03-1 (under #37)`, #34, #201 | 1200 | — |
| `e11-f02-2` | 2 | A | **Two-stage dispatch, stage one: the delivery-team receive scan, the dispatch authorisation and the fail-closed payment gate** — Add custody.dispatch_authorisations and its job child table, make POST /custody/orders/{orderId}/delivery-receive the one place the payment rule blocks by mapping all six string reasons from the merged IDispatchEligibilityQuery and consuming an approved exception exactly once … | `e11-f02-1`, `E07-F03-1 (under #37)`, `E07-F03-2 (under #37)`, #190, #34 | 1350 | — |
| `e11-f02-7` | 2 | B | **Client: the delivery queue and the order's stop card** — Found clients/pwa/src/delivery/ (api, types, permission constants, problem alert, synthetic fixtures, en/ta messages) and build the two read screens the shell already links to but the router does not have — /delivery, the branch queue grouped by order with ready/blocked/overdu… | `e11-f02-1` | 1100 | — |
| `e11-f02-3a` | 3 | A | **The doorstep handover: custody.delivery_confirmations, the confirm command and DeliveryConfirmed** — Add custody.delivery_confirmations with one confirmation per authorisation enforced by a unique index, accept the handover by typed recipient name plus either a code verified in fixed time against a stored digest or a ≤10 KB signature stroke per Custody:Dispatch:RecipientConfi… | `e11-f02-2`, `e11-f02-1`, `E07-F03-1 (under #37)`, `E07-F03-2 (under #37)`, #190, #34 | 1000 | — |
| `e11-f02-8a` | 3 | B | **Client: the delivery-team receive scan, the blocked refusal and the irreversible dispatch scan** — Build the two gate screens of the priority-zero journey — the receive scan rendering custody.dispatch-blocked as one aria-live announced sentence naming what was refused, the figure and the two ways out with nothing disabled, and the dispatch scan stating the irreversibility b… | `e11-f02-7`, `e11-f02-2`, #36 | 900 | — |
| `e11-f02-3b` | 4 | A | **The doorstep one-time code: mint it, protect it, and carry it to the customer without a mechanism that does not exist** — Mint a six-digit code from RandomNumberGenerator valid ten minutes with five attempts per delivery attempt, store a digest for verification and a Data-Protection-ring-protected copy for exactly one read, return the expiry and the attempts remaining and never the code, publish … | `e11-f02-3a`, `e11-f02-2`, #190 | 900 | — |
| `e11-f02-4` | 7 | A | **Failed, returned and disputed handovers: compensating custody, the reopened queue entry and the delivery timeline** — Record a failed or returned delivery with a mandatory configured reason code seeded through a new ICustodyReferenceDataSeeder, spend the dispatch authorisation so it can never be reused, request the compensating custody transfer and its scan event through [E07-F03-2] and [E07-… | `e11-f02-3a`, `e11-f02-2`, `e11-f02-1`, `E07-F03-1 (under #37)`, `E07-F03-2 (under #37)`, `E07-F03-3 (under #37)`, `E07-F03-5 (under #37)`, #190, #34 | 1200 | — |
| `e11-f02-6` | 8 | A | **Delivery messages: the Templates/delivery.* set, the four custody routing rules and the status link they mint** — Add four Notifications routing rules over custody.dispatch-recorded / delivery-confirmed / delivery-failed / delivery-returned, each raising one consent-gated, de-duplicated intent with its own HandlerName (delivery-confirmed will also carry #49's [E11-F03-5] handler), the Tem… | #208, #209, #210, #211, `e11-f02-5`, `e11-f02-2`, `e11-f02-3a`, `e11-f02-4` | 950 | — |
| `e11-f02-8b` | 8 | B | **Client: the doorstep handover, the failed delivery and the dispute** — Build the doorstep screen whose primary path is a typed recipient name plus the six-digit code with a Send code action and the attempts left visible, the signature pad rendered only where the branch policy asks for it and never the only way through (A11Y-75, A11Y-78), plus the… | `e11-f02-8a`, `e11-f02-3a`, `e11-f02-3b`, `e11-f02-4` | 1000 | — |
| `e11-f02-6b` | 9 | A | **The confirmation-code message: one routing rule, one template, and the render-time read that spends the code** — Consume custody.delivery-confirmation-code-issued.v1 into exactly one transactional intent, add Templates/delivery.confirmation-code in en-IN and ta-IN whose allowlist is the code, the order number and the expiry and nothing else, and pull the plaintext once at render time thr… | `e11-f02-3b`, `e11-f02-6`, #208, #209, #210 | 450 | — |
| `e11-f02-9` | 10 | A | **Evidence only: the paid and blocked dispatch rehearsals, the delivery-team UAT in installed mode and the A11Y-PZ-03 screen-reader walk** — No production code: the physical and human evidence #48 asks for and no coding session can produce — the paid-dispatch rehearsal with a printed label and a real scanner, the blocked-dispatch rehearsal through both documented remedies, the failed-delivery and return rehearsal, … | `e11-f02-5`, `e11-f02-6`, `e11-f02-6b`, `e11-f02-7`, `e11-f02-8a`, `e11-f02-8b`, #190, #34, #35, #36, #208, #210, #211 | — | — |

### 4.14 #49 — E11-F03 feedback, alterations, service recovery

9 units, about 6,880 production lines, in dependency order.

| Unit | Lv | Lane | What it delivers | Depends on | Lines | Issue |
| --- | --- | --- | --- | --- | --- | --- |
| `e11-f03-1` | 0 | A | **Capture a feedback response through its one-time link, and open the case it earns** — Notifications.Contracts.Feedback.IFeedbackCapture taking an opaque redacting FeedbackLinkToken (GetForm/Submit/Update), plus the handler that validates the five ratings server-side, re-checks delivery eligibility through IOrderSnapshotQuery, writes the response through #213's … | #208, #213, #211 | 700 | — |
| `e11-f03-2` | 1 | A | **Serve the customer feedback page at /c/feedback/{token}** — Two justified-anonymous, DefaultIp-rate-limited routes (GET and POST /c/feedback/{token}) rendering an accessible English and Tamil form outside the PWA shell, the POST audited, anti-forgery validated through the hidden __RequestVerificationToken field written from IAntiforger… | `e11-f03-1`, #211, #213, #208 | 800 | — |
| `e11-f03-3` | 1 | A | **Read feedback responses and the service-recovery queue, with the free-text visibility rule** — Four feedback.read routes (responses list and detail, cases list and detail) keyset-paged on the AuditQuery 50/200 precedent, with NotificationsResponseViews declaring the comment CustomerNotes so it appears in exactly the two detail payloads under an explicit read audit, and … | `e11-f03-1`, #213, #208 | 1100 | — |
| `e11-f03-5` | 1 | A | **Invite, confirm and escalate: the feedback event edges and the overdue sweep** — Two outbox handlers — custody.delivery-confirmed.v1 raising one invitation per delivered ORDER through IConsentQuery/ICommunicationPreferenceQuery and #209's gate (with docs/prd/state-transitions.md line 133 amended in the same pull request, because it asks for one per garment… | `e11-f03-1`, #208, #209, #211, #213 | 780 | — |
| `e11-f03-3b` | 2 | A | **Feedback metrics, the metric dictionary and the feedback entries on the customer timeline** — One feedback.read metrics route under RequestTimeoutPolicies.Report (five means and distributions, response rate, alteration and contact rates, cases opened and closed, median and p90 closure) with a null mean distinguished from a real zero, plus docs/reports/feedback-metrics.… | `e11-f03-3`, #212, #213, #208 | 600 | — |
| `e11-f03-4` | 2 | A | **Own, contact, close a service-recovery case, edit its policy, and hand an accepted alteration to Orders** — Five routes — assign, contact and close under feedback.manage_cases with reason, idempotency, ScopedToResource and audit, plus the branch policy read emitting an entity tag and the PUT behind RequireIfMatch — calling IAlterationRequests.OpenAsync(..., AlterationSource.Feedback… | `e11-f03-3`, #213, #208 | 1100 | — |
| `e11-f03-6` | 2 | B | **Client: the feedback feature folder and the service-recovery queue screen** — The feature folder every feedback screen needs (feedbackApi with the five read functions, feedbackPermissions, types, feedbackProblems, fixtures, the two-locale message family, the stylesheet, the navigation entry) plus /service-recovery with its Cases and Feedback tabs, filte… | `e11-f03-3` | 950 | — |
| `e11-f03-6b` | 3 | B | **Client: the service-recovery case screen and its three commands** — /service-recovery/:caseId with the case, its source response including the comment and its contact attempts as a Timeline, plus assign, contact and close as dialogs gated on feedback.manage_cases so an Owner sees the case and no buttons, each collecting its mandatory reason in… | `e11-f03-6`, `e11-f03-4` | 850 | — |
| `e11-f03-7` | 4 | C | **Evidence only, no code: UAT journeys, real-device installed-mode walk and the manual accessibility pass** — The work no coding session can do and on which none must therefore be blocked: the three UAT journeys with their negative cases, the Tier 1 device walks in tab and installed mode, the browser axe run over both client routes, the three dialogs and the server-rendered page in bo… | `e11-f03-2`, `e11-f03-4`, `e11-f03-5`, `e11-f03-6`, `e11-f03-6b` | — | — |

### 4.15 #50 — E12-F01 design system and role layouts

7 units, about 2,890 production lines, in dependency order.
1 of them is evidence rather than code and needs a person, not a session.

| Unit | Lv | Lane | What it delivers | Depends on | Lines | Issue |
| --- | --- | --- | --- | --- | --- | --- |
| `e12-f01-1` | 0 | A | **Store the text size on identity.user_preferences and add the self-service preferences write** — Add InterfaceTextSize and TextSize to UserPreferences with one expand-only migration (AddUserPreferenceTextSize), StaffUser.EnsurePreferences, a PreferencesHandler and a full-replacement PUT /api/v1/me/preferences carrying all seven members including timeZoneId, declared Requi… | — | 520 | — |
| `e12-f01-3` | 0 | B | **Drive the shell's navigation, primary action and landing route from the session** — Add shellPermissions.ts quoting twelve real keys from Platform.Security/Permissions, give every destination the permission its target route already declares plus a route-registered flag, correct the /billing and /settings hrefs and the takePayment action's never-registered /bi… | — | 600 | — |
| `e12-f01-5` | 0 | C | **Evidence only (no code, needs real devices and two signatures): walk the eight reference journeys with VoiceOver and TalkBack and record the device matrix** — Create docs/nfr/a11y-records/ under the A11Y-OD-06 default path and commit five completed records for the A11Y-RJ journeys that have records of their own (RJ-02, -03, -04, -05, -08; RJ-01, -06 and -07 are covered by A11Y-PZ-01, -05 and -03 and must not be walked twice), each w… | — | — | — |
| `e12-f01-2` | 1 | B | **Client: apply the account's theme, text size, density and reduced motion at sign-in, and save a person's own changes** — Implement createServerDisplayPreferencesStore behind the existing two-method DisplayPreferencesStore interface, swap main.tsx to AppIntlProvider → SessionProvider → DisplayPreferencesProvider so the provider can read the session, apply the account's theme, text size, density a… | `e12-f01-1` | 600 | — |
| `e12-f01-3b` | 1 | B | **Replace the placeholder home screen with a destination launcher** — Move HomeRoute into src/routes/home/ and turn it into a launcher that renders one Card per destination useSessionNavigation() already offers, each card's accessible name identical to its navigation label (A11Y-10) so the second route to every screen satisfies 2.4.5, the primar… | `e12-f01-3` | 420 | — |
| `e12-f01-2b` | 2 | B | **Client: apply the account's locale to the interface and <html lang> at sign-in** — Add a subscribable src/i18n/accountLocale.ts written from the single choke point SessionProvider.rememberUser and consumed by AppIntlProvider through useSyncExternalStore, so active = explicit prop ?? account ?? device, the account's ta-IN renders and <html lang> follows it, t… | `e12-f01-2` | 350 | — |
| `e12-f01-4` | 2 | B | **Close #50's mechanical gates: the two localisation lint rules, Storybook in CI, component docs and the traceability re-verdict** — Make NFR-LO-01 and NFR-LO-04 mechanical as client lint rules with their carve-outs and the one Intl.ListFormat use in Forbidden.tsx resolved into formatters.ts, add build-storybook to the build-test-pwa job, tag the component families autodocs and add one MDX overview, correct… | `e12-f01-3b` | 400 | — |

### 4.16 #51 — E12-F02 installable PWA, safe updates, resilience

9 units, about 4,220 production lines, in dependency order.

| Unit | Lv | Lane | What it delivers | Depends on | Lines | Issue |
| --- | --- | --- | --- | --- | --- | --- |
| `e12-f02-1` | 0 | B | **Register the service worker, precache the build, cache only the allowlisted reference reads, and mark them as stale** — Switch vite-plugin-pwa to injectManifest, write a source service worker whose routing defaults to network-only, precache the hashed build, serve the shell from cache only when the network fails, stale-while-revalidate the two catalogue reference reads and mark them visibly as … | — | 790 | — |
| `e12-f02-2` | 1 | B | **Detect a new build, prompt for it, activate only on consent, and refuse an incompatible client** — Build the update state machine over the waiting worker and GET /api/version, a persistent (never toast) update prompt with a non-dismissable required form answering A11Y-OF-08, skipWaiting on consent only with one reload, a 426 latch in apiClient that fails unsafe methods fast… | `e12-f02-1` | 560 | — |
| `e12-f02-3a` | 1 | B | **The encrypted device store: keys, binding, bounds, quota and the sign-out shred** — Create the one sanctioned device store — AES-GCM records under a non-extractable key in IndexedDB, bound to a hashed userId and branch, expiring on SM-07's 24 hours, with quota detection, navigator.storage.persist() at sign-in and install, a lowerable-only depth override so A1… | `e12-f02-1` | 540 | — |
| `e12-f02-3b` | 2 | B | **Autosaved local drafts and the restore notice in the measurement capture wizard** — Give the device store its first consumer: a keyed local-draft adapter, a debounced autosave that never leads the server, a discard on confirmation and close, an explicit ask-before-restore notice announced through the autosave region, and the plain storage-full sentence that n… | `e12-f02-3a` | 340 | — |
| `e12-f02-4` | 2 | B | **The bounded offline queue: the typed operations allowlist, binding, and the replay engine** — Build the persistent encrypted queue on the device store: a single typed allowlist empty at merge and restricted for ever to custody.scan and custody.confirm_delivery under /api/v1/custody/, enqueue-time Idempotency-Key and client event UUID, refusal at the depth bound rather … | `e12-f02-3a` | 820 | — |
| `e12-f02-5a` | 3 | B | **Publish the queue into the shell: the sync status region, the not-yet-sent marker and the update prompt's pending count** — Map the queue onto the shell's reserved, non-dismissable sync channel in one stated priority order — full, conflict, cannot-send, sending, drain outcome, waiting — build the inline not-yet-sent marker that takes its sentence as a required message id so #36 and #48 own their ow… | `e12-f02-4`, `e12-f02-2` | 330 | — |
| `e12-f02-5b` | 4 | B | **The /sync screen, the eviction notice and the acknowledged sign-out with queued work** — Build /sync inside RequireSession with no permission gate and no navigation entry — waiting, needs-your-attention with the server problem named by operation and job number, and cannot-be-sent-from-this-device with reasons in words and no send control — plus the start-up evicti… | `e12-f02-5a` | 640 | — |
| `e12-f02-6a` | 5 | B | **Offline and support documentation, the device-privacy and quota checks, and the honest traceability statuses** — Write docs/pwa/offline-and-resilience.md and docs/support/pwa-troubleshooting.md citing rather than restating the support matrix, add the cache-and-storage privacy inspection and the quota-exhaustion test with four deliberately broken runs as proof they can fail, move NFR-CL-0… | `e12-f02-1`, `e12-f02-2`, `e12-f02-3a`, `e12-f02-3b`, `e12-f02-4`, `e12-f02-5a`, `e12-f02-5b` | 160 | — |
| `e12-f02-6b` | 6 | B | **Evidence only, real devices required: install, installed mode, offline reload, update and rollback per Tier 1 device class** — Record the five steps — install, installed-mode confirmation, offline reload, update, rollback — in a browser tab and in installed mode on each available Tier 1 device class with versions logged, append the evidence table, state in one line why the print station needs no separ… | `e12-f02-6a` | 40 | — |

### 4.17 #52 — E12-F03 accessibility, cross-browser, performance

9 units, about 5,520 production lines, in dependency order.

| Unit | Lv | Lane | What it delivers | Depends on | Lines | Issue |
| --- | --- | --- | --- | --- | --- | --- |
| `e12-f03-1a` | 0 | A | **Seed synthetic staff accounts and their authenticator enrolments, and share the key ring with the CLI** — One staff account per seeded role with a fixed identifier, a password from TAILOR360_SEED_PASSWORD and a fixed per-role base-32 TOTP secret — plus the one line that makes the CLI share the web host's data-protection key ring, without which the protected secret is unreadable an… | #214 | 480 | — |
| `e12-f03-4` | 0 | A | **Ingest client telemetry: POST /api/v1/telemetry/client, the declared-origin gate, the redaction allowlist and the event-to-log mapping** — The anonymous, rate-limited ingest endpoint in the web host under Telemetry/, with an allowlist rather than a denylist, the telemetry-ingest policy the catalogue names but does not carry, a per-endpoint declared-origin filter that replaces the same-origin guarantee the anti-fo… | — | 950 | — |
| `e12-f03-1b` | 1 | B | **Found the end-to-end suite: nine Playwright projects, the sign-in fixture and the @smoke journeys the seeded data supports** — tests/e2e as its own pnpm project with exactly nine Chromium/Firefox/WebKit x phone/tablet/desktop projects, a real sign-in fixture, a node:crypto TOTP generator, ten page objects, a @smoke spec confined to the journeys the seeded data can complete, and one end-to-end CI job o… | `e12-f03-1a` | 780 | — |
| `e12-f03-2` | 2 | B | **Run axe, reflow and the keyboard pass in a real browser, and open the accepted-violation register** — The enforcing accessibility gate the code already points at #52 for: axe with color-contrast and target-size re-enabled, expectNoHorizontalOverflow driven by a real Playwright page at its own unnarrowed 320/360/768/1024/1280 px and 100/200% defaults, a keyboard-only pass over … | `e12-f03-1b` | 700 | — |
| `e12-f03-6` | 2 | B | **Measure the mobile performance budgets: size-limit, Lighthouse CI, the memory probe and the baseline report** — Turns the ten budget rows of capacity-and-performance.md section 3.3 that name a client-side gate into gates that exist — size-limit per entry, Lighthouse CI over nine named URLs on the section 2 throttling profile with a Puppeteer sign-in for the authenticated ones, a Chromiu… | `e12-f03-1b` | 600 | — |
| `e12-f03-3` | 3 | B | **Detect device capabilities, document the fallback ladders and refuse an unsupported configuration** — One pure synchronous capability module over fourteen keys — with the camera split into a required API and an optional device — the unsupported-configuration refusal page support-matrix.md section 1.1 specifies, an About diagnostics section, the four missing fallback ladders in… | `e12-f03-1b`, `e12-f03-2` | 880 | — |
| `e12-f03-7` | 3 | B | **Apply the measured optimisations: route-level code splitting and chunk boundaries** — The applied half of 'measured first, then applied': feature-area React.lazy boundaries for the admin, catalogue, measurement and billing routes that every phone currently downloads before its first paint, qrcode-generator moved out of the first-paint graph by both a manualChun… | `e12-f03-6`, `e12-f03-2` | 380 | — |
| `e12-f03-8` | 3 | C | **Stand up the manual accessibility and device-evidence instruments, and defer every human-run pass (evidence child, no code)** — Turns a11y-checklist.md section 8 into files a runner copies instead of hand-building, creates docs/nfr/a11y-records/ with pre-filled but deliberately UNSIGNED record files and the device-evidence log, and records every pass that needs a human or a device — keyboard, environme… | `e12-f03-1a`, `e12-f03-2`, `e12-f03-6` | — | — |
| `e12-f03-5` | 4 | B | **Build the browser telemetry module: web vitals, unhandled errors, batching, sampling and redaction** — The reporter vite.config.ts line 61 already names #52 for: web-vitals per route, window-level errors reduced to a code and an async stack hash with a documented non-cryptographic fallback, a bounded in-memory buffer flushed on pagehide through a sendBeacon Blob typed applicati… | `e12-f03-4`, `e12-f03-3`, `e12-f03-1b` | 750 | — |

### 4.18 #56 — E14-F01 threat models and ASVS baseline

18 units, about 7,150 production lines, in dependency order.

| Unit | Lv | Lane | What it delivers | Depends on | Lines | Issue |
| --- | --- | --- | --- | --- | --- | --- |
| `E14-F01a-1` | 0 | C | **Write the abuse-case catalogue and open the threat-model programme** — The eleven ABF-01..ABF-11 abuse-case families with their control patterns, what defeats each today, and the per-flow coverage map; plus the family column in the threat-model template, the README index rewrite, and the rule that freezes the identifiers on merge because seven la… | — | 450 | — |
| `E14-F01a-2` | 0 | C | **Build the ASVS Level 2 traceability skeleton with owners and verdicts** — docs/security/asvs-traceability.md: one row per Level 2 requirement with level, flow model, control owner, a four-value verdict and covering issue, the chapter-to-NFR-to-gate cross-reference, and authentication.md's empty section 10 filled. | — | 500 | — |
| `E14-F01a-7` | 0 | C | **Vulnerability management, the security exception register, and the expiry gate in CI** — vulnerability-management.md, SECURITY.md and exceptions.md, plus a kind discriminator on .github/sarif-accepted.json so a false positive keeps no expiry while an accepted risk carries an approver, a SEC-EX identifier and a window the severity bounds, enforced in scripts/sarif-… | — | 600 | — |
| `E14-F01a-3` | 1 | C | **Threat models: customer data and media, customer links** — customer-and-media.md (live customer, consent, merge, measurement and export surfaces plus the designed media pipeline) and customer-links.md (purpose-bound expiring links), each with STRIDE, controls mapped to tests and owned residual risk. | `E14-F01a-1`, `E14-F01a-2` | 700 | — |
| `E14-F01a-4` | 1 | C | **Threat models: order workflow, barcode custody, inventory** — order-workflow.md, barcode-custody.md and inventory.md over the thirteen orders tables including job_ready_state: the workflow-bypass, barcode-replay and stock-manipulation flows, with the two fail-closed defaults as named controls. | `E14-F01a-1`, `E14-F01a-2` | 850 | — |
| `E14-F01a-5` | 1 | C | **Threat models: billing and payments, reports and exports** — billing-payment.md over the already-live money surface (thirteen migrations, thirty-nine tables, sixty-five endpoint registrations, the golden master) and reports-exports.md over the designed projection and governed-export surface. | `E14-F01a-1`, `E14-F01a-2` | 700 | — |
| `E14-F01a-6` | 1 | C | **Threat models: integration adapters, deployment and the runtime** — integrations.md (webhooks, callbacks, the accounting export, the print bridge, and the unimplemented IOutboundHttp as an owned residual risk) and deployment.md (proxy, secrets under /run/secrets, key ring, supply-chain gates), keeping authentication.md's two forward references. | `E14-F01a-1`, `E14-F01a-2` | 700 | — |
| `E14-F01a-8` | 1 | C | **Add the licence audit over the CycloneDX bill of materials and wire the RG-08 gate** — scripts/licence-audit.py with a self-test and defined multi-licence and unparseable-expression semantics, .github/licence-allowlist.json derived from the bill of materials of main, the gate wired into the existing sbom job, and RG-08's detail row made true. | `E14-F01a-7` | 450 | — |
| `E14-F01b-2` | 1 | C | **Security regression suite: insecure direct object reference, privilege escalation, and the shared abuse-case machinery** — ABF-01 and ABF-02 as sequence tests, plus the folder, the eleven family traits, the five named probe principals and the coverage harness that discovers one predicate file per family — the machinery all six later suites extend without editing a shared file. Must be taken first … | `E14-F01a-1`, #199, #201 | 120 | — |
| `E14-F01b-1` | 2 | C | **Harden the enforcing content security policy with Trusted Types, and review central validation and encoding** — require-trusted-types-for 'script' and a single named policy in SecurityHeadersMiddleware, one client Trusted Types module with a feature check, the lint rule closing the four innerHTML sites in two files, the COEP decision, and the validation and output-encoding review record… | `E14-F01a-6` | 260 | — |
| `E14-F01b-2b` | 2 | C | **Security regression suite: workflow bypass, barcode replay and the no-client-driven-state rule** — ABF-03 and ABF-04 as sequences over precondition-bearing transitions and the custody scan surface, with the two fail-closed defaults asserted as defaults, plus the no-client-driven-state rule enforced over every state-changing endpoint by reflection over the Api payload types. | `E14-F01a-1`, `E14-F01a-4`, `E14-F01b-2` | 120 | — |
| `E14-F01b-3` | 2 | C | **Security regression suite: invoice and payment tampering** — ABF-05 against the already-live money surface: posted-invoice and payment immutability, allocation and reversal bounds, receipt reprint versus second receipt, cashier-close rules, and the Billing-owned dispatch exception — with enforced coverage over all sixty-five money-movin… | `E14-F01a-1`, `E14-F01a-5`, `E14-F01b-2` | 120 | — |
| `E14-F01b-3b` | 2 | C | **Security regression suite: stock manipulation and export leakage** — ABF-06 ledger immutability, backdating, negative stock and self-approved variance against the #39/#40 surface — not #38's masters — and ABF-08 exports that cannot widen a field set, cross a branch, be fetched by identifier or carry a formula, with the live customer export keep… | `E14-F01a-1`, `E14-F01a-4`, `E14-F01a-5`, `E14-F01b-2`, #39, #40, #183, #186, #198 | 150 | — |
| `E14-F01b-4` | 2 | C | **Security regression suite: malicious upload and authorised media streaming** — ABF-07: a documented hostile corpus (disagreeing magic bytes, script-bearing SVG, polyglot, archive, decompression bomb, hostile filename) refused or neutralised, metadata stripped, quarantine closed on every route, storage keys random, and no storage URL in any body or header… | `E14-F01a-1`, `E14-F01a-3`, `E14-F01b-2`, #187, #188 | 180 | — |
| `E14-F01b-4b` | 2 | C | **Security regression suite: server-side request forgery and the outbound edge** — ABF-09 against an IOutboundHttp that has no implementation: the address, host-encoding, redirect, scheme and size rules as a unit suite ready for the day one lands, the callback and allowlist sequences where routes exist, secret redaction across five sinks, and a registered da… | `E14-F01a-1`, `E14-F01a-6`, `E14-F01a-7`, `E14-F01b-2` | 160 | — |
| `E14-F01b-4c` | 2 | C | **Security regression suite: credential abuse and denial-of-service limits** — ABF-10 stuffing across accounts with both partitions proved independent, enumeration closed through body, status and clock on every credential endpoint, the half-signed-in story composed, and revocation propagation; plus ABF-11 exhausting all eight rate-limit policies, page an… | `E14-F01a-1`, `E14-F01b-2` | 140 | — |
| `E14-F01b-5` | 3 | C | **Audit the ASVS traceability sheet against the landed models and suites** — Fill every reserved column and downgrade anything whose control is absent or whose named test was not actually run in this pull request's evidence, reconcile the sheet against the ten models and the coverage harness's eleven-family report, raise every finding, and wire the aud… | `E14-F01a-1`, `E14-F01a-2`, `E14-F01a-3`, `E14-F01a-4`, `E14-F01a-5`, `E14-F01a-6`, `E14-F01a-7`, `E14-F01b-1`, `E14-F01b-2`, `E14-F01b-2b`, `E14-F01b-3`, `E14-F01b-3b`, `E14-F01b-4`, `E14-F01b-4b`, `E14-F01b-4c` | 450 | — |
| `E14-F01b-6` | 4 | C | **Prepare and run the independent penetration test with verified remediation** — The engagement pack a session finishes — scope, synthetic environment, twelve roles across two branches, rules of engagement with a stop condition, report data-handling terms, a path-and-epic re-test trigger and the empty remediation register — after which the issue stays open… | `E14-F01b-5` | 500 | — |

### 4.19 #57 — E14-F02 privacy, audit, encryption, secrets

12 units, about 10,050 production lines, in dependency order.

| Unit | Lv | Lane | What it delivers | Depends on | Lines | Issue |
| --- | --- | --- | --- | --- | --- | --- |
| `e14-f02-1` | 0 | A | **Establish the retention-policy register, the data-class catalogue, the effective-policy resolver and the erasure-participant port** — platform.retention_policies as append-only effective-dated configuration; the twenty DataClass keys listed verbatim so every sibling names the same strings; IRetentionPolicies answering Pending rather than substituting a period; IErasureParticipant declared here rather than wi… | #21, #24, #25 | 1250 | — |
| `e14-f02-5` | 0 | A | **Close the audit-integrity loop: scheduled chain verification, anchored chain heads and correlation-id search** — A leased job that finally calls the already-built platform.verify_audit_chain, platform.audit_chain_anchors with head-disagreement detection for a restore from an unexpected point, the correlation-id filter and partial index the parent's third criterion is currently unmeetable… | #21, #24, #25 | 780 | — |
| `e14-f02-5b` | 0 | A | **Audit completeness: the correlation-id harness, its exemption register and the stored-payload redaction assertion** — The run-time half of ARCH-008: a harness that drives every state-changing route it can and asserts the audit entry's correlation_id equals the response header, a machine-read audit-completeness.yaml exemption register with a fixed reason vocabulary that fails both on a missing… | #21, #22, #24 | 60 | — |
| `e14-f02-6` | 0 | C | **Prove secret and key rotation: the runbooks, key ownership, emergency revocation and a measured rotation rehearsal** — Creates docs/runbooks/ with one rotation procedure per row of the fourteen-row section 6.2 register, each carrying a measured service impact and the key-ownership column the plan had put in a data inventory nobody is creating, plus the emergency-revocation decision table, the … | #23, #26 | 60 | — |
| `e14-f02-2` | 1 | A | **Build the data-subject request register and its four-eyes approval** — platform.data_subject_requests with its state machine and a four-eyes rule enforced in the aggregate, xmin concurrency rather than an invented row_version column, five admin endpoints reusing the already-granted customers.request_deletion with the step-up on the create route w… | `e14-f02-1`, #21, #24, #25, #26 | 1280 | — |
| `e14-f02-3` | 1 | A | **Customers: processing restriction and the pseudonymisation participant** — Restriction as a flag that survives a correction, two endpoints on the existing customers.restrict key with no step-up because the catalogue forbids it, ProcessingRestricted added to the real five-member CommunicationPreference record, and the first IErasureParticipant impleme… | `e14-f02-1`, #26, #28 | 1250 | — |
| `e14-f02-9` | 1 | A | **Orders: clear the frozen measurement and design snapshots and the job media links on an approved erasure** — The half of measurement erasure nobody had assigned: an OrdersErasureParticipant clearing measurement_snapshots values and ease notes, design_snapshots garment instructions and conditional notes, and garment_jobs.reference_media_ids on delivered, closed or cancelled jobs only … | `e14-f02-1`, #32 | 850 | — |
| `e14-f02-4` | 2 | A | **Build retention holds and the retention-run exception report, with their administration routes** — platform.retention_holds and platform.retention_runs with one expand-only migration, the IRetentionHolds predicate and its five unit assertions proved here so the job inherits a settled rule, IRetentionHoldAdministration, and four hold and exception-report endpoints on admin.p… | `e14-f02-1`, `e14-f02-2`, #21, #24, #25 | 1000 | — |
| `e14-f02-7` | 2 | B | **Client: the retention policy register and the data-subject request register** — Two list-shaped admin screens — /admin/retention showing the policy in force per data class and naming the open decision id where a class has none, and /admin/privacy paging the request register — with state stories through withAdminApi, the AdminScreens.test.tsx harness rathe… | `e14-f02-1`, `e14-f02-2` | 1080 | — |
| `e14-f02-4b` | 3 | A | **Run the privacy lifecycle: the leased worker, the hold check, statutory exclusions and the run record** — A leased PrivacyLifecycleService whose one participant loop serves both the approved-request pass and the per-policy sweep, the hold check first and per class, a Pending decision as a skip that on seeded data is eighteen of twenty classes, a pinned statutory-exclusion set, one… | `e14-f02-1`, `e14-f02-2`, `e14-f02-4`, `e14-f02-3`, #21, #24 | 900 | — |
| `e14-f02-8` | 3 | B | **Client: the erasure decision screen, with the four-eyes refusal as a designed state** — /admin/privacy/requests/:requestId with approve and refuse behind confirmations, reasons, If-Match and a caller-minted Idempotency-Key, the requester's own Approve disabled with a visible explanation that the server's 409 privacy.approval-by-requester renders identically, open… | `e14-f02-2`, `e14-f02-4`, `e14-f02-7` | 780 | — |
| `e14-f02-8b` | 3 | B | **Client: the retention-hold register and the exception report** — /admin/privacy/holds with place and release behind reasons, confirmations and If-Match — and a typed subject identifier validated client-side because resolving it to a name would put a name on a privacy screen — plus /admin/privacy/runs grouping each skip under its reason code… | `e14-f02-4`, `e14-f02-7` | 760 | — |

### 4.20 #58 — E14-F03 observability, SLO alerting, resilience

14 units, about 8,570 production lines, in dependency order.

| Unit | Lv | Lane | What it delivers | Depends on | Lines | Issue |
| --- | --- | --- | --- | --- | --- | --- |
| `e14-f03-1` | 0 | A | **Declare the signal catalogue and instrument the platform's golden and integrity signals** — Move Tailor360Diagnostics into Abstractions, declare SignalNames and the IIntegritySignalSource port, wire the platform-owned outbox, lease, heartbeat, audit, idempotency and sequence instruments for the rows that genuinely have no producer (S7 and S8, never a second latency o… | — | 800 | — |
| `e14-f03-2` | 0 | A | **Serve /health/detail: the authorised detailed health report and the non-essential Degraded contract** — Map the /health/detail probe that ADR-0010, container.md line 172 and failure-modes.md all specify and nothing implements - permissioned on admin.health.read in the web host, anonymous on the worker's unpublished 8081 because that host composes no authentication at all - with … | — | 520 | — |
| `e14-f03-7` | 0 | A | **Retry-safety fault injection: the fixture, the coverage gate, and the invoice-side commands** — Tests only, about 900 test lines: a fault-injection fixture plus nine scenarios - invoice post, invoice cancel and audited outbox replay under timeout, mid-transaction failure and worker restart - each asserting exactly one business effect and one audit event, plus docs/qa/ret… | — | — | — |
| `e14-f03-8` | 0 | C | **Found tests/load: the k6 harness, the load tier in both dev scripts, and the authentication scenario** — Found tests/load with a k6 harness, a per-virtual-user keying scheme so the run measures the application and not the rate limiter, a thresholds module citing every docs/nfr row, the NFR-LT-04 authentication scenario (the one fully drivable over HTTP today), ./scripts/dev test … | — | 850 | — |
| `e14-f03-3` | 1 | C | **Redact and sample in the collector, and stand up the self-hosted observability stack** — Add the redaction, filter and tail-sampling processors the collector file's own header defers to #58, and bring up Prometheus, Loki, Tempo, Grafana and Alertmanager as the D13 compose profile with digest pins, loopback ports, container hardening and the Grafana credential as a… | `e14-f03-1` | 700 | — |
| `e14-f03-6` | 1 | A | **Bounded failure: the resilience policy catalogue, the object-storage bulkhead and breaker, and the liveness watchdog** — Write docs/architecture/resilience-policies.md classifying every operation's semantics against the timeouts, bounded retry and queue limits already built; add the missing bulkhead (2 in the worker, 6 in the web host, both sourced from capacity section 2.4), circuit breaker and… | `e14-f03-2` | 700 | — |
| `e14-f03-7b` | 1 | A | **Retry-safety fault injection: the payment-side commands and handler redelivery** — Tests only, about 900 test lines: nine scenarios over payments.record, payments.reverse and payments.session - note the payments.* namespace, not billing.* - asserting one payment row with one allocation set, one compensating record with a 409 on the second, and one closed ses… | `e14-f03-7` | — | — |
| `e14-f03-8b` | 1 | C | **Load scenarios: S1-partial counter peak and the S7 soak** — S1-partial at 20 requests per second for 15 minutes over the route mix that actually exists - Identity, Customers, Catalog reads, Billing reads and the administrative writes that need no order - with the absent legs named rather than approximated, plus S7 soaking it at 50% wit… | `e14-f03-8` | 700 | — |
| `e14-f03-4` | 2 | C | **Dashboards as code: the service-health dashboard, file provisioning and the panel lint test** — The operator's service-health dashboard - the one whose every series is already producing, so it can be fully green - plus the file-based provisioning, the panel-to-SLI README and a contract-tier lint test that resolves every metric name against SignalNames, demands a descript… | `e14-f03-1`, `e14-f03-3` | 900 | — |
| `e14-f03-5` | 2 | C | **Alert rules as code: burn-rate and latency alerts, Alertmanager, the dead-man's switch and the runbook foundation** — The four multi-window burn-rate rules of slo.md section 10.1 transcribed exactly over the S1 SLI, the secondary latency and error rules, all of Alertmanager's grouping, inhibition and severity routing with placeholder receivers under an explicit six-field OD-15 deferral rather… | `e14-f03-1`, `e14-f03-3` | 900 | — |
| `e14-f03-4b` | 3 | C | **Dashboards as code: background integrity, capacity, and the no-producer table on screen** — The two dashboards whose series come from child 1's new instruments and the capacity envelope - outbox lag per module against S7, dead letters against S8, heartbeat age at twice the configured interval for NFR-BG-08, lease and audit-chain age, saturation against capacity secti… | `e14-f03-1`, `e14-f03-3`, `e14-f03-4` | 950 | — |
| `e14-f03-5b` | 3 | C | **Alert rules as code: background work, business integrity and the absent() discipline** — Outbox lag, dead letters, job-lease age, worker heartbeat at twice the configured interval, audit chain-head age as P1 and pending migrations - each with an absent() companion so a stopped exporter fires instead of going green - plus one business-integrity rule per catalogue r… | `e14-f03-1`, `e14-f03-3`, `e14-f03-5` | 850 | — |
| `e14-f03-9` | 3 | C | **Write the incident-response procedure, the record templates and the game-day programme** — Write docs/process/incident-response.md, the incident-record and post-incident-review templates and docs/process/game-days.md with all six scenario scripts and the scoring sheet - by reference to security-operations-targets.md section 6 rather than restating a single severity … | `e14-f03-5` | 700 | — |
| `e14-f03-9b` | 4 | C | **EVIDENCE, NOT A CODING SESSION: run and score the four game days and the alert and runbook effectiveness review** — Not a coding session and titled so: it needs a running Docker daemon, a live Alertmanager with children 5 and 5b's rules loaded, and a second human observer who did not write the runbook. Run and score the four exercisable drills - database slowdown, storage outage, stuck outb… | `e14-f03-3`, `e14-f03-5`, `e14-f03-5b`, `e14-f03-9` | — | — |

### 4.21 #59 — E15-F01 environments, CI/CD, safe database releases

11 units, about 7,000 production lines, in dependency order.

| Unit | Lv | Lane | What it delivers | Depends on | Lines | Issue |
| --- | --- | --- | --- | --- | --- | --- |
| `e15-f01-1` | 0 | C | **Fix the version of a release: the version source, the compatibility matrix and the release-notes template** — One root VERSION file read into <VersionPrefix> by an MSBuild property function (not ReadLinesFromFile) so GET /api/version reports the release without a build argument, an architecture-tier parity check against clients/pwa/package.json with its two negative controls in Negati… | — | 700 | — |
| `e15-f01-5` | 0 | A | **Safe database releases: the migration preflight recovery target, and the N/N+1 coexistence and failed-migration rehearsals** — migrate --preflight recording the pending set, pg_current_wal_lsn() and pg_stat_archiver as the point-in-time-recovery target with the backup label honestly left to #60, timestamps from IClock so ARCH-014 stays green, and the proof the coexistence property currently lacks enti… | — | 700 | — |
| `e15-f01-9` | 0 | A | **Encrypt the Data Protection key ring: provision the key-encryption certificate and close RR-04** — The one obligation #59 carries in the waiver register rather than in its body: docs/process/waivers.md section 4.2 names '#59 (environments, secrets and certificate provisioning)' as the corrective issue for the ring being persisted UNENCRYPTED (RR-04, threat AS-05, gate RG-10… | — | 350 | — |
| `e15-f01-2` | 1 | C | **Build the three release images once: the CLI image, digest-pinned bases and the tag-driven build workflow** — infra/docker/Dockerfile.cli (which the staging stack has referenced by ${TAILOR360_CLI_IMAGE} since day one with nothing to build it), digest pins on the three base images and the two quay.io MinIO images with a new mechanical rule whose identifier is taken at writing time rat… | `e15-f01-1` | 850 | — |
| `e15-f01-3` | 2 | C | **Attest every release artefact: per-image bill of materials, cosign signature and build provenance** — RG-11 is S1 with no waiver for a missing signature and nothing produces one: a per-image CycloneDX document generated from the pushed digest rather than the source tree, a Trivy image scan gated through the existing scripts/sarif-gate.py at --fail-on-severity 7.0 (the shipped-… | `e15-f01-2` | 700 | — |
| `e15-f01-4` | 3 | C | **Promote only an approved digest: the machine-checked release record, the Environments and release-branch protection** — scripts/release-record.py enforcing the sixteen items of release-gates.md section 6 and the waiver-expiry read RG-14 asserts — anchored on waivers.md section 4.2's register alone, because the four 'Known gaps' rows beneath it have no expiry and would fail every release — a pro… | `e15-f01-1`, `e15-f01-3` | 800 | — |
| `e15-f01-6` | 4 | C | **Deploy, verify, halt and roll back on the host: tailor360-deploy and its runbooks** — scripts/tailor360-deploy implementing the seven numbered rules of docs/dev/staging.md section 6 that section 12 item 3 records as a hand-typed procedure: verify signature and provenance before pulling, refuse a digest outside the promotion record, record the recovery target th… | `e15-f01-3`, `e15-f01-4`, `e15-f01-5` | 750 | — |
| `e15-f01-8` | 4 | C | **Patch management and the nightly cadence: the missing schedule trigger, the weekly rebuild and the deployed-digest re-scan** — release-gates.md section 2.3 asserts a Nightly cadence and no workflow in the repository has a schedule key: a new nightly.yml delivering the one arm that can run today (RG-11 against the deployed digests read from the promotion record, verified then Trivy-scanned), the weekly… | `e15-f01-3`, `e15-f01-4` | 700 | — |
| `e15-f01-7` | 5 | C | **Provision an environment from empty: the provider-neutral Ansible baseline and the clean-environment provisioning test** — NFR-PO-03 is 'Proof scheduled (#59)' with no provisioning code at all: an Ansible playbook with a pinned version and ansible-lint that takes an empty host to the existing staging compose stack with no manual step (runtime, service account, secret file tree and modes but never … | `e15-f01-2`, `e15-f01-6` | 700 | — |
| `e15-f01-6b` | 6 | C | **Evidence: the rollback rehearsal with an N+1 client cached in a real browser (not a coding session)** — EVIDENCE ONLY, NO CODE: the one piece of #59's required verification a coding agent cannot produce, because NFR-BR-08 names a client 'still cached in a browser' and there is no Playwright suite (#52 open), no registered service worker (#51 open) and no host (staging.md item 1)… | `e15-f01-6`, `e15-f01-7` | 250 | — |
| `e15-f01-7b` | 6 | C | **Settle what the host exposes: the health-probe reconciliation, the firewall baseline and the from-outside reachability check** — docs/architecture/deployment.md section 8 rule 4 has read 'Issue #59 reconciles the two' since it was written, so decide it with the reason and make the Caddyfile and the document agree; then give rules 1 and 2 a second layer they have never had — one provider-neutral host pac… | `e15-f01-7` | 500 | — |

### 4.22 #60 — E15-F02 backups, PITR, disaster recovery, runbooks

7 units, about 6,070 production lines, in dependency order.
1 of them is evidence rather than code and needs a person, not a session.

| Unit | Lv | Lane | What it delivers | Depends on | Lines | Issue |
| --- | --- | --- | --- | --- | --- | --- |
| `e15-f02-1` | 0 | C | **Build the encrypted PostgreSQL backup chain: the pgBackRest image, the repository and the scheduler** — infra/docker/Dockerfile.postgres (postgres:16.13-alpine + pgbackrest) so archive_command can run in-image, infra/backup/pgbackrest.conf with every credential as a /run/secrets file and no retention flag, a backup-scheduler service plus a local `backup` profile — with the devel… | — | 1150 | — |
| `e15-f02-2` | 1 | C | **Lock the backup destination, version the media bucket and copy it off site** — provision-buckets.sh creating the backup bucket with versioning and object lock at creation (refusing to continue on an existing unlocked bucket), lifecycle-only retention, the monthly and audit-anchors prefixes, media versioning with non-current expiry, committed archiver/res… | `e15-f02-1` | 900 | — |
| `e15-f02-4` | 1 | A | **Report backup freshness: the WAL and base-backup health check and its metrics** — BackupFreshnessHealthCheck reading pg_stat_archiver and the scheduler's status document, registered NonEssential so a stale backup never fails readiness, BackupMonitoringOptions whose window defaults quote security-operations-targets.md line 273, a singleton IBackupSignalSourc… | `e15-f02-1` | 520 | — |
| `e15-f02-3` | 2 | C | **Automate the restore drill with invariant verification and a measured record** — docker-compose.restore.yml as an isolated project, restore-drill.sh that refuses any live target, restores to now-1h, asserts migrate --dry-run's exact "The database is up to date with this build." string (there is no --check flag), runs three invariant queries that can actual… | `e15-f02-1`, `e15-f02-2` | 850 | — |
| `e15-f02-5` | 3 | C | **Write the data-recovery runbooks: restore, point-in-time, deletion, bad migration, corruption, lost object** — Creates docs/runbooks/ — which four documents already link to and which does not exist — with the index, the shared template and six data-recovery runbooks that cite the scripts rather than restate their flags, using slo.md section 6.1's six stage names verbatim, each opening … | `e15-f02-3` | 1100 | — |
| `e15-f02-6` | 4 | C | **Write the disaster runbooks, the dependency list and the backup access review** — site-failure.md on slo.md section 6.1's six named stages with the pre-agreed-replacement-host condition left as a named empty slot under SLO-03, secret-compromise.md with a row per secret class saying what a compromise means for data already written, ransomware.md restoring on… | `e15-f02-5`, `e15-f02-2`, `e15-f02-3` | 1150 | — |
| `e15-f02-7` | 5 | C | **Run the recovery exercises and record the measured RPO, RTO and corrective actions — evidence only, needs a provisioned host and a non-author operator** — The register and record form that RG-13 and RE-12 have nothing to cite today, the release-blocker label recorded as a settings action rather than a diff, the cadence registration on the provisioned host (never a hosted runner — neither workflow has a schedule trigger), and the… | `e15-f02-3`, `e15-f02-4`, `e15-f02-5`, `e15-f02-6`, #59, #58 | 400 | — |

### 4.23 #61 — E15-F03 QA, UAT, training, pilot, go-live

13 units, about 6,810 production lines, in dependency order.

| Unit | Lv | Lane | What it delivers | Depends on | Lines | Issue |
| --- | --- | --- | --- | --- | --- | --- |
| `E15-F03-1` | 0 | C | **Write the risk-based test strategy and the legacy-import validation decision** — docs/qa/README.md and docs/qa/test-strategy.md mapping all thirteen kinds of test to the seven runnable tiers and the RG-nn gates, plus docs/qa/legacy-import-validation.md and the next free open decision (OD-25) asking whether any source data exists; the regression scope rule … | #52, #19, #22 | 650 | — |
| `E15-F03-2` | 1 | A | **Consolidate the deterministic fixture library and its catalogue** — tests/fixtures/manifest.json covering all thirteen dataset areas, a shared Tailor360.Fixtures library (no PackageReference, no ProjectReference, excluded in coverage-floors.json, registered at slnx line 92) replacing the duplicated repository-root walks, docs/qa/fixture-catalo… | `E15-F03-1`, #21, #52, #26, #32, #38, #41, #47 | 700 | — |
| `E15-F03-3` | 2 | B | **Stand up the business-scenario regression suite and automate walkthroughs 1 and 2** — The business-scenario area inside #52's Playwright suite: journey fixture, dataset helper, tag-driven pass-matrix reporter, the first CI job on ci.yml's existing triggers, and the two blouse walkthroughs (about 300 further lines of spec, excluded from the line count as tests);… | `E15-F03-1`, `E15-F03-2`, #52, #59, #34, #35, #36, #37, #42, #43, #49 | 400 | — |
| `E15-F03-4` | 3 | B | **Automate walkthroughs 3 to 6: multi-garment orders, dependencies, revisions and a blocked dispatch** — Salwar, lehenga, gown and kids walkthroughs as four specs (about 600 lines of test code, excluded): two and three garments on one order, finish_before and deliver_together dependencies, an order revision, a design revision, a hold and resume, two advances, and the dispatch ref… | `E15-F03-3`, #33, #40, #48 | 60 | — |
| `E15-F03-5` | 4 | B | **Automate the intake, production and custody exception paths** — EX-01 duplicate customer, EX-02 missing material, EX-06 late order, EX-07 unreadable label and EX-13 custody mismatch as five specs (about 600 lines of test code, excluded), each asserting detection and the recovery the catalogue requires; creates the append-only 'exceptions.m… | `E15-F03-3`, `E15-F03-4`, #26, #33, #36, #37, #39 | 70 | — |
| `E15-F03-6` | 5 | B | **Automate the money exceptions and prove blocked and allowed dispatch under every policy** — EX-08 cancellation, EX-09 refund and EX-15 expired dispatch exception as specs, plus a parameterised dispatch-policy spec asserting a blocked and an allowed case for every policy the running configuration supports under OD-04 (about 500 lines of test code, excluded); split out… | `E15-F03-3`, `E15-F03-4`, `E15-F03-5`, #42, #48, #37, #59 | 60 | — |
| `E15-F03-6b` | 6 | B | **Automate the delivery, feedback and notification exceptions and publish the regression report** — EX-11 negative feedback, EX-12 failed delivery and EX-14 notification dead-letter as specs (about 400 lines of test code, excluded), plus docs/qa/regression-report.md completed for the build — the RG-05 and RE-06 evidence — plus the four NFR-CU status cells, which only this ch… | `E15-F03-3`, `E15-F03-4`, `E15-F03-5`, `E15-F03-6`, `E15-F03-1`, #47, #48, #49, #55, #59 | 220 | — |
| `E15-F03-7` | 7 | C | **Rehearse a production-like release including restore and rollback** — docs/qa/dress-rehearsal.md as a run sheet plus the completed record of the first run: signature and provenance, clean provision, migrate, regression, backup, an induced migration failure, rollback, restore with measured RPO and RTO, regression again, and a filled release-evide… | `E15-F03-6b`, `E15-F03-1`, #59, #60, #58 | 500 | — |
| `E15-F03-8` | 7 | C | **Write the shop-floor UAT scripts and the sign-off sheet** — docs/uat/README.md, the sign-off sheet and five role scripts — Reception, Measurement Staff, Tailor Master, Tailor, Inventory Clerk — in the shape of docs/process/uat-administration.md: steps with a question for the person, refusals with their problem-details body, and an EX-n… | `E15-F03-6b`, `E15-F03-2`, #24, #25, #17, #60 | 750 | — |
| `E15-F03-8b` | 8 | C | **Write the money and oversight UAT scripts and the accountant's billing approval** — Cashier, Delivery Staff, Owner and Auditor scripts — the cashier close against INV-CSH-03, the blocked dispatch of EX-10, the step-up exception approval of EX-15, and an Auditor proved unable to change anything three ways — plus docs/uat/accountant-approval.md tracing every fi… | `E15-F03-8`, `E15-F03-2`, #43, #42, #48, #37 | 700 | — |
| `E15-F03-9` | 9 | C | **Write the training material, quick cards, support contacts and production-access onboarding** — docs/training/ with nine role quick cards capped at 50 lines each and generated from the 'What changes for staff' tables, the printed first-run card and training label sheet, support contacts against OD-15, controlled production-access onboarding, the retained training environ… | `E15-F03-8`, `E15-F03-8b`, #17, #23, #25, #57, #59, #60 | 1000 | — |
| `E15-F03-10` | 10 | C | **Plan the pilot: entry and exit criteria, daily reconciliation and defect triage** — docs/launch/ pilot plan with a device, printer and scanner inventory from the support matrix, artefact-resolving entry criteria, measurable exit criteria that quote the plan's 'zero P1 defects' wording and name the S1 reading they adopt, the daily reconciliation procedure per … | `E15-F03-7`, `E15-F03-8`, `E15-F03-8b`, `E15-F03-9`, `E15-F03-6b`, #35, #36, #37, #48, #58, #59, #60 | 800 | — |
| `E15-F03-11` | 11 | C | **Record the go/no-go decision, hypercare, the post-launch review and the release evidence index** — docs/launch/go-no-go.md with a row per RG-nn and a named holder for each of the four owner roles the plan requires — business, technical, security, operations — plus a distinct rollback decision owner; hypercare with its rota and exit criteria; the post-launch review schedule … | `E15-F03-10`, `E15-F03-9`, `E15-F03-8`, `E15-F03-8b`, `E15-F03-7`, `E15-F03-6b`, #56, #58, #59, #60 | 900 | — |

### 4.24 #216 — E09-F03-5a billing client screens and the balances endpoint

3 units, about 1,770 production lines, in dependency order.

| Unit | Lv | Lane | What it delivers | Depends on | Lines | Issue |
| --- | --- | --- | --- | --- | --- | --- |
| `E09-F03-5b` | 0 | A | **Serve the branch's outstanding balances from one read, and page the screen through it** — Replace the client-side 1+n fan-out behind the outstanding-balances screen with one branch-scoped Billing read endpoint that fills each page by cursor under a declared scan bound, and wire the Show more control whose message key already exists. | #162, #165 | 420 | — |
| `E09-F03-5c` | 0 | B | **Storybook state and pseudo-locale stories for the four counter billing screens** — Build the billing story harness (permission override evaluated per request, forced connection) and the five-state, pseudo-locale and 320 px reflow-floor stories for take payment, allocate advance, payment detail and the cashier session, with the axe assertions in Vitest where … | #165 | 830 | — |
| `E09-F03-5d` | 1 | B | **Storybook state stories for the three office billing screens, and the forced-state map for the payment journey** — Five-state, pseudo-locale and reflow-floor stories for outstanding balances, reconciliation approval and dispatch-exception approval, plus the story-id-per-state map for all seven billing screens so the human screen-reader run of a11y-checklist.md section 8 is a walkthrough ra… | `E09-F03-5b`, `E09-F03-5c`, #165 | 520 | — |

---

## 5. The schedule

### 5.1 Levels

A unit's level is its depth in the dependency graph: level 0 depends on nothing that is still unbuilt, level *n* waits
on something at level *n*−1. Computed over all 256 units plus the open issues they name, the graph is **acyclic**, and
every dependency token resolves to a real issue or a real sibling — there are no dangling references left.

| Level | 0 | 1 | 2 | 3 | 4 | 5 | 6 | 7 | 8 | 9 | 10 | 11 |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| Units | 46 | 50 | 51 | 38 | 22 | 19 | 11 | 7 | 5 | 3 | 3 | 1 |

The shape matters more than the depth. A wide base (46 units at level 0, 50 at level 1, 51 at level 2) is what makes
concurrent sessions possible at all; the long thin tail from level 6 upwards is the release chain, which is serial by
nature because each step certifies the one before it.

### 5.2 Startable against `main` today

25 units have no unmerged dependency of any kind. They are where work begins.

| Unit | Parent | Lane | Branch | Goal |
| --- | --- | --- | --- | --- |
| `e06-f02-1` | #33 | A | `feat/e06-f02-1-workflow-definition-model` | Create workflow_definitions, workflow_versions and workflow_version_phases in the orders schema with one expand-only migration 33_workflow_definitions, the draft/published/retired  |
| `e06-f03-1a` | #34 | A | `feat/e06-f03-1a-qc-checklist-schema` | All four catalog QC tables (qc_checklist_templates, qc_checklist_versions, qc_criteria, defect_codes) in ONE expand-only migration with both published-version immutability triggers |
| `e07-f01-5` | #35 | A | `feat/e07-f01-5-durable-print-queue` | Create platform.print_jobs with one expand-only migration, replace LoggingPrintQueue with a durable DatabasePrintQueue that publishes platform.print-job-queued.v1 in the same save, |
| `E09-F02-4` | #42 | B | `feat/e09-f02-4-invoice-register-and-detail` | Open the invoice read surface against the three published read routes: the branch's register with its status filter and cursor paging, the detail screen with its lines, tax compone |
| `E09-F02-8` | #42 | A | `feat/e09-f02-8-billing-timeline-source` | Implement the ITimelineSource the #42 blueprint names and no merged child built, reading Billing's own three tables through the platform port, filtering on permissions and assigned |
| `E09-F02-9` | #42 | C | `chore/e09-f02-9-document-figure-assertions` | Add the test-only PDF text-extraction capability and prove, without a database, that every figure printed on the page equals the figure it was given — all 17 golden-master cases at |
| `E09-F03-5b` | #216 | A | `feat/e09-f03-5b-outstanding-balances-read` | Replace the client-side 1+n fan-out behind the outstanding-balances screen with one branch-scoped Billing read endpoint that fills each page by cursor under a declared scan bound,  |
| `E09-F03-5c` | #216 | B | `feat/e09-f03-5c-billing-counter-screen-stories` | Build the billing story harness (permission override evaluated per request, forced connection) and the five-state, pseudo-locale and 320 px reflow-floor stories for take payment, a |
| `e10-f02-7a` | #45 | B | `feat/e10-f02-7a-client-reporting-area-and-hub` | The client reporting area in the shape of clients/pwa/src/billing/, the unowned-today status strip, and the /reports hub that roleNavigation.ts line 81 has pointed at since before  |
| `e12-f01-1` | #50 | A | `feat/e12-f01-1-account-display-preferences-api` | Add InterfaceTextSize and TextSize to UserPreferences with one expand-only migration (AddUserPreferenceTextSize), StaffUser.EnsurePreferences, a PreferencesHandler and a full-repla |
| `e12-f01-3` | #50 | B | `feat/e12-f01-3-session-driven-shell-navigation` | Add shellPermissions.ts quoting twelve real keys from Platform.Security/Permissions, give every destination the permission its target route already declares plus a route-registered |
| `e12-f01-5` | #50 | C | `docs/e12-f01-5-reference-journey-device-and-screen-reader-evidence` | Create docs/nfr/a11y-records/ under the A11Y-OD-06 default path and commit five completed records for the A11Y-RJ journeys that have records of their own (RJ-02, -03, -04, -05, -08 |
| `e12-f02-1` | #51 | B | `feat/e12-f02-1-service-worker-caching` | Switch vite-plugin-pwa to injectManifest, write a source service worker whose routing defaults to network-only, precache the hashed build, serve the shell from cache only when the  |
| `e12-f03-4` | #52 | A | `feat/e12-f03-4-client-telemetry-endpoint` | The anonymous, rate-limited ingest endpoint in the web host under Telemetry/, with an allowlist rather than a denylist, the telemetry-ingest policy the catalogue names but does not |
| `E14-F01a-1` | #56 | C | `docs/e14-f01a-1-abuse-cases` | The eleven ABF-01..ABF-11 abuse-case families with their control patterns, what defeats each today, and the per-flow coverage map; plus the family column in the threat-model templa |
| `E14-F01a-2` | #56 | C | `docs/e14-f01a-2-asvs-traceability` | docs/security/asvs-traceability.md: one row per Level 2 requirement with level, flow model, control owner, a four-value verdict and covering issue, the chapter-to-NFR-to-gate cross |
| `E14-F01a-7` | #56 | C | `feat/e14-f01a-7-vulnerability-management-register` | vulnerability-management.md, SECURITY.md and exceptions.md, plus a kind discriminator on .github/sarif-accepted.json so a false positive keeps no expiry while an accepted risk carr |
| `e14-f03-1` | #58 | A | `feat/e14-f03-1-signal-catalogue-instruments` | Move Tailor360Diagnostics into Abstractions, declare SignalNames and the IIntegritySignalSource port, wire the platform-owned outbox, lease, heartbeat, audit, idempotency and seque |
| `e14-f03-2` | #58 | A | `feat/e14-f03-2-health-detail-endpoint` | Map the /health/detail probe that ADR-0010, container.md line 172 and failure-modes.md all specify and nothing implements - permissioned on admin.health.read in the web host, anony |
| `e14-f03-7` | #58 | A | `feat/e14-f03-7-retry-safety-fault-injection` | Tests only, about 900 test lines: a fault-injection fixture plus nine scenarios - invoice post, invoice cancel and audited outbox replay under timeout, mid-transaction failure and  |
| `e14-f03-8` | #58 | C | `feat/e14-f03-8-k6-load-harness` | Found tests/load with a k6 harness, a per-virtual-user keying scheme so the run measures the application and not the rate limiter, a thresholds module citing every docs/nfr row, th |
| `e15-f01-1` | #59 | C | `feat/e15-f01-1-release-versioning` | One root VERSION file read into <VersionPrefix> by an MSBuild property function (not ReadLinesFromFile) so GET /api/version reports the release without a build argument, an archite |
| `e15-f01-5` | #59 | A | `feat/e15-f01-5-migration-preflight-rehearsal` | migrate --preflight recording the pending set, pg_current_wal_lsn() and pg_stat_archiver as the point-in-time-recovery target with the backup label honestly left to #60, timestamps |
| `e15-f01-9` | #59 | A | `feat/e15-f01-9-key-ring-encryption` | The one obligation #59 carries in the waiver register rather than in its body: docs/process/waivers.md section 4.2 names '#59 (environments, secrets and certificate provisioning)'  |
| `e15-f02-1` | #60 | C | `feat/e15-f02-1-postgres-backup-chain` | infra/docker/Dockerfile.postgres (postgres:16.13-alpine + pgbackrest) so archive_command can run in-image, infra/backup/pgbackrest.conf with every credential as a /run/secrets file |

### 5.3 How many sessions can really run at once

**Five**, not eleven. The level widths say eleven units could proceed in parallel; the contended files in
[section 6](#6-contended-files--the-register-that-replaces-pairwise-notes) say otherwise. Of the 25 startable units,
most must regenerate `docs/api/openapi.v1.json` and `clients/pwa/src/api/schema.d.ts`, and the same ones add rows to
`docs/security/permission-matrix.md` and `tests/Tailor360.IntegrationTests/Authorization/matrix.yaml`. Both matrix
files are parsed by tests, so a careless merge of them is **green and wrong** — which is the failure mode worth
designing against, because nothing catches it.

The practical rule: at most one session per slot may hold each of the four serialisers, and a session that needs one
rebases immediately before it pushes. Two backend sessions founding different module schemas, one client session in a
different route group, one documentation or governance session and one evidence session is a realistic five.

### 5.4 The critical path

The longest chain is 12 links, and it runs through custody and the release gates rather than through anything a
customer sees: orders must exist before barcodes are allocated, barcodes before labels, labels before the scanning
experience, scanning before the custody chain, custody before the delivery gate, the delivery gate before the
installable client, the client before the Playwright suite, and the Playwright suite before every QA, UAT and go-live
unit. Two points on it deserve attention before anything else is scheduled:

- **The Playwright suite gates thirteen units.** `tests/e2e` and `playwright.config` do not exist; `scripts/dev` says
  so itself. Until the founding units of #52 land, thirteen E15-F03 units cannot start, and the automated
  reflow and obscured-focus gate that several client units name as their NFR-AC-04 proof cannot run at all. It is
  the highest-leverage unit in the whole backlog.
- **#214 blocks every session's evidence step.** `Tailor360.Cli` cannot compose its container when
  `DOTNET_ENVIRONMENT=Development`, which is `scripts/dev`'s own default, so `migrate`, `init-reference-data` and
  `seed-synthetic` all fail. Almost every backend unit's evidence list asks for migration output. It is a small fix
  and it is in nobody's dependency list.

---

## 6. Contended files — the register that replaces pairwise notes

A unit's issue body lists the shared files it must touch. This register is the other half: for each such file,
which units touch it and in what order they may safely land. It exists because the contention is **not**
pairwise — 93 units regenerate `clients/pwa/src/api/schema.d.ts`, and that fact cannot be written into 93 issue
bodies as 92 cross-references without rotting on the first change. One table, one place to correct it.

**How to use it.** Before cutting a branch, look up every file your issue names. If a unit earlier in the merge
order holds it and has not landed, either take that unit first or accept that you will rebase. Immediately before
you push, rebase on `main` and re-run the check that parses the file.

### 6.1 The serialisers — 17 files too widely shared to list by unit

For these, naming the units would be a list of almost every unit in the backlog, which is not a usable
instruction. The rule is the instruction.

| File | Units | The rule |
| --- | --- | --- |
| `clients/pwa/src/api/schema.d.ts` | 93 | Every unit that changes the API surface regenerates it. **Rebase, then run `pnpm --dir clients/pwa generate:api`** — never the other way round, or `generate:api:check` fails on drift that is not yours. |
| `docs/api/openapi.v1.json` | 63 | Every unit that adds or changes an endpoint. Regenerated, never hand-edited. Paste the diff in the pull request. |
| `docs/security/permission-matrix.md` | 57 | Every unit that adds or changes an endpoint or a permission. **Machine-parsed**: append inside your own generated block only. |
| `tests/Tailor360.IntegrationTests/Authorization/matrix.yaml` | 54 | Every unit that adds or changes an endpoint. **Machine-parsed**: a careless merge here is green and wrong. Add only your own rows. |
| `clients/pwa/src/app/router.tsx` | 44 | Every unit that adds a route group. One session at a time; the conflict is trivial to resolve and easy to resolve wrongly by dropping a sibling route. |
| `docs/dev/migrations.md` | 31 | Every unit that adds a migration appends its own row to the log. Append-only; never reorder. |
| `clients/pwa/src/i18n/messages/index.ts` | 26 | Every unit that adds a message family registers it here. Append only. |
| `docs/nfr/traceability.md` | 18 | Each unit owns only the status cells its body names. **Never edit another unit’s cell.** |
| `clients/pwa/src/api/contract.ts` | 16 | Every unit that pins a generated type. Append only. |
| `docs/integration/events/README.md` | 15 | Every unit that publishes an integration event adds its row. Append only. |
| `docs/reports/metrics.md` | 12 | One session at a time. Rebase immediately before pushing and re-read the file rather than reapplying your diff. |
| `src/Modules/Orders/Tailor360.Modules.Orders.Infrastructure/OrdersModuleServiceCollectionExtensions.cs` | 12 | One session at a time. Rebase immediately before pushing and re-read the file rather than reapplying your diff. |
| `clients/pwa/src/i18n/messages/inventory.ts` | 11 | One session at a time. Rebase immediately before pushing and re-read the file rather than reapplying your diff. |
| `clients/pwa/src/i18n/en-IN.ts` | 10 | One session at a time. Rebase immediately before pushing and re-read the file rather than reapplying your diff. |
| `clients/pwa/src/i18n/ta-IN.ts` | 10 | One session at a time. Rebase immediately before pushing and re-read the file rather than reapplying your diff. |
| `src/Modules/Identity/Tailor360.Modules.Identity.Application/Access/SystemRoles.cs` | 9 | One session at a time. Rebase immediately before pushing and re-read the file rather than reapplying your diff. |
| `src/Modules/Reporting/Tailor360.Modules.Reporting.Infrastructure/ReportingModuleServiceCollectionExtensions.cs` | 9 | One session at a time. Rebase immediately before pushing and re-read the file rather than reapplying your diff. |

### 6.2 Files shared by two to eight units, in merge order

Here a list is short enough to act on. **Merge order** is dependency depth, so a unit never waits on one that
depends on it; units at the same depth may land in either order.

| File | Units, in merge order |
| --- | --- |
| `docs/prd/assumptions-and-open-decisions.md` | `e08-f03-1`, `e14-f03-6`, `e08-f03-5`, `e11-f02-3a`, `e11-f02-3b`, `e06-f03-6`, `e08-f03-13`, `e10-f03-8` |
| `docs/integration/events/` | `e08-f02-2`, `e08-f02-5`, `e08-f03-2`, `e08-f02-3`, `e08-f03-3`, `e08-f03-5`, `e10-f02-6b` |
| `docs/nfr/a11y-checklist.md` | `e12-f03-8`, `e08-f02-8`, `e08-f02-10`, `e08-f02-9`, `e07-f02-6`, `e08-f02-10b`, `e06-f02-14` |
| `docs/security/field-visibility.md` | `e11-f03-3`, `e14-f02-3`, `e10-f02-3b`, `e10-f03-3`, `e10-f03-6`, `e06-f02-8`, `e10-f03-2b` |
| `src/Modules/Notifications/Tailor360.Modules.Notifications.Infrastructure/NotificationsModuleServiceCollectionExtensions.cs` | `e11-f03-1`, `e11-f03-3`, `e11-f03-5`, `e11-f03-3b`, `e11-f03-4`, `e11-f02-6`, `e11-f02-6b` |
| `src/Modules/Orders/Tailor360.Modules.Orders.Api/OrdersEndpoints.cs` | `e06-f03-3`, `e06-f02-2`, `e06-f03-4a`, `e06-f03-4b`, `e06-f03-2a`, `e06-f03-2b`, `e06-f03-6` |
| `InventoryDbContext and the Inventory migration chain` | `e08-f03-1`, `e08-f03-4`, `e08-f03-2`, `e08-f03-3`, `e08-f03-5`, `e08-f03-7` |
| `ReportingModuleServiceCollectionExtensions.cs` | `e10-f03-4`, `e10-f03-2`, `e10-f03-3`, `e10-f03-5`, `e10-f03-2b`, `e10-f03-8` |
| `clients/pwa/package.json` | `e15-f01-1`, `e07-f02-2`, `e12-f02-3a`, `e12-f03-2`, `e12-f03-6`, `e12-f03-5` |
| `clients/pwa/src/routes/inventory/inventory.css` | `e08-f03-9`, `e08-f03-10`, `e08-f03-11`, `e08-f03-12`, `e08-f03-8`, `e08-f03-11b` |
| `src/Hosts/Tailor360.Worker/Program.cs` | `e14-f03-2`, `e08-f03-2`, `e11-f03-5`, `e14-f03-6`, `e10-f02-6b`, `e06-f02-9` |
| `src/Modules/Custody/Tailor360.Modules.Custody.Infrastructure/CustodyModuleServiceCollectionExtensions.cs` | `e07-f03-1`, `e11-f02-1`, `e11-f02-2`, `e11-f02-3a`, `e11-f02-3b`, `e11-f02-4` |
| `src/Modules/Inventory/Tailor360.Modules.Inventory.Api/InventoryEndpoints.cs` | `e08-f02-4`, `e08-f02-2`, `e08-f02-5`, `e08-f02-3`, `e08-f02-6`, `e08-f02-7` |
| `src/Modules/Orders/Tailor360.Modules.Orders.Infrastructure/Persistence/OrdersDbContext.cs` | `e06-f02-1`, `e06-f02-4`, `e14-f02-9`, `e06-f02-6`, `e06-f02-8`, `e06-f02-9` |
| `.github/workflows/ci.yml` | `e14-f03-8`, `e12-f03-1b`, `e14-f03-8b`, `e12-f03-2`, `e12-f03-6` |
| `InventoryResourceKinds.cs` | `e08-f02-4`, `e08-f02-2`, `e08-f02-5`, `e08-f02-3`, `e08-f02-6` |
| `clients/pwa/src/i18n/messages/offline.ts` | `e12-f02-3a`, `e12-f02-3b`, `e12-f02-4`, `e12-f02-5a`, `e12-f02-5b` |
| `clients/pwa/src/inventory/inventoryApi.ts` | `e08-f03-9`, `e08-f03-10`, `e08-f03-11`, `e08-f03-8`, `e08-f03-11b` |
| `clients/pwa/src/inventory/types.ts` | `e08-f03-9`, `e08-f03-10`, `e08-f03-11`, `e08-f03-8`, `e08-f03-11b` |
| `clients/pwa/src/routes/inventory/` | `e08-f02-8`, `e08-f02-10`, `e08-f02-9`, `e08-f02-10b`, `e08-f02-9b` |
| `docs/architecture/module-ownership.md` | `e14-f02-1`, `e14-f02-3`, `e06-f03-1c`, `e06-f03-4b`, `e06-f02-8` |
| `docs/dev/migrations.md register table` | `e10-f02-1`, `e10-f02-3a`, `e10-f02-4a`, `e10-f02-5`, `e10-f02-6c` |
| `docs/platform/backups.md` | `e15-f02-1`, `e15-f02-2`, `e15-f02-4`, `e15-f02-3`, `e15-f02-5` |
| `src/Modules/Orders/Tailor360.Modules.Orders.Infrastructure/Migrations (the Orders migration chain)` | `e06-f02-1`, `e06-f02-4`, `e06-f02-6`, `e06-f02-8`, `e06-f02-9` |
| `src/Modules/Reporting/.../Persistence/ReportingDbContext.cs` | `e10-f03-1`, `e10-f03-4`, `e10-f03-2`, `e10-f03-5`, `e10-f03-2b` |
| `src/Tools/Tailor360.Cli/Commands/InitReferenceDataCommand.cs` | `e14-f02-1`, `e06-f02-3`, `e11-f02-4`, `e11-f02-6`, `e11-f02-6b` |
| `the Reporting migration chain` | `e10-f03-1`, `e10-f03-4`, `e10-f03-2`, `e10-f03-5`, `e10-f03-2b` |
| `CustodyModuleServiceCollectionExtensions.cs` | `e07-f03-2`, `e07-f03-3`, `e07-f03-3b`, `e07-f03-4` |
| `clients/pwa/pnpm-lock.yaml` | `e07-f02-2`, `e12-f03-2`, `e12-f03-6`, `e12-f03-5` |
| `clients/pwa/src/admin/adminApi.ts` | `e14-f02-5`, `e14-f02-7`, `e14-f02-8`, `e14-f02-8b` |
| `clients/pwa/src/admin/adminPermissions.ts` | `e14-f02-7`, `e06-f02-12`, `e14-f02-8`, `e14-f02-8b` |
| `clients/pwa/src/components/layout/AppShell.tsx` | `e12-f01-3`, `e12-f02-2`, `e12-f02-5a`, `e12-f02-5b` |
| `clients/pwa/src/i18n/messages/reporting.ts` | `e10-f02-7b`, `e10-f02-8a`, `e10-f02-8b`, `e10-f02-9` |
| `clients/pwa/src/i18n/messages/scanning.ts` | `e07-f02-3`, `e07-f02-3b`, `e07-f02-4`, `e07-f02-5` |
| `clients/pwa/src/inventory/inventoryPermissions.ts` | `e08-f03-9`, `e08-f03-10`, `e08-f03-11`, `e08-f03-11b` |
| `clients/pwa/src/inventory/stockApi.ts` | `e08-f02-10`, `e08-f02-9`, `e08-f02-10b`, `e08-f02-9b` |
| `clients/pwa/src/reporting/*` | `e10-f02-7b`, `e10-f02-8a`, `e10-f02-8b`, `e10-f02-9` |
| `clients/pwa/src/routes/admin/AdminScreens.test.tsx` | `e14-f02-5`, `e14-f02-7`, `e14-f02-8`, `e14-f02-8b` |
| `clients/pwa/src/routes/reporting/*` | `e10-f02-7b`, `e10-f02-8a`, `e10-f02-8b`, `e10-f02-9` |
| `clients/pwa/src/routes/scanning/scanningScreens.stories.tsx` | `e07-f02-3`, `e07-f02-3b`, `e07-f02-4`, `e07-f02-5` |
| `docs/nfr/a11y-checklist.md section 6.8` | `e10-f02-7b`, `e10-f02-8a`, `e10-f02-8b`, `e10-f02-9` |
| `docs/nfr/performance-report.md` | `e14-f03-8`, `e14-f03-8b`, `e12-f03-7`, `e12-f03-8` |
| `docs/nfr/support-matrix.md` | `e12-f03-3`, `e12-f03-8`, `e12-f02-6a`, `e07-f02-6` |
| `infra/compose/.env.example` | `e15-f02-1`, `e15-f02-2`, `e15-f02-4`, `e15-f02-3` |
| `infra/compose/docker-compose.staging.yml` | `e15-f02-1`, `e14-f03-3`, `e15-f02-2`, `e15-f02-4` |
| `src/Modules/Inventory/Tailor360.Modules.Inventory.Infrastructure/InventoryModuleServiceCollectionExtensions.cs` | `e08-f02-1`, `e08-f02-2`, `e08-f02-3`, `e08-f02-7` |
| `src/Modules/Inventory/Tailor360.Modules.Inventory.Infrastructure/Persistence/InventoryDbContext.cs` | `e08-f02-1`, `e08-f02-4`, `e08-f02-2`, `e08-f02-5` |
| `src/Modules/Orders/Tailor360.Modules.Orders.Infrastructure/Persistence/OrdersDbContext.cs and the Orders migration chain` | `e06-f03-3`, `e06-f03-4a`, `e06-f03-2a`, `e06-f03-2b` |
| `src/Modules/Reporting/Tailor360.Modules.Reporting.Api/ReportingEndpoints.cs` | `e10-f02-2`, `e10-f02-5`, `e10-f02-3b`, `e10-f02-4b` |
| `src/Platform/Tailor360.Platform.Persistence/Contexts/PlatformDbContext.cs and src/Platform/Tailor360.Platform.Persistence/Migrations/` | `e14-f02-1`, `e14-f02-5`, `e14-f02-2`, `e14-f02-4` |
| `src/Tools/Tailor360.Cli/CliHost.cs` | `e12-f03-1a`, `e11-f02-1`, `e11-f02-4`, `e11-f02-6` |
| `tests/Tailor360.IntegrationTests/Inventory/InventoryMigrationTests.cs` | `e08-f03-4`, `e08-f03-2`, `e08-f03-5`, `e08-f03-7` |
| `the ## Register table of docs/dev/migrations.md` | `e10-f03-1`, `e10-f03-2`, `e10-f03-5`, `e10-f03-2b` |
| `the Inventory migration chain` | `e08-f02-1`, `e08-f02-4`, `e08-f02-2`, `e08-f02-5` |
| `ReportingDbContext migration chain and ReportingDbContextModelSnapshot.cs` | `e10-f02-3a`, `e10-f02-4a`, `e10-f02-5` |
| `clients/pwa/src/admin/adminDestinations.ts` | `e14-f02-7`, `e06-f02-12`, `e14-f02-8b` |
| `clients/pwa/src/admin/types.ts` | `e14-f02-7`, `e14-f02-8`, `e14-f02-8b` |
| `clients/pwa/src/auth/SessionProvider.tsx` | `e12-f02-1`, `e12-f02-3a`, `e12-f01-2b` |
| `clients/pwa/src/i18n/messages/index.ts and the reporting message family` | `e10-f03-9b`, `e10-f03-10a`, `e10-f03-2b` |
| `clients/pwa/src/main.tsx` | `e12-f02-1`, `e12-f03-3`, `e12-f03-5` |
| `clients/pwa/src/production/productionApi.ts` | `e06-f02-11`, `e06-f02-11b`, `e06-f02-13` |
| `clients/pwa/src/reporting/` | `e10-f03-9b`, `e10-f03-10a`, `e10-f03-10b` |
| `clients/pwa/src/routes/admin/adminScreens.stories.tsx` | `e14-f02-7`, `e14-f02-8`, `e14-f02-8b` |
| `clients/pwa/src/routes/scanning/ScanRoute.tsx` | `e07-f02-3b`, `e07-f02-4`, `e07-f02-5` |
| `clients/pwa/vite.config.ts` | `e12-f02-1`, `e07-f02-2`, `e12-f03-7` |
| `docs/architecture/sequences/stock-reservation.md` | `e08-f02-1`, `e08-f02-5`, `e08-f02-6` |
| `docs/dev/commands.md` | `e12-f03-1a`, `e14-f03-8`, `e12-f03-1b` |
| `docs/nfr/data-classification.md` | `e12-f03-4`, `e14-f02-9`, `e14-f02-4` |
| `infra/README.md` | `e15-f02-1`, `e15-f02-2`, `e15-f02-3` |
| `infra/compose/docker-compose.yml` | `e15-f02-1`, `e14-f03-3`, `e15-f02-2` |
| `src/Hosts/Tailor360.Web/Program.cs` | `e12-f03-4`, `e14-f03-2`, `e14-f03-6` |
| `src/Hosts/Tailor360.Worker job registration` | `e10-f03-1`, `e10-f03-2`, `e10-f03-5` |
| `src/Modules/Catalog/Tailor360.Modules.Catalog.Api/CatalogEndpoints.cs` | `e06-f03-1a`, `e06-f03-1b`, `e06-f03-1c` |
| `src/Modules/Identity/Tailor360.Modules.Identity.Api/IdentityEndpoints.cs` | `e14-f02-1`, `e14-f02-2`, `e14-f02-4` |
| `src/Modules/Identity/Tailor360.Modules.Identity.Api/IdentityRoutes.cs` | `e14-f02-1`, `e14-f02-2`, `e14-f02-4` |
| `src/Modules/Notifications/Tailor360.Modules.Notifications.Api/NotificationsEndpoints.cs` | `e11-f03-3`, `e11-f03-3b`, `e11-f03-4` |
| `CustodyResourceKinds` | `e07-f03-3`, `e07-f03-5` |
| `Directory.Packages.props` | `e14-f03-1`, `e14-f03-6` |
| `clients/pwa/src/auth/apiClient.ts` | `e12-f02-2`, `e07-f02-5` |
| `clients/pwa/src/components/layout/roleNavigation.ts` | `e11-f03-6`, `e06-f03-8a` |
| `clients/pwa/src/i18n/messages/navigation.ts` | `e12-f01-3`, `e07-f03-8` |
| `clients/pwa/src/i18n/messages/orders.ts` | `e06-f03-8a`, `e06-f03-8b` |
| `clients/pwa/src/offline/onSignOut.ts` | `e12-f02-3a`, `e12-f02-4` |
| `clients/pwa/src/routes/admin/TemplateValidationReport.tsx` | `e06-f02-12`, `e06-f02-12b` |
| `clients/pwa/src/routes/measurements/measurementScreens.stories.tsx` | `e12-f02-1`, `e12-f02-3b` |
| `docs/architecture/architecture-rules.md` | `e12-f03-4`, `e14-f03-2` |
| `docs/nfr/capacity-and-performance.md` | `e14-f03-8`, `e12-f03-6` |
| `docs/runbooks/README.md` | `e14-f03-5`, `e14-f03-5b` |
| `scripts/dev` | `e14-f03-8`, `e14-f03-3` |
| `scripts/dev.ps1` | `e14-f03-8`, `e14-f03-3` |
| `src/Modules/Catalog/Tailor360.Modules.Catalog.Infrastructure/CatalogModuleServiceCollectionExtensions.cs` | `e06-f03-1a`, `e06-f03-1c` |
| `src/Platform/Tailor360.Platform.Security/FieldVisibility/ (including ApplicationResponseViews.cs)` | `e10-f03-6`, `e10-f03-2b` |
| `src/Platform/Tailor360.Platform.Security/FieldVisibility/OrdersResponseViews.cs` | `e10-f02-3b`, `e06-f02-8` |
| `src/Platform/Tailor360.Platform.Security/Permissions/InventoryPermissions.cs` | `e08-f02-4`, `e08-f03-3` |
| `src/Platform/Tailor360.Platform.Security/Permissions/ReportingPermissions.cs` | `e10-f03-4`, `e10-f02-3b` |
| `tests/Tailor360.ArchitectureTests/BackupConfigurationTests.cs` | `e15-f02-1`, `e15-f02-2` |
| `tests/Tailor360.ArchitectureTests/NegativeControlTests.cs` | `e15-f01-1`, `e15-f02-1` |
| `tests/Tailor360.ContractTests/ResponseViewPayloadTests.cs` | `e10-f02-3b`, `e06-f02-8` |
| `tests/Tailor360.IntegrationTests/Billing (also touched by batch 1's E14-F01b-3)` | `e14-f03-7`, `e14-f03-7b` |

---

## 7. What the crosscheck found

### 7.1 Re-scopes needed before those branches are cut

Four places were found where two units would have written the same deliverable. Three resolved themselves, because
each decomposition pass was given the previous pass's index and could see the other claim; one needed correcting and
has been corrected. They are recorded because the *resolutions* are now load-bearing.

| Collision | Resolution |
| --- | --- |
| `e07-f01-5` and open issue **#205** both delete `LoggingPrintQueue` | `e07-f01-5` owns the deletion and corrects ADR-0014 in the same change. It does **not** edit #205, which is the owner's to re-scope: two of #205's stated premises are false against `main` — there is no print-station screen, and `platform.print_jobs` does not exist. **An owner edit to #205 is outstanding.** |
| `E09-F03-5b`, `-5c`, `-5d` are the exact union of open issue **#216** | They are filed as children of #216, not of #43 — #43 closed on 2026-09-13 with all six of its own children. #216 becomes the umbrella rather than a duplicate implementation issue |
| `e07-f01-5b` widens CSP `frame-src` to `'self'` while `E14-F01b-1` adds Trusted Types to the same `StaticDirectives` constant | Sequenced, not contradictory: `e07-f01-5b` is level 1 and `E14-F01b-1` level 2, so the first lands and the second extends the same `ContentSecurityPolicyTests` theory. No edit needed |
| `e07-f01-7` added `apiBlob` while `E09-F02-5` added `apiRequestBlob` — one job, two names, in two different waves | **Corrected in five bodies.** The helper is `apiRequestBlob(path, options)`, matching the existing `apiRequest` / `apiRequestVersioned` convention, returning `{ blob, contentType, fileName }`. Because the four units that need it sit in different waves, the instruction is deliberately order-independent and declares **no dependency edge**: whichever branch merges first adds it, every later one reuses it and adds nothing |

### 7.2 What still has no owner

The second pass absorbed nearly everything the first pass found orphaned: `tests/e2e` and the Playwright harness are
now `e12-f03-1b`, `tests/load` is `e14-f03-8`, the SLO dashboards and burn-rate alert rules are `e14-f03-1`..`-5b`,
the single outbound `HttpClient` adapter ADR-0012 specifies is `e14-f03-6`, and the Nightly trigger that
[`release-gates.md`](release-gates.md) asserts but `ci.yml` has no schedule for is `e15-f01-8`. Three gaps remain.

| Gap | Why it matters |
| --- | --- |
| **`docs/security/threat-models/authorisation.md` has no owner** | [`../security/README.md`](../security/README.md) section 3.2 assigns it to `#32a`, which is a plan identifier and not a GitHub issue; on GitHub the Orders backend is #199 plus #201 and neither claims it. Every E14 unit excludes it as "#32a's". **DOR-05 is therefore unsatisfiable for the W3 issues that cover the permission and branch-scope pipeline.** `e14-f01b-2` has been corrected to record it as a gap rather than wait on it, but the model still needs an issue |
| **#182 and #204 are each more than one session** | #182 is the whole `/customers` route group — search and find-or-create, detail with timeline tabs, edit with optimistic concurrency, duplicate review with step-up merge, consent — for a client that has no `customers` directory at all. #204 is intake, estimate, job card, confirmation and revision for a module with zero client routes. Seven and ten planned units respectively depend on them. They should be split before they are started, by the session that picks them up, under rule 8 of [section 2](#2-how-a-session-picks-up-work) |
| **A consolidated synthetic dataset** | `e12-f03-1a` adds staff accounts and authenticator enrolments, and `e15-f03-2` catalogues what exists and asserts no drift, but neither teaches `seed-synthetic` to write a shop with orders, invoices, stock and custody. Several walkthroughs need one |

### 7.3 Issues needing a disposition rather than a session

| Issue | Disposition |
| --- | --- |
| #91 | Work merged (`CatalogReconciler.cs` and `CatalogReconciliationHandlers.cs`, commit `0342dc8`). Verify and close |
| #172 | Work merged (migration `20260912180307_ReconciliationBatches`, `ReconciliationHandler.cs`, `ReconciliationApprovalRoute.tsx`, commit `44e9153`). Verify and close |
| #220 | Both Owner read gaps it names are already fixed on `main` by `ee831c4`, which added the read routes, the permission gates, `RequireStepUp()`, the integration tests and the client wiring. Verify and close |
| #214 | **Start it first.** It is small, it is nobody's dependency, and it blocks `migrate`, `init-reference-data` and `seed-synthetic` in `scripts/dev`'s own default environment — which is where almost every other unit's evidence comes from |
| #17, #18, #19, #21, #22, #24, #25, #27, #29 | Deliverables are on `main`. Each needs verifying and closing, not working. Leaving them open makes their epics look unfinished and hides what is genuinely left |

### 7.4 Decisions the owner must make before a session starts

| Decision | Blocks |
| --- | --- |
| **#205's scope**, after `e07-f01-5` replaces `LoggingPrintQueue` with a durable queue | #205 as written describes behaviour that will not exist |
| **Who owns `threat-models/authorisation.md`** — a new issue, or a scope item on #199 or #201 | DOR-05 for every W3 authorisation flow (section 7.2) |
| **OD-13**, the approved role-to-permission grants | Every unit that adds a permission is implementing a documented default rather than an approved grant. It was due at the W1 exit gate |
| **OD-05's valuation half**, co-signed by the accountant | #40's fifth acceptance criterion. `e08-f03-13` is the unit that closes it and it needs a person, not a session |
| **OD-02 and OD-17**, the hosting model and a provisioned non-production environment | `e15-f03-7`'s measured RPO and RTO, and everything in #59 and #60 that needs somewhere to deploy |
| **Whether 256 open sub-issues is the tracker you want** | If not, the alternative is filing them wave by wave. The breakdown does not change; only how much of it is visible at once does |

---

## 8. What cannot be verified in a cloud session

This environment is a headless Linux container with no printer, no scanner, no phone, no payment vendor, no
provisioned staging, no screen reader and no second human. `./scripts/dev doctor` governs which test tiers run at all,
and integration tests skip with a visible reason when no database is reachable — which `CI=true` turns into a failure,
so nothing merges unverified. The rule for everything below is the same: **the session writes the instrument and
leaves the reading blank.**

| Obligation in this batch | What cannot be done here | The honest substitute |
| --- | --- | --- |
| `e07-f01-9` — printer and template records with a photograph, Android, iPhone/iPad and wedge-scanner scans, durability observations | Everything physical | Already correct: `e07-f01-8` ships the empty record, index and durability templates and a deterministic `label-sheet` command; the CLI-rendered sheets and the five invalid-payload refusals are the machine half and the **interim** W3 evidence. The three traceability cells stay blank until a person signs |
| `e07-f01-6` — print station | A real browser print dialog, a remembered printer, kiosk printing | The body stubs `window.print`; the new-tab and Download-PDF rungs are the testable fallbacks |
| `E14-F01b-6` — independent penetration test | The engagement itself | A session finishes the pack — scope, synthetic environment, twelve roles across two branches, rules of engagement with a stop condition, report data-handling terms, the empty remediation register — and the issue stays open across the engagement, which is an owner action on the critical path to go-live |
| `E14-F01b-4b` — ABF-09 server-side request forgery | Any integration-tier proof: `IOutboundHttp` has no implementation anywhere in `src/` | A unit suite written against the port, ready for the day an adapter lands, plus a registered dated `SEC-EX` exception. State plainly that ARCH-016 is vacuous today |
| `E14-F01b-4` — ABF-07 hostile upload corpus | The integration tier, if #187 and #188 have not shipped | The documented corpus (disagreeing magic bytes, script-bearing SVG, polyglot, archive, decompression bomb, hostile filename) committed as fixtures and run in the unit tier, so the tier switch is a one-line change |
| `E15-F03-7` — dress rehearsal: provision, migrate, backup, induced migration failure, rollback, restore with measured RPO and RTO | The environment and the operator; #59 and #60 are unbuilt | Split it: a session writes the run sheet and the empty record, and only a person fills the measured figures. One issue must not hold both |
| `E15-F03-8`, `-8b`, `-9`, `-10`, `-11` — UAT scripts, training, pilot, go/no-go | The signatures, the accountant's approval, the named holders of the four owner roles, a Tamil-reading staff walkthrough | Scripts, sheets and templates are writable; every signature line stays blank. `E15-F03-11` may not tick a gate on its own evidence |
| `E09-F02-10` — the accountant's review of the sample pack | The accountant | The three-document pack and the review record as a template, plus a recorded Definition-of-Ready deferral — **not** a waiver, because RG-14 admits none |
| #200 / #55 — a real UPI or card gateway | No vendor: OD-03 is open | The fake adapter plus recorded fault injection |
| Roughly fifteen manual screen-reader rows (A11Y-BI-nn, A11Y-DP-nn, A11Y-LF-nn) | VoiceOver and TalkBack | Every body separates the automated axe and Vitest assertions from the manual row. The mitigation against a session quietly ticking it is the forced-state story map; `E09-F03-5d` builds the first one |
| Clean-clone logs from Windows/WSL and macOS (an E02 exit criterion) | Only Linux is available | Record that those two halves are unverified rather than letting #20's closure imply all three |
| Any hardware scan, device matrix row or physical custody rehearsal in #36, #37 and #52 | All of it | Now carved out as their own units, the way `e07-f01-9` is to `e07-f01-8`: `e07-f02-6` (the device matrix), `e07-f03-9` (the physical custody rehearsal) and `e12-f03-7` (the browser and device matrix) are evidence units a person runs, and no coding unit is blocked on hardware it cannot have |

---

---

## 9. How this document is maintained

1. The **Issue** column is filled in when a child is filed, in the same change that files it. A dash that survives a
   sprint means the breakdown and the tracker have diverged.
2. When an issue body and this document disagree, **the issue wins** and this document is corrected in the same pull
   request that noticed.
3. A session that splits an issue under rule 8 of [section 2](#2-how-a-session-picks-up-work) adds its sub-units here,
   with the same columns.
4. Sections 5, 6 and 7 go stale fastest, because they are statements about `main` at one commit. Re-derive them
   before the next fan-out run rather than trusting them; the numbers carry the commit they were built from in
   [section 1](#1-purpose-and-status).
