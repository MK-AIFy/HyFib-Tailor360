# Work breakdown — the open tracker turned into session-sized units

This document breaks the open feature issues of [`../IMPLEMENTATION_PLAN.md`](../IMPLEMENTATION_PLAN.md) into units
one coding session can finish, orders them, and records what the breakdown found wrong with the tracker on the way.
It is a planning aid, not a specification: **the authority for the scope of any piece of work is the GitHub issue**,
and where this document and an issue body disagree, the issue wins and this document is corrected.

---

## 1. Purpose and status

| Field | Value |
| --- | --- |
| Status | **Draft — proposed breakdown.** Binding on a session only once the children below are filed and the owner decisions of [section 6.5](#65-decisions-the-owner-must-make-before-a-session-starts) are answered |
| Drafted | 2026-09-13, against `main` at `afe01b4`, from branch `claude/github-issues-planning-v1mu7r` |
| Owner of the document | Technical reviewer |
| Authority for scope | The GitHub issue. This document indexes; the issue body carries scope, out of scope, acceptance criteria and the evidence list |
| Read with | [`definition-of-ready.md`](definition-of-ready.md), [`definition-of-done.md`](definition-of-done.md), [`release-gates.md`](release-gates.md), plan Sections 6.2, 6.4, 7 and 8–9 |
| Covers | The 61 new children of seven parents (#35, #38, #41, #42, #43, #56, #61), the 35 open issues already session-sized, and the schedule over all 96 |
| Does not cover | Seventeen open feature issues that still need decomposing — [section 6.3](#63-the-seventeen-parents-that-still-need-decomposing) lists them and their seams |

**What this is for.** Several Sonnet sessions will work this backlog concurrently, each in its own container, each able
to see only its own issue. A session cannot discover that the issue next to it is about to rewrite the same file, that
its dependency is a plan identifier rather than a real issue, or that the premise in its issue body is false against
`main`. This document is where those facts live, so that a human can approve the shape of the work once and each
session can then be handed exactly one unit of it.

**What it is not.** It is not a substitute for reading the issue. Every child below has a full body — scope, out of
scope, acceptance criteria, the named tests, the evidence the pull request must paste — filed on the issue itself.
The tables here carry only what is needed to choose and order the work.

**The honest summary, in three sentences.** The 61 children are good work and 20 units are startable against `main`
today. Fourteen open feature issues are neither merged nor decomposed, and they add sixteen levels of false depth to a
graph that holds at most eight levels of genuine parallel work. Four shared files — the OpenAPI document, the
permission matrix, `SystemRoles.cs` and the client router — cap real concurrency at about five sessions, whatever the
module boundaries say.

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
   [section 6.1](#61-four-re-scopes-before-any-branch-is-cut) and
   [section 6.5](#65-decisions-the-owner-must-make-before-a-session-starts). If the issue does not pass,
   say so on the issue and stop; do not start and improvise.
4. **Respect the concurrency slot.** Two sessions may run at once only when they touch no common module, migration
   chain, `DbContext`, host registration file, or any of the six serialisers in
   [section 5.2](#52-concurrency-slots-and-the-six-serialisers). The slot, not the module, is the unit of isolation.
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
   [Section 7](#7-what-cannot-be-verified-in-a-cloud-session) lists every such row and the honest substitute.
10. **Do not edit another child's traceability status cell.** [`../nfr/traceability.md`](../nfr/traceability.md) and
    [`../prd/traceability.md`](../prd/traceability.md) are contended; each child's body names the cells it owns.

---

## 3. The state of the tracker today

One row per feature issue #17–#61, with the verdict the inventory pass reached after checking the working copy rather
than the issue text. `parent-closeable` means the deliverables are on `main` and the issue needs verifying and
closing, not working. `partially-decomposed-gaps-remain` is where this breakdown adds children.

| Issue | Feature | State | Verdict | Action now |
| --- | --- | --- | --- | --- |
| #17 | E01-F01 workflows, glossary, taxonomy | open | documentation-only | Delivered under `docs/prd/`. Outstanding item is the owner's approval record |
| #18 | E01-F02 architecture, ownership, ADR baseline | open | documentation-only | Delivered: ADR-0001..0015, ARCH-001..023 all enforced |
| #19 | E01-F03 NFRs, SLOs, data policy, Definition of Done | open | documentation-only | Delivered under `docs/nfr/` and `docs/process/` |
| #20 | E02-F01 solution scaffold and local environment | closed | parent-closeable | Nothing to plan |
| #21 | E02-F02 persistence, migrations, outbox, flags | open | parent-closeable | Verify and close; 73 migrations, outbox and flag surfaces shipped |
| #22 | E02-F03 CI gates, governance, Claude Code workflow | open | parent-closeable | Verify and close, minus the exception-process clause (see 6.1) |
| #23 | E03-F01 authentication, sessions, MFA, recovery | closed | parent-closeable | Nothing to plan |
| #24 | E03-F02 RBAC, branch scopes, authorisation tests | open | parent-closeable | Verify and close; matrix and `RoleMatrixTests` shipped |
| #25 | E03-F03 audited administration | open | parent-closeable | Verify and close; eight admin routes shipped |
| #26 | E04-F01 customer profiles, consent, search, timeline | open | fully-decomposed-ok | Server complete; one open child #182, **itself a split candidate** |
| #27 | E04-F02 measurement-template administration | open | parent-closeable | All descendants closed; close the parent |
| #28 | E04-F03 measurement capture and versioning | closed | parent-closeable | Nothing to plan |
| #29 | E05-F01 stitching-category and service catalogue | open | parent-closeable | Child #85 closed with the backend; close the parent |
| #30 | E05-F02 shape and design catalogue | open | fully-decomposed-ok | Four children closed; #141 and #142 open |
| #31 | E05-F03 customer material and reference images | open | fully-decomposed-ok | #187, #188, #189, #192 open; Media is a stub |
| #32 | E06-F01 estimates, orders, job cards | open | fully-decomposed-ok | #199, #201, #204 open. **#204 is a split candidate** |
| #33 | E06-F02 production workflow, assignment, workboard | open | no-children-needs-split | **Undecomposed.** 16 items wait behind it |
| #34 | E06-F03 QC, rework, hold, cancellation | open | no-children-needs-split | **Undecomposed.** 13 items wait |
| #35 | E07-F01 barcode identifiers, labels, reprints | open | partially-decomposed-gaps-remain | Six children added — [section 4.1](#41-35--e07-f01-barcodes-labels-and-the-print-station) |
| #36 | E07-F02 camera, scanner and manual scanning | open | no-children-needs-split | **Undecomposed, and on the critical path.** 13 items wait |
| #37 | E07-F03 custody transfers, phase scans, reconciliation | open | no-children-needs-split | **Undecomposed, and on the critical path.** 13 items wait |
| #38 | E08-F01 inventory items, units, suppliers, locations | open | partially-decomposed-gaps-remain | Six children added — [section 4.2](#42-38--e08-f01-the-inventory-client) |
| #39 | E08-F02 immutable stock ledger | open | no-children-needs-split | **Undecomposed.** 14 items wait |
| #40 | E08-F03 low-stock alerts, stocktake, valuation | open | no-children-needs-split | **Undecomposed.** 14 items wait |
| #41 | E09-F01 pricing, discounts, GST engine | open | partially-decomposed-gaps-remain | Eight children added — [section 4.3](#43-41--e09-f01-the-pricing-administration-client) |
| #42 | E09-F02 invoices, numbering, PDFs, immutability | open | partially-decomposed-gaps-remain | Seven children added — [section 4.4](#44-42--e09-f02-the-invoice-client-and-its-proofs) |
| #43 | E09-F03 advances, payments, receipts, dispatch gate | open | partially-decomposed-gaps-remain | Three children added — [section 4.5](#45-43--e09-f03-the-billing-read-and-the-screen-evidence) |
| #44 | E10-F01 sales, GST, payment, receivables reporting | open | fully-decomposed-ok | #183..#186 open; Reporting is a stub |
| #45 | E10-F02 pipeline, workload, turnaround analytics | open | no-children-needs-split | **Undecomposed** |
| #46 | E10-F03 inventory and profitability analytics, exports | open | no-children-needs-split | **Undecomposed** |
| #47 | E11-F01 notification templates, consent, delivery | open | fully-decomposed-ok | #208..#213 open; Notifications is a stub |
| #48 | E11-F02 delivery queue, customer status, dispatch | open | no-children-needs-split | **Undecomposed, and on the critical path.** 13 items wait |
| #49 | E11-F03 feedback, alterations, service recovery | open | no-children-needs-split | **Undecomposed.** 11 items wait |
| #50 | E12-F01 design system and role layouts | open | no-children-needs-split | **Part-built.** Primitives shipped; evidence and role dashboards are not |
| #51 | E12-F02 installable PWA, safe updates, resilience | open | no-children-needs-split | **Undecomposed, and on the critical path.** 13 items wait |
| #52 | E12-F03 accessibility, cross-browser, performance | open | no-children-needs-split | **Undecomposed, owns the Playwright suite.** All 13 E15-F03 children wait |
| #53 | E13-F01 versioned API, BFF, OpenAPI, idempotency | closed | parent-closeable | Nothing to plan |
| #54 | E13-F02 integration events and signed webhooks | open | fully-decomposed-ok | #202, #206, #207 open |
| #55 | E13-F03 payment, messaging, accounting, print adapters | open | fully-decomposed-ok | #200, #203, #205 open. **#205 needs re-scoping** |
| #56 | E14-F01 threat models and ASVS baseline | open | needs the mandated #56a/#56b split | Eighteen children added — [section 4.6](#46-56--e14-f01-threat-models-and-the-security-baseline) |
| #57 | E14-F02 privacy, audit, encryption, secrets | open | no-children-needs-split | **Undecomposed.** Owns the retention policy two children forward-reference |
| #58 | E14-F03 observability, SLO alerting, resilience | open | no-children-needs-split | **Undecomposed.** 11 items wait |
| #59 | E15-F01 environments, CI/CD, safe database releases | open | no-children-needs-split | **Undecomposed.** 11 items wait |
| #60 | E15-F02 backups, PITR, disaster recovery, runbooks | open | no-children-needs-split | **Undecomposed** |
| #61 | E15-F03 QA, UAT, training, pilot, go-live | open | needs the mandated #61a/b/c split | Thirteen children added — [section 4.7](#47-61--e15-f03-qa-uat-training-pilot-and-go-live) |

Counted: 45 feature issues, of which 4 are closed, 9 need only verifying and closing, 8 already have the children
they need, 7 are decomposed here, and **17 remain undecomposed**.

---

## 4. The breakdown

Sixty-one children across seven parents, about 39,400 estimated lines of non-test production code. Columns are the
same in every table:

- **Key** — the planning key. The branch is `feat|fix|docs/<key>-<slug>`; the key is not the issue number.
- **Lane** — A server and platform, B client, C documents, tests and tooling, per plan Section 6.1.
- **Depends on** — what must be merged first. A `#NN` is an existing issue; a key is another child here.
- **Est.** — estimated non-test production lines. Test code is excluded, which is why some rows are tiny and still a
  full session.
- **Issue** — filled in with the issue number when the child is filed. A dash means not yet filed.

| Parent | Children | Est. lines | Lanes |
| --- | --- | --- | --- |
| #35 E07-F01 | 6 | 3,610 | A, B, C |
| #38 E08-F01 | 6 | 6,130 | B |
| #41 E09-F01 | 8 | 9,950 | B |
| #42 E09-F02 | 7 | 4,020 | A, B, C |
| #43 E09-F03 | 3 | 1,770 | A+B, B |
| #56 E14-F01 | 18 | 7,150 | C, C+A |
| #61 E15-F03 | 13 | 6,810 | A+C, B, C |

### 4.1 #35 — E07-F01 barcodes, labels and the print station

Four server children already exist (#190 identifiers and allocation, #191 label rendering and queueing, #194 resolve
and lookup, #197 reprint and invalidate). These six add the durable queue, the station, the two client screens and the
physical evidence the parent's acceptance criterion 4 asks for. Wave W3.

| Key | Title | Lane | Modules | Depends on | Est. | Issue |
| --- | --- | --- | --- | --- | --- | --- |
| `e07-f01-5` | Make the print queue durable: `platform.print_jobs` and the station drain port | A | Platform.Persistence, Platform.Abstractions, Integration.Infrastructure | — | 650 | — |
| `e07-f01-5b` | Serve the branch print station: the queue API, its permission and the print frame | A | Tailor360.Web, Platform.Security, Platform.Persistence, Identity.Application | `e07-f01-5` | 750 | — |
| `e07-f01-6` | Client: the print station screen that drains its branch's queue | B | clients/pwa | `e07-f01-5b`, #191, #190 | 900 | — |
| `e07-f01-7` | Client: the label print screen, the shared print action and the reprint dialog | B | clients/pwa | `e07-f01-6`, #191, #194, #197, #201 | 800 | — |
| `e07-f01-8` | Label content policy and a repeatable label test-sheet command | C | Tailor360.Cli, docs/custody, docs/dev | #191, #190, **#214** | 450 | — |
| `e07-f01-9` | Printed label evidence: printer, device and durability records | C | docs/custody, docs/nfr, docs/dev | `e07-f01-8`, `e07-f01-5b`, `e07-f01-6`, `e07-f01-7`, #194 | 60 | — |

`e07-f01-9` is **not a coding session**: it is the hardware rehearsal a person runs at the head branch, filling the
record templates `e07-f01-8` ships. `e07-f01-5` contradicts a merged decision and `e07-f01-7` owns a shared transport
helper a #42 child also plans — both in [section 6.1](#61-four-re-scopes-before-any-branch-is-cut).

### 4.2 #38 — E08-F01 the inventory client

Four server children exist (#193 schema, suppliers and locations; #195 items, units and conversions; #196 reorder
rules; #198 customer material). All six children here are the administration surface the parent's acceptance
criterion 1 is demonstrated on, and none touches a `.csproj`. Wave W4.

| Key | Title | Lane | Modules | Depends on | Est. | Issue |
| --- | --- | --- | --- | --- | --- | --- |
| `E08-F01-5` | Client: the Inventory module foundation and the location master screens | B | clients/pwa | #193 | 1,250 | — |
| `E08-F01-5b` | Client: the supplier master screens | B | clients/pwa | #193, `E08-F01-5` | 850 | — |
| `E08-F01-6` | Client: the item register — list and the item editor | B | clients/pwa | #195, `E08-F01-5` | 1,350 | — |
| `E08-F01-6b` | Client: item units, conversions and the client mirror of invertibility and cycle detection | B | clients/pwa | #195, `E08-F01-6` | 780 | — |
| `E08-F01-7` | Client: bulk CSV item import — preview, error report and commit | B | clients/pwa, apiClient | #195, `E08-F01-6` | 850 | — |
| `E08-F01-8` | Client: the reorder-rule list and editor, per item and location | B | clients/pwa | #196, `E08-F01-5`, `E08-F01-6` | 1,050 | — |

`E08-F01-5` fixes the six routing, naming and concurrency decisions the other five inherit, so it goes first.
`E08-F01-5b` is the only screen in #38 that renders Personal data (supplier contacts) and carries
[`../nfr/data-classification.md`](../nfr/data-classification.md) section 5.9 accordingly. `E08-F01-7` may have no
server endpoint at all — [section 6.5](#65-decisions-the-owner-must-make-before-a-session-starts).

### 4.3 #41 — E09-F01 the pricing administration client

All three server children are closed; the engine, the registers and `POST /pricing/preview` are live. What has no
implementation at all is the authoring surface, so these eight are the whole of the parent's remaining scope. Wave W3,
lane B throughout, no .NET change in any of them.

| Key | Title | Lane | Modules | Depends on | Est. | Issue |
| --- | --- | --- | --- | --- | --- | --- |
| `e09-f01-4` | Client: the pricing foundation, GST registrations and the tax configuration register | B | clients/pwa | #145, #24, #25, #50 | 1,450 | — |
| `e09-f01-5` | Client: draft and edit a tax configuration version and its tax codes | B | clients/pwa | `e09-f01-4`, #145 | 1,400 | — |
| `e09-f01-5b` | Client: validate and publish a tax configuration version, and the shared findings list | B | clients/pwa | `e09-f01-5`, #145 | 1,050 | — |
| `e09-f01-6` | Client: the price-list register, a list's versions, and the conventions form | B | clients/pwa | `e09-f01-4`, #146 | 1,350 | — |
| `e09-f01-7` | Client: the price-list version editor — conventions, items and the tax-code picker | B | clients/pwa | `e09-f01-6`, `e09-f01-5`, #146, #147 | 1,400 | — |
| `e09-f01-7b` | Client: validate and publish a price-list version | B | clients/pwa | `e09-f01-7`, `e09-f01-5b`, #146, #147 | 950 | — |
| `e09-f01-8` | Client: author the discount rules of a draft price-list version | B | clients/pwa | `e09-f01-7`, `e09-f01-7b`, #146 | 900 | — |
| `e09-f01-8b` | Client: the pricing preview and the accountant's test-case shapes before publish | B | clients/pwa | `e09-f01-8`, `e09-f01-7`, `e09-f01-6`, `e09-f01-5`, #146, #147 | 1,450 | — |

The chain is almost perfectly serial because each child extends the editor the one before it built. No rate, threshold
or round-off rule is defaulted by the client in any of them, and the absence of each default is tested — plan Section 8
and [`../prd/configurable-vs-fixed.md`](../prd/configurable-vs-fixed.md) fix the fields, not the values.

### 4.4 #42 — E09-F02 the invoice client and its proofs

All three server children are closed; Billing ships 65 endpoint registrations. These seven open the read surface, the
three write acts, the compensating documents, the Billing timeline contribution, and the two tiers of proof that what
a document prints equals what was calculated. Wave W4.

| Key | Title | Lane | Modules | Depends on | Est. | Issue |
| --- | --- | --- | --- | --- | --- | --- |
| `E09-F02-4` | Client: the invoice register and the invoice detail screen | B | clients/pwa | — | 1,050 | — |
| `E09-F02-5` | Client: the in-app print view, the PDF download and the print-queue hand-off | A+B | clients/pwa, Billing | `E09-F02-4`, **`e07-f01-7`** | 900 | — |
| `E09-F02-6` | Client: draft an invoice for an order, discard it, and post it | B | clients/pwa | `E09-F02-4`, `E09-F02-5` | 800 | — |
| `E09-F02-7` | Client: cancel a posted invoice and issue credit and debit notes | B | clients/pwa | `E09-F02-4`, `E09-F02-5`, `E09-F02-6` | 850 | — |
| `E09-F02-8` | Contribute invoices, cancellations and notes to the customer timeline | A | Billing | — | 350 | — |
| `E09-F02-9` | Prove the rendered documents against the golden master, in the contract tier | C | tests/ContractTests, Directory.Packages.props | — | 40 | — |
| `E09-F02-10` | Prove the stored PDF against the persisted snapshot, and prepare the accountant's pack | C | tests/IntegrationTests, docs/billing | `E09-F02-9` | 30 | — |

`E09-F02-5` carries the only server change in the set (projecting `SupplierLegalName` and `SupplierTradeName` onto
`InvoiceCalculationPayload`) and **must** gain `e07-f01-7` as a dependency so that the binary read helper and the
print ladder are consumed rather than rebuilt. `E09-F02-9` and `E09-F02-10` are about 40 and 30 lines of non-test code
over roughly 450 and 200 lines of test code.

### 4.5 #43 — E09-F03 the billing read and the screen evidence

All six linked children are closed and the seven counter and office screens are live. What is missing is the
branch-level read the outstanding-balances screen fakes with a client-side fan-out, and the per-screen state evidence
CLAUDE.md requires. These three are the content of the unlinked #216. Wave W4.

| Key | Title | Lane | Modules | Depends on | Est. | Issue |
| --- | --- | --- | --- | --- | --- | --- |
| `E09-F03-5b` | Serve the branch's outstanding balances from one read, and page the screen through it | A+B | Billing, clients/pwa | #162, #165 | 420 | — |
| `E09-F03-5c` | Storybook state and pseudo-locale stories for the four counter billing screens | B | clients/pwa | #165 | 830 | — |
| `E09-F03-5d` | Storybook state stories for the three office screens, and the forced-state map | B | clients/pwa | `E09-F03-5b`, `E09-F03-5c`, #165 | 520 | — |

`E09-F03-5d` builds the story-id-per-state map that turns the human screen-reader run of
[`../nfr/a11y-checklist.md`](../nfr/a11y-checklist.md) section 8 into a walkthrough. Both story children should also
depend on the #220 fix, which is already merged — so the practical action is to close #220, not to add an edge.

### 4.6 #56 — E14-F01 threat models and the security baseline

Plan Section 6.2 note 11 mandates a #56a/#56b split that was never filed: the threat models must precede the flows
they cover (W2), while the enforcing policy, the regression suite, the audit and the penetration test come after them
(W5). These eighteen children realise that split. Lane C throughout, except where noted.

**#56a — W2, eight children, 4,950 lines.**

| Key | Title | Lane | Modules | Depends on | Est. | Issue |
| --- | --- | --- | --- | --- | --- | --- |
| `E14-F01a-1` | Write the abuse-case catalogue and open the threat-model programme | C | docs/security, docs/templates | — | 450 | — |
| `E14-F01a-2` | Build the ASVS Level 2 traceability skeleton with owners and verdicts | C | docs/security, docs/nfr | `E14-F01a-1` | 500 | — |
| `E14-F01a-3` | Threat models: customer data and media, customer links | C | docs/security/threat-models | `E14-F01a-1`, `E14-F01a-2` | 700 | — |
| `E14-F01a-4` | Threat models: order workflow, barcode custody, inventory | C | docs/security/threat-models | `E14-F01a-1`, `E14-F01a-2` | 850 | — |
| `E14-F01a-5` | Threat models: billing and payments, reports and exports | C | docs/security/threat-models | `E14-F01a-1`, `E14-F01a-2` | 700 | — |
| `E14-F01a-6` | Threat models: integration adapters, deployment and the runtime | C | docs/security/threat-models, infra | `E14-F01a-1`, `E14-F01a-2` | 700 | — |
| `E14-F01a-7` | Vulnerability management, the security exception register, and the expiry gate | C | docs/security, scripts, docs/process, docs/nfr | `E14-F01a-1` | 600 | — |
| `E14-F01a-8` | Add the licence audit over the CycloneDX bill of materials and wire RG-08 | C | scripts, .github/workflows, docs/process, docs/nfr | `E14-F01a-7` | 450 | — |

**#56b — W5, ten children, 2,200 lines of non-test code over roughly 2,000 of test code.**

| Key | Title | Lane | Modules | Depends on | Est. | Issue |
| --- | --- | --- | --- | --- | --- | --- |
| `E14-F01b-1` | Harden the enforcing CSP with Trusted Types, and review validation and encoding | C+A | Tailor360.Web, clients/pwa, docs/api, tests/IntegrationTests | `E14-F01a-6` | 260 | — |
| `E14-F01b-2` | Suite: direct object reference, privilege escalation, and the shared machinery | C | tests/IntegrationTests, tests/ContractTests, docs/security, docs/nfr | `E14-F01a-1`, #199, #201 | 120 | — |
| `E14-F01b-2b` | Suite: workflow bypass, barcode replay and the no-client-driven-state rule | C | tests/IntegrationTests, docs/security, docs/nfr | `E14-F01a-1`, `E14-F01a-4`, `E14-F01b-2` | 120 | — |
| `E14-F01b-3` | Suite: invoice and payment tampering | C | tests/IntegrationTests, docs/security, docs/nfr | `E14-F01a-1`, `E14-F01a-5`, `E14-F01b-2` | 120 | — |
| `E14-F01b-3b` | Suite: stock manipulation and export leakage | C | tests/IntegrationTests, tests/UnitTests, docs/security, docs/nfr | `E14-F01a-1`, `E14-F01a-4`, `E14-F01a-5`, `E14-F01b-2`, #39, #40, #183, #186, #198 | 150 | — |
| `E14-F01b-4` | Suite: malicious upload and authorised media streaming | C | tests/IntegrationTests, tests/UnitTests, docs/security, docs/nfr | `E14-F01a-1`, `E14-F01a-3`, `E14-F01b-2`, #187, #188 | 180 | — |
| `E14-F01b-4b` | Suite: server-side request forgery and the outbound edge | C | tests/IntegrationTests, tests/UnitTests, docs/security, docs/nfr | `E14-F01a-1`, `E14-F01a-6`, `E14-F01a-7`, `E14-F01b-2` | 160 | — |
| `E14-F01b-4c` | Suite: credential abuse and denial-of-service limits | C | tests/IntegrationTests, docs/security, docs/nfr | `E14-F01a-1`, `E14-F01b-2` | 140 | — |
| `E14-F01b-5` | Audit the ASVS traceability sheet against the landed models and suites | C | docs/security, docs/process, docs/nfr | all fifteen `E14-F01a-*` and `E14-F01b-*` above | 450 | — |
| `E14-F01b-6` | Prepare and run the independent penetration test with verified remediation | C | docs/security, docs/process, docs/nfr, docs/prd | `E14-F01b-5` | 500 | — |

`E14-F01a-1` freezes the `ABF-01..ABF-11` identifiers that seven later suites compile against, so it merges before any
of them. `E14-F01b-2` builds the folder, the eleven family traits, the five probe principals and the coverage harness
the other six suites extend **without editing a shared file** — it must be taken first of the seven. `E14-F01b-2`'s
dependency was filed as `#32a`, which is a plan identifier and not an issue; it is resolved above to #199 + #201, and
the document it was also relying on is orphaned — [section 6.1](#61-four-re-scopes-before-any-branch-is-cut).
`E14-F01b-6` stays open across the engagement by design.

### 4.7 #61 — E15-F03 QA, UAT, training, pilot and go-live

Plan Section 6.2 note 11 mandates a #61a/#61b/#61c split that was never filed. These thirteen children realise it:
strategy and fixtures, then the scenario suite, then the release track. Wave W5. **Every one of them sits behind work
nobody has been asked to build** — see [section 6.2](#62-work-no-issue-covers) item 4 and
[section 6.3](#63-the-seventeen-parents-that-still-need-decomposing).

| Key | Title | Lane | Modules | Depends on | Est. | Issue |
| --- | --- | --- | --- | --- | --- | --- |
| `E15-F03-1` | Write the risk-based test strategy and the legacy-import validation decision | C | docs/qa, docs/prd, docs/nfr | #52, #19, #22 | 650 | — |
| `E15-F03-2` | Consolidate the deterministic fixture library and its catalogue | A+C | tests/fixtures, tests/Tailor360.Fixtures, Tailor360.Cli, docs/qa, docs/nfr | `E15-F03-1`, **#214**, #21, #52, #26, #32, #38, #41, #47 | 700 | — |
| `E15-F03-3` | Stand up the business-scenario regression suite and automate walkthroughs 1 and 2 | B | tests/e2e, .github/workflows, docs/qa, docs/nfr | `E15-F03-1`, `E15-F03-2`, **#214**, #52, #59, #34, #35, #36, #37, #42, #43, #49 | 400 | — |
| `E15-F03-4` | Automate walkthroughs 3 to 6: multi-garment, dependencies, revisions, blocked dispatch | B | tests/e2e | `E15-F03-3`, #33, #40, #48 | 60 | — |
| `E15-F03-5` | Automate the intake, production and custody exception paths | B | tests/e2e, docs/prd | `E15-F03-3`, `E15-F03-4`, #26, #33, #36, #37, #39 | 70 | — |
| `E15-F03-6` | Automate the money exceptions and prove dispatch under every policy | B | tests/e2e, docs/prd | `E15-F03-3`, `E15-F03-4`, `E15-F03-5`, #42, #48, #37, #59 | 60 | — |
| `E15-F03-6b` | Automate the delivery, feedback and notification exceptions, and publish the report | B | tests/e2e, docs/qa, docs/nfr, docs/prd | `E15-F03-3`..`E15-F03-6`, `E15-F03-1`, #47, #48, #49, #55, #59 | 220 | — |
| `E15-F03-7` | Rehearse a production-like release including restore and rollback | C | docs/qa, docs/dev, infra | `E15-F03-6b`, `E15-F03-1`, **#214**, #59, #60, #58 | 500 | — |
| `E15-F03-8` | Write the shop-floor UAT scripts and the sign-off sheet | C | docs/uat | `E15-F03-6b`, `E15-F03-2`, #24, #25, #17, #60 | 750 | — |
| `E15-F03-8b` | Write the money and oversight UAT scripts and the accountant's billing approval | C | docs/uat | `E15-F03-8`, `E15-F03-2`, #43, #42, #48, #37 | 700 | — |
| `E15-F03-9` | Write the training material, quick cards, support contacts and access onboarding | C | docs/training, docs/prd | `E15-F03-8`, `E15-F03-8b`, #17, #23, #25, #57, #59, #60 | 1,000 | — |
| `E15-F03-10` | Plan the pilot: entry and exit criteria, daily reconciliation and defect triage | C | docs/launch, docs/prd | `E15-F03-7`..`E15-F03-9`, `E15-F03-6b`, #35, #36, #37, #48, #58, #59, #60 | 800 | — |
| `E15-F03-11` | Record the go/no-go decision, hypercare, the review and the evidence index | C | docs/launch, docs/evidence, docs/nfr | `E15-F03-10`, `E15-F03-9`, `E15-F03-8`, `E15-F03-8b`, `E15-F03-7`, `E15-F03-6b`, #56, #58, #59, #60 | 900 | — |

`E15-F03-7` is the only genuinely concurrent track in E15 and the one child that needs a provisioned environment and
an operator; it should be split into a writable run sheet and a person-filled measured record rather than holding
both. `E15-F03-6b` owns the four NFR-CU status cells because only it has the complete eighteen-scenario matrix to
claim them with, and `E15-F03-11` must not tick a gate on its own evidence.

---

## 5. The schedule

Built over all 96 schedulable items — the 61 children above plus the 35 open issues that are already session-sized.
Every dependency was resolved against `main`, not against the issue text, because several open issues have their
substance merged and several "already built" claims in issue bodies are wrong. Zero cycles. The `#41 ↔ #32` cycle plan
Section 6.2 note 1 warns about is already resolved on `main`: `Billing.Contracts.IPricingService` shipped with #147
and ARCH-010 holds.

### 5.1 Levels

Level 0 is startable against `main` today. A **phantom** is a feature-level issue that is neither merged nor
decomposed: it is a hole in the backlog, not work, and nothing can be scheduled in a level that holds only phantoms.

| Level | Items | Est. lines | Max concurrent | What is there |
| --- | --- | --- | --- | --- |
| 0 | 20 | ~6,460 | 8 | Six module-founding issues, all the empty-dependency documents, three client foundations |
| 1 | 23 | ~9,600 | 4 | The second layer of every module, the six threat models, three client chains starting |
| 2 | 13 | ~5,710 | 3 | Custody and Notifications delivery, the inventory and orders clients |
| 3 | 11 | ~4,600 | 3 | Labels, resolve, the security machinery, the inventory editors |
| 4 | 9 | ~2,970 | 3 | The print station, reprints, five of the seven security suites |
| 5 | 3 | ~2,250 | 2 | The label print screen, the print bridge, the pricing preview |
| 6 | 2 | ~210 | 1 | The hardware rehearsal, the stock and export suite |
| 7 | 1 | 450 | 1 | The ASVS audit |
| 8 | 1 | 500 | 1 | The penetration-test pack |
| 9–11 | **0** | — | — | **Phantoms only**: #48/#56/#59, then #49/#51/#60, then #52. No schedulable work exists here |
| 12–18 | 1 each | ~2,160 | 1 | `E15-F03-1` through `E15-F03-6b`, strictly serial |
| 19 | 2 | ~1,250 | 2 | `E15-F03-8` and `E15-F03-7`, the one concurrent pair in E15 |
| 20–23 | 1 each | ~3,400 | 1 | `E15-F03-8b`, `-9`, `-10`, `-11` |

**The 96 items contain at most eight levels of genuine parallel work. The other sixteen levels of depth are
undecomposed features.**

### 5.2 Concurrency slots and the six serialisers

Two items share a slot only when they touch no common module, migration chain, `DbContext` or host registration file —
and also none of these six files, each of which this repository enforces with a test and therefore admits exactly one
session per slot:

| Serialiser | Level-0 items touching it | Why it serialises |
| --- | --- | --- |
| `docs/api/openapi.v1.json` + `clients/pwa/src/api/schema.d.ts` | 7 | Generated *and* committed; `generate:api:check` fails on drift, so two concurrent regenerations conflict every time |
| [`../security/permission-matrix.md`](../security/permission-matrix.md) + `tests/Tailor360.IntegrationTests/Authorization/matrix.yaml` | 7 | Both are machine-parsed, so a careless merge is green-looking and wrong — the worse of the two conflicts |
| `Identity.Application/Access/SystemRoles.cs` | 6 | One file, one grant list |
| The `Tailor360.Web` registration and middleware | 7 | Six of the seven register a brand-new module |
| `clients/pwa/src/app/router.tsx` | 5 | A single flat file, one import per route, no path constants — every client item edits it |
| `clients/pwa/src/i18n/messages/index.ts` | 6 | One index; the per-family files beside it do not collide |

Three further contended files bind later levels: [`../nfr/traceability.md`](../nfr/traceability.md) status cells
(six items at level 4), `docs/security/abuse-cases.md` (five at level 4), and `.github/workflows/ci.yml` (two items,
in different parents, adding different jobs).

### 5.3 Recommended number of simultaneous sessions: five

Not a round number — it is the level-0 collision arithmetic. A healthy batch is **one OpenAPI-touching server item,
one router-touching client item, and three that touch neither** (security documents, contract tests, Storybook-only
stories, CLI tooling). That is five.

- **Eight is achievable for exactly one batch**, because eight level-0 items happen to be pairwise disjoint. It is not
  sustainable: the largest pairwise-disjoint group is 8 at level 0, **4** at level 1, **3** at levels 2–4, **2** at
  level 5 and **1** from level 6 on. The non-colliding filler is nearly all spent in the first two batches.
- **Drop to three from level 2**, where the filler runs out.
- Six module-founding issues (#199 Orders, #193 Inventory, #208 Notifications, #183 Reporting, #187 Media, #202
  Integration) are all startable today and all mutually exclusive in one slot. **Put exactly one in each of the first
  six batches.** Between them they account for 124 of the graph's waiting edges.

### 5.4 Start here — the twenty items whose every dependency is merged

Ordered by bottleneck weight, grouped into slot-safe batches of five. "Waiting" is how many schedulable items
transitively wait on this one.

| # | Item | Waiting | What it does |
| --- | --- | --- | --- |
| 1 | #199 | 33 | Turn the built `OrderDraft` domain into Application commands and the module's first HTTP routes |
| 2 | `E14-F01a-1` | 25 | Write the eleven abuse-case families and freeze the `ABF-nn` identifiers |
| 3 | #142 | 17 | The design picker and the job-card design component over the merged #140 reads |
| 4 | `E14-F01a-7` | 15 | Vulnerability management, `SECURITY.md`, the exception register and the SARIF expiry gate |
| 5 | `E09-F02-9` | 1 | Prove all 17 golden-master cases at the renderer level, with no database |
| 6 | #193 | 24 | Found the `inventory` schema with the supplier and location masters |
| 7 | `E14-F01a-2` | 23 | The ASVS Level 2 traceability sheet with owners and four-value verdicts |
| 8 | `e09-f01-4` | 7 | The client pricing foundation and the two registers over the merged #145 |
| 9 | `E09-F03-5c` | 1 | The billing story harness and the counter-screen state stories |
| 10 | #214 | 0 | Fix `CliHost` so `migrate --dry-run` works in Development — **required evidence for many items below** |
| 11 | #208 | 21 | Found the `notifications` schema with templates, versions and safe rendering |
| 12 | `E09-F02-4` | 3 | The invoice register, the detail screen and the `I-` barcode lookup |
| 13 | `e07-f01-5` | 18 | `platform.print_jobs`, `DatabasePrintQueue` and the `IPrintStationQueue` drain port |
| 14 | `E09-F02-8` | 0 | Billing's `ITimelineSource` over its own three tables |
| 15 | #141 | 0 | The design administration screens — **after #142 merges, not merely after its batch** |
| 16 | #183 | 17 | Found the `reporting` schema, the projection runner, checkpoints and rebuild |
| 17 | #182 | 0 | The customer client screens over the complete merged server side |
| 18 | #187 | 17 | Found the `media` schema with upload, quarantine and malware scan |
| 19 | `E09-F03-5b` | 1 | One branch-scoped outstanding-balances read, replacing the client fan-out |
| 20 | #202 | 12 | Found the `integration` schema and `webhook_subscriptions` |

| Batch | OpenAPI + matrix owner | `router.tsx` owner | Collision-free remainder |
| --- | --- | --- | --- |
| 1–5 | #199 | #142 | `E14-F01a-1`, `E14-F01a-7`, `E09-F02-9` |
| 6–10 | #193 | `e09-f01-4` | `E14-F01a-2`, `E09-F03-5c`, #214 |
| 11–15 | #208 | `E09-F02-4` | `e07-f01-5`, `E09-F02-8`, #141 (sequence after #142) |
| 16–20 | #183, **then** #187, **then** `E09-F03-5b`, **then** #202 — four owners, four batches | #182 | — |

Batch 4 is therefore really four sequential batches. That is the honest cost of six module-founding issues arriving at
once, and it is the reason the recommendation is five sessions and not eight.

### 5.5 The critical path — 24 sequential items, ten of them phantoms

```
#199 → #201 → #190 → #191 → #197 → e07-f01-7 → e07-f01-9 → #36† → #37† → #48† → #51† → #52†
     → e15-f03-1 → -2 → -3 → -4 → -5 → -6 → -6b → -8 → -8b → -9 → -10 → -11
```

In prose: orders must exist before barcodes can be allocated, barcodes before labels, labels before reprints,
reprints before the client label screen, that screen before the hardware rehearsal, the rehearsal before scanning
(#36), scanning before custody (#37), custody before the delivery gate (#48), the gate before PWA resilience (#51),
resilience before the accessibility and performance suite (#52) — and only then does the thirteen-item release track
begin, which is itself almost perfectly serial because each walkthrough child extends the same `tests/e2e` area and
the same append-only tables in [`../prd/exceptions.md`](../prd/exceptions.md) and `docs/qa`.

`†` marks a phantom. Five of them are on this path and six more (#33, #34, #39, #40, #59, #60) feed the rest of the
release track. **No number of concurrent sessions shortens this. Filing and sizing #36, #37 and #52 shortens it more
than any amount of concurrency.**

### 5.6 The bottleneck items

| Item | Level | Waiting | Why |
| --- | --- | --- | --- |
| #199 | 0 | 33 | Founds every Orders route; through #201 it reaches #35, #36, #37, #33, #34 and the whole release track |
| #201 | 1 | 32 | Introduces `IConfirmationParticipant<TEvent>`, which does not exist in `src/` and which #190 needs |
| `E14-F01a-1` | 0 | 25 | Freezes the abuse-case identifiers seven security suites compile against |
| #193 | 0 | 24 | Founds the `inventory` schema; all six #38 client children and #195/#196/#198 sit behind it |
| `E14-F01a-2` | 0 | 23 | The ASVS sheet every model and suite writes a verdict into, and `E14-F01b-5` audits |
| #190 | 2 | 21 | Founds the `custody` schema and the barcode payload format |
| #208 | 0 | 21 | Founds the `notifications` schema |
| #195 | 1 | 20 | The item model every later inventory screen and the stock ledger reads |
| `E08-F01-5` | 1 | 20 | Fixes the six client decisions the other five #38 children inherit |
| #191 | 3 | 19 | Renders and queues the label every print screen and the rehearsal need |
| #209 | 1 | 19 | The consent, quiet-hours and de-duplication gate every notification channel passes |
| `E14-F01b-2` | 3 | 19 | Builds the machinery the six later suites extend without editing a shared file |
| #210 | 2 | 18 | The delivery engine #212, #213, #40, #48 and #49 all consume |
| `e07-f01-5` | 0 | 18 | Creates `platform.print_jobs` in the shared `platform` migration chain |
| `E08-F01-6` | 2 | 18 | The item editor #38's acceptance criterion 1 is judged on |
| #183, #187, #142 | 0 | 17 each | Found `reporting`, found `media`, publish the job-card design component |

---

## 6. Gaps, duplicates and decisions

Two crosscheck passes read every planned child against the 96 open issues and against the working copy. Nineteen
collisions and twenty uncovered deliverables came back. This section is the part of the document a reviewer should
read first, because most of it has to be acted on **before** a branch is cut rather than during review.

### 6.1 Four re-scopes before any branch is cut

In each of these, two sessions write the same file or the same deliverable, and one pull request is thrown away. The
fix is an edit to an issue body, not a code decision.

| Collision | Survivor | Required edit before either is taken |
| --- | --- | --- |
| `e07-f01-5` ↔ **#205** — both delete `LoggingPrintQueue` | `e07-f01-5` | Re-scope #205 to the bridge only (`print_bridge_adapters`, the ADR-0012 section 4.3 exception, the fake bridge, degraded health). Strike "replaces the log-only behaviour" and the false premise "`IPrintQueue` and the print-station screen already exist and work" — **there is no print-station screen on `main`** — and add `e07-f01-5`/`-5b` as dependencies |
| `E09-F03-5b`/`5c`/`5d` ↔ **#216** — the same four deliverables | The three children | Link #216 under #43 and convert it into a tracking parent: replace its checklist and acceptance criteria with links to the three children plus the one human item, the screen-reader run. Left implementable, a session assigned #216 writes all three pull requests |
| `e07-f01-7` ↔ `E09-F02-5` — one `apiClient.ts`, two blob helpers, two print ladders | `e07-f01-7` | `e07-f01-7` (W3) owns the binary read helper — one name, `apiBlob` — and `PrintTargetAction`. `E09-F02-5` (W4) adds `e07-f01-7` to its dependencies and consumes both, declaring only the invoice view, its stylesheet and the supplier-name projection |
| `e07-f01-5b` ↔ `E14-F01b-1` — one CSP constant, opposite directions | Both, serialised | `e07-f01-5b` (W3) widens `frame-src` to `'self'`; `E14-F01b-1` (W5) must state `frame-src 'self'` as its baseline instead of `'none'`, or whichever merges second fails its own test. The widening must appear as a justified named control in `E14-F01a-6`'s `deployment.md`, not be discovered as a red test |

Five more need an edit but not a re-scope:

| Issue | Edit |
| --- | --- |
| **#191** | Strike required-verification item 3 (printed test sheets, device scans, durability note) and the "proven on real thermal and A4 output" half of acceptance criterion 1, replacing both with a pointer to `e07-f01-9`. #191 cannot produce them — the station a label must be printed by is `e07-f01-5b` plus `e07-f01-6` — so as written it can never close honestly |
| **#197** | Re-scope acceptance criterion 4 to its server half (an invalidated identity has no active replacement, resolve reports it, `permittedActions` omits `reprint-label`) and delegate the "no valid label" card to `e07-f01-7`, which builds it |
| **#214** | Take it first. `e07-f01-8`, `E15-F03-2`, `E15-F03-3` and `E15-F03-7` all need a working CLI and none of them listed #214; the dependency is added in [section 4](#4-the-breakdown) above |
| **#22** | Strike the security-exception-process clause from step 6 with a pointer to #56, keeping the dependency-update half, which nothing planned covers |
| **#184** | State that its screen is the *reported* invoice register, a reconciled projection wearing data-as-of and projection lag, and that it may not register a route under `/billing`. `E09-F02-4` ships the operational register a cashier works from |

And four mechanical orderings to write into the bodies rather than discover: **#202 owns the `integration` schema's
first migration** (#200, #203 and #205 follow it — four issues currently claim to found it); the permission matrix is
edited in the order `e07-f01-5b` → `E09-F03-5b` → the E14 suites, append-only to each child's own block; the two
`ci.yml` jobs (`E14-F01a-8`'s licence gate in the existing `sbom` job, `E15-F03-3`'s new end-to-end job) conflict
mechanically and the second author rebases; and #36's retrofit list gains `/print-station` and `/labels/print`, because
`e07-f01-6` builds an interim keyboard-wedge field that #36's shared scanner replaces.

### 6.2 Work no issue covers

Twenty deliverables have no owning issue. Six must exist before this batch can be worked; the rest are surfaces and
obligations that fall between two issues.

**Must exist first.**

| Proposed | What it is | Why it blocks |
| --- | --- | --- |
| `[E06-F01-0]` | `docs/security/threat-models/authorisation.md` — STRIDE over the permission, branch-scope and resource-scope pipeline | [`../security/README.md`](../security/README.md) section 3.2 assigns it to `#32a`; every planned E14 child excludes it as "#32a's"; #199, #201 and #204 claim no such scope. `E14-F01b-2` must *update* a file nobody writes, `E14-F01b-5` will downgrade on the dangling reference, and **DOR-05 is unsatisfiable for every W3 issue** |
| `[E02-F02-4]` | Extend `seed-synthetic` beyond Identity and Customers to every module a journey touches | `SeedSyntheticCommand.cs` is 156 lines and names only two modules; `tests/fixtures/` holds two files, both billing. `./scripts/dev reset` cannot produce a shop with orders, invoices, stock or custody, so the eighteen walkthroughs of `E15-F03-3`..`-6b` have no dataset. `E15-F03-2` catalogues what exists; it does not extend the seeder |
| `[E12-F03-0]` | The Playwright harness, page objects and the end-to-end tier `scripts/dev` already promises | `scripts/dev` line 434 says "tests/e2e does not exist yet: the Playwright suite arrives with issue #52", and #52 has no children. All thirteen E15-F03 items sit behind it |
| `[E14-F03-0]` | SLO dashboards, burn-rate alert rules and their runbook links | `infra/observability/` holds one file; `tests/load` does not exist. E14's exit criterion, RG-14's monitoring row and `E15-F03-11`'s go/no-go all read dashboards that nobody builds |
| `[E08-F01-2a]` | Publish the bulk item import endpoint — or add it as a scope item on #195 | `E08-F01-7` admits it cannot tell whether #195 publishes a multipart import operation. If it does not, no issue owns the endpoint and #38's import criterion fails |
| `[E15-F03-12]` | `docs/evidence/`, the closure-record template, and the E01–E05 closure backfill | Plan Section 6.4 requires `docs/evidence/eXX-closure.md` per epic. The directory does not exist, only `e15-closure.md` is planned, and five epics are closeable now — this is already overdue |

**Splits this batch should have made.**

| Proposed | Replaces | Why |
| --- | --- | --- |
| `[E04-F01a-1]`, `-2`, `-3` | #182 | Six screens in one issue — search and find-or-create; detail, edit with concurrency and the timeline tabs; duplicate review, step-up merge and consent. Seven planned children depend on it and the client has no `customers` directory at all |
| `[E06-F01-3a]`, `-3b`, `-3c` | #204 | Intake, estimate, job card, confirmation and revision in one issue, for a module with zero client routes. Ten planned children depend on it |

**Orphaned surfaces and obligations.**

| Proposed | What it is |
| --- | --- |
| `[E11-F01-7]` | Client: notification template administration — author, preview, test-send, publish, retire. #208 is server-only and #209–#213 add no authoring screen, yet [`../nfr/accessibility-localisation.md`](../nfr/accessibility-localisation.md) section 10.4 names "the template author" as a role |
| `[E13-F02-4]` | Client: webhook subscription administration and delivery diagnostics — or a recorded decision on #202/#207 that administration is API-only, and why |
| `[E06-F01-3d]` | Client: capture and return customer-owned material at intake — #198 pushes it to "Orders' own UI" and #204 never claims it |
| `[E01-F02-1]` | Register the no-client-driven-state rule as **ARCH-024**. `E14-F01b-2b` introduces a genuine mechanical rule over every state-changing endpoint with no identifier and no row in [`../architecture/architecture-rules.md`](../architecture/architecture-rules.md), which CLAUDE.md section 3 makes authoritative |
| `[E12-F01-6]` | Complete the `ta-IN` catalogue and build the identifier-coverage report the Tamil gate measures. `E15-F03-9` defers Tamil honestly; nothing un-defers it, and criterion 1's build report exists as neither script nor CI job |
| `[E02-F03-4]` | Reconcile [`release-gates.md`](release-gates.md) line 58's Nightly cadence with `ci.yml`, which has only `pull_request` and `push` triggers. Either add the schedule or correct the RG-05/06/07/11 frequency claims — one of the two, with the reason |
| `[E01-F01-2]` | Record the owner approvals three epic exit criteria are defined as: the #17 glossary and workflow-map approval (the still-open W0 gate), the #27 template business review, the #29/#30 add-without-deployment demonstrations |
| `[E12-F01-7]` | Decide A11Y-OD-01 (which screen-reader pairing a pull request uses) and extend `E09-F03-5d`'s forced-state story map to the print-station, inventory, pricing and invoice families |
| `[E14-F01a-9]` | Add the notification delivery flow to the threat-model register, or record why it is covered. "Notification" appears in none of the ten planned models |
| `[E09-F03-6]` | Superseded: the two Owner read gaps #220 names are **already fixed** on `main` by `ee831c4`. Close #220 instead |
| `[E07-F01-10]` | Correct #205's scope after `LoggingPrintQueue` is deleted — an owner edit rather than an issue, and the same edit as [section 6.1](#61-four-re-scopes-before-any-branch-is-cut) |
| `[E13-F03-x]` | Own the single outbound `HttpClient` factory ADR-0012 section 4.3 specifies. #202, #205 and #206 all depend on it, `E14-F01b-4b`'s suite tests nothing without it, and no issue builds it |

### 6.3 The seventeen parents that still need decomposing

This is a second mandatory fan-out run, not a backlog. The seams are recorded so the next run does not start cold.

| Parent | Seams |
| --- | --- |
| #33 | workflow engine and SLA evaluator · assignment and eligibility · client workboard and queues · admin workflow editor |
| #34 | the five exceptional paths · the ready-for-delivery gate and its `CustodyReconciled` predicate · Catalog QC checklist versions · client screens · dashboards |
| #36 | parser, debounce and namespace classification · camera path · wedge-scanner and manual-by-job-number · device matrix record |
| #37 | custody state machine and transfers · idempotency and stale rejection · the fail-closed dispatch gate · `ICustodyStateQuery` for #34 · client screens · physical rehearsal |
| #39 | ledger core and rebuild property tests · purchasing and transfer half-post · reservation and consumption · client receipts and material panel |
| #40 | alert evaluation with hysteresis · stocktake and variance approval separation · valuation and rounding golden master · client screens |
| #45 / #46 | projections and migration · screens · for #46, export governance and the mixed-load test |
| #48 | payment-cleared queue · partial-readiness policy · two-stage dispatch · customer status link and page · compensating custody on failed delivery |
| #49 | feedback token journey and its page · the alteration-request contract into Orders · staff service-recovery case surface |
| #50 | remaining primitives · visual-regression harness · device and zoom matrix with per-role screen-reader evidence |
| #51 | installability · safe service-worker update and 426 handling · encrypted local drafts with quota and eviction · the persisted offline scan queue |
| #52 | the harness (`[E12-F03-0]` above) · cross-browser and axe reports · Lighthouse budgets · client telemetry redaction |
| #57 | data inventory · data-subject request and erasure · retention jobs and `platform.retention_policies` · audit hash-chain review · key and secret rotation |
| #58 | correlation · dashboards and burn-rate alerts (`[E14-F03-0]` above) · retry-safety fault tests · load, soak and mixed-load · game days |
| #59 | provenance and deploy-time signature check · clean provisioning · expand/contract releases · failed-migration halt, rollback and roll-forward |
| #60 | encrypted immutable backups · point-in-time restore with media consistency · a DR exercise by a non-author operator · the runbooks |

**Decompose #52 first** — it alone gates thirteen items — then #36 and #37, which are on the critical path. Do not file
any E15-F03 child as startable work: they sit at levels 12 to 23.

### 6.4 Dispositions — five issues that need a decision, not a session

| Issue | Disposition | Evidence |
| --- | --- | --- |
| #91 | **Close.** Already done | `CatalogReconciler.cs` and `CatalogReconciliationHandlers.cs` exist, commit `0342dc8` |
| #172 | **Close**, noting it is an unlinked sub-issue of a closed parent | Migration `20260912180307_ReconciliationBatches`, `ReconciliationHandler.cs` and `ReconciliationApprovalRoute.tsx`, commit `44e9153` |
| #181 | **Close** in favour of #211 | #181 is a bug report against the absence of #211, which is sized and parented |
| #220 | **Close.** Already fixed | `ee831c4` added both read routes, both permission gates, `.RequireStepUp()`, the integration tests and the client wiring |
| #125 | **Do not plan.** In flight as PR #219 | — |

#214 is the only loose issue that is real work, and it belongs in the first ten because the `migrate --dry-run` output
it unblocks is required pull-request evidence throughout this batch.

### 6.5 Decisions the owner must make before a session starts

Each of these is a product or architecture decision. CLAUDE.md section 8 is explicit that an unsourced number or role
grant is a change to the product, so a session must not invent any of them.

| Decision | Why a session cannot make it | Where the answer is recorded |
| --- | --- | --- |
| Who writes `threat-models/authorisation.md` | It is assigned to a plan identifier (`#32a`) that is not an issue, and excluded by all ten planned models | A new `[E06-F01-0]`, or a scope item on #199/#201, or a widened `E14-F01b-2` |
| Whether the new `print.station` permission key is the right one | Three merged documents disagree: plan Section 8 says `custody.print_label`, `definition-of-ready.md` section 7 says `custody.print_label` + `custody.scan`, `support-matrix.md` section 3 says `print.station` | OD-13 at the W1 exit gate, and the same pull request corrects the two documents it overrules |
| Whether ADR-0014 is amended or superseded | `e07-f01-5` replaces an adapter the ADR calls interim with a durable table the ADR does not contemplate, and the ADR assigns the queue to #55 | An amendment with a dated note (the ADR is still Proposed) or a new ADR from [`../adr/0000-template.md`](../adr/0000-template.md) |
| Whether the bulk item import is JSON or multipart, and who publishes it | `E08-F01-7` cannot resolve it from the merged schema | A scope item on #195, or `[E08-F01-2a]` |
| Whether `#216` is closed or converted into a tracking parent | It double-books three planned children | #216 itself |
| Whether #184's reported invoice register and `E09-F02-4`'s operational one both exist | Two screens, one user sentence, two branch-scope leak suites | #184's body |
| Whether `ci.yml` gains a `schedule` trigger or `release-gates.md` loses its Nightly row | Four gates assert a cadence with no runner | `[E02-F03-4]` |
| The ASVS version and level, and the open-decision number it takes | `E14-F01a-2` raises it without claiming a number; `E15-F03-1` claims OD-25 and `E15-F03-10` OD-26, first-come | [`../prd/assumptions-and-open-decisions.md`](../prd/assumptions-and-open-decisions.md) section 3 |
| The ARCH-016 exception register row for `OutboundHttpClient.cs` | The register holds an exception in force for a file that does not exist, and the rule is currently vacuous | `E14-F01a-6`, in the same pull request as its residual-risk entry |
| Whether E01's W0 approval gate is open or closed | Two exit criteria are owner acts with no artefact path, and 24 open decisions have nothing driving them to closure | `[E01-F01-2]` |

---

## 7. What cannot be verified in a cloud session

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
| Any hardware scan, device matrix row or physical custody rehearsal in #36, #37 and #52 | All of it | These are undecomposed; when they are split, the physical record belongs in its own child, as `e07-f01-9` is to `e07-f01-8` |

---

## 8. How this document is maintained

1. The **Issue** column is filled in when a child is filed, in the same change that files it. A dash that survives a
   sprint means the breakdown and the tracker have diverged.
2. When an issue body and this document disagree, **the issue wins** and this document is corrected in the same pull
   request that noticed.
3. A session that splits an issue under rule 8 of [section 2](#2-how-a-session-picks-up-work) adds its sub-units here,
   with the same columns.
4. Sections 5 and 6 go stale fastest, because they are statements about `main` at one commit. Re-derive them before the
   next fan-out run rather than trusting them; the numbers carry the commit they were built from in
   [section 1](#1-purpose-and-status).
