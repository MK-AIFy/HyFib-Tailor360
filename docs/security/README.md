# Security documents

This directory holds the security documents the project is judged against: the threat model for each flow, the
abuse-case catalogue, the ASVS traceability sheet, the vulnerability-management process, the exception register and
the permission matrix. Most of them arrive with #56a and #24; this index exists from #22 so that the directory has
an owner, a naming convention and a template before the first model is written, rather than after ten of them have
each invented their own shape.

Read it with [`../process/definition-of-ready.md`](../process/definition-of-ready.md) (**DOR-05**, which requires a
flow to be covered by a model before work on it starts) and
[`../process/definition-of-done.md`](../process/definition-of-done.md) (**DoD 6**, which requires a pull request to
name its model and close the controls the model maps to it).

---

## 1. Status and scope

| Field | Value |
| --- | --- |
| Status | **One model written.** The authentication flow is covered by [`threat-models/authentication.md`](threat-models/authentication.md); the rest of the set is scheduled in section 3.2 |
| Drafted | 2026-09-05, issue #22, wave W1 |
| Owner of the directory | The security owner once #56a appoints one; until then the Owner, with the technical reviewer |
| Review cadence | At every wave exit gate, and whenever a flow listed in section 3 changes |

---

## 2. What lives here, and when it arrives

| Path | Contents | Delivered by | State today |
| --- | --- | --- | --- |
| `threat-models/<flow>.md` | One model per flow: assets, data flow diagram, trust boundaries, STRIDE table, abuse cases, controls mapped to tests, residual risk | #23 (authentication), #32a (authorisation), then #56a for the rest | [`threat-models/authentication.md`](threat-models/authentication.md) is **written and reviewed** (#23, 2026-09-05). The rest are not; the template is [`../templates/threat-model.md`](../templates/threat-model.md) |
| `abuse-cases.md` | The cross-cutting abuse catalogue — insecure direct object reference, privilege escalation, workflow bypass, barcode replay, invoice and payment tampering, stock manipulation, malicious upload, export leakage, server-side request forgery, credential abuse, denial of service | #56a | Not written |
| `asvs-traceability.md` | ASVS L2 with selected L3: requirement, control, issue or pull request, test, evidence, residual risk, owner, review date | #56a | Not written |
| `vulnerability-management.md` | Triage, the remediation service levels of [`../nfr/security-operations-targets.md`](../nfr/security-operations-targets.md), and disclosure | #56a | Not written |
| `exceptions.md` | Accepted findings with approver, expiry and compensating control; a continuous-integration check fails on an expired entry | #56a | Not written |
| [`permission-matrix.md`](permission-matrix.md) | Role × permission × branch scope × step-up flag, with the rationale for each grant, and every route the application publishes with how each decides who may reach it | #24 | **Written 2026-09-06, not approved.** It is the artefact owner decision OD-13 approves, and OD-13 is open. The catalogue, the twelve roles and their default grants are code and seed data; the document is held equal to them, to the live route table and to the answers the application gives, by the tests in its section 8. A route published without a row there fails the build |
| [`field-visibility.md`](field-visibility.md) | Response view × field × class × permission, and the field set each role is shown, derived from that and from the default grants | #24 | **Written 2026-09-06, not approved.** The mechanism and its invariants are code and are tested; no endpoint returns any of the three declared views yet, and none can until #26, #28, #32a and #33 publish them. The document says so in its own first paragraph, so that a reader who never reaches this table is not misled |
| [`role-walkthrough.md`](role-walkthrough.md) | The scripted per-role walkthrough used to demonstrate the authorisation model | #24, completed by #25 and #32a | **Written 2026-09-06. Section 3 run 2026-09-06 and its results recorded**; sections 4 to 6 are not run, because they need endpoints that enforce a permission (#32a) and an administration surface that can create users (#25). Those two issues carry them, and the plan's blueprints say so |

Anything in this directory is **Internal** at least, and several of the documents above quote identifiers and
control names rather than data. Nothing here contains a secret, a credential, real customer data or the detail of an
unpatched exploitable finding — an open finding lives in its issue, under the security issue template's rules, not
in a document the whole repository can read.

---

## 3. Threat models

### 3.1 Writing one

Copy [`../templates/threat-model.md`](../templates/threat-model.md) to `threat-models/<flow>.md`, where `<flow>` is
the flow's name in the vocabulary of [`../prd/glossary.md`](../prd/glossary.md) — `authentication`,
`order-workflow`, `barcode-custody`, `billing-payment`, and so on. One flow per file. The identifiers the template
defines (`TM-nnn` threats, `AB-nn` abuse cases, `CTL-nn` controls, `RR-nn` residual risks) are cited from pull
requests and tests, so they are allocated once and never renumbered.

### 3.2 Coverage, and what stands in until a model exists

**DOR-05** allows an issue to start before its flow has a model, provided the flow appears here with **when** it
will be covered and the controls that apply in the meantime. That is what this table is for; it is deleted when
#56a completes the set.

"When" is a wave and its exit gate, not a calendar date: this project schedules by wave
([`../IMPLEMENTATION_PLAN.md`](../IMPLEMENTATION_PLAN.md) Section 6), and a date invented for a table is a date
nobody is held to. The wave is the commitment — a flow whose covering issue has not landed by its wave's exit gate
is a finding at that gate.

| Flow | Model | Covered by | When (wave) | What stands in until then |
| --- | --- | --- | --- | --- |
| Authentication, sessions, multi-factor authentication, recovery | [`threat-models/authentication.md`](threat-models/authentication.md) | #23 | W1 — **written 2026-09-05** | Nothing: the model is the authority |
| Authorisation, roles and branch scope | `threat-models/authorisation.md` | **#32a**, the first issue to publish a permissioned route. #24 delivered the mechanism and [`permission-matrix.md`](permission-matrix.md) and did not deliver this document | W3 | The matrix itself, [`../architecture/architecture-rules.md`](../architecture/architecture-rules.md) and the deny-by-default rule it asserts. Two controls are already decided and tested and belong in the model when it is written: a foreign resource and a missing one are both answered `404 security.resource-not-found`, and the resource pipeline fails closed — a host that omits `UseTailor360ResourceScope()` refuses every request rather than skipping the check |
| Customer data, measurements and media | `threat-models/customer-and-media.md` | #56a | W2 | [`../nfr/data-classification.md`](../nfr/data-classification.md) handling rules, and [`../adr/0005-object-storage-authorised-delivery.md`](../adr/0005-object-storage-authorised-delivery.md) |
| Order and garment workflow | `threat-models/order-workflow.md` | #56a | W2 | The workflow invariants in [`../architecture/invariants.md`](../architecture/invariants.md) |
| Barcode identity and custody | `threat-models/barcode-custody.md` | #56a | W2 | The custody invariants in [`../architecture/invariants.md`](../architecture/invariants.md) |
| Inventory and the stock ledger | `threat-models/inventory.md` | #56a | W2 | The same |
| Billing, invoicing and payments | `threat-models/billing-payment.md` | #56a | W2 | [`../architecture/invariants.md`](../architecture/invariants.md) and the financial handling rules of [`../nfr/data-classification.md`](../nfr/data-classification.md) |
| Reports and exports | `threat-models/reports-exports.md` | #56a | W2 | [`../adr/0011-reporting-read-models.md`](../adr/0011-reporting-read-models.md) |
| Customer links and feedback | `threat-models/customer-links.md` | #56a | W2 | The expiring, purpose-bound link design of plan Section 4.4 |
| Integration adapters | `threat-models/integrations.md` | #56a | W2 | [`../adr/0012-integration-ports-and-adapters.md`](../adr/0012-integration-ports-and-adapters.md) |
| Deployment, secrets and the runtime | `threat-models/deployment.md` | #56a | W2 | [`../platform/secrets.md`](../platform/secrets.md), [`../adr/0010-deployment-portability.md`](../adr/0010-deployment-portability.md) and [`../process/branch-protection.md`](../process/branch-protection.md) section 8 |

---

## 4. Related documents

| Document | Why it matters here |
| --- | --- |
| [`../templates/threat-model.md`](../templates/threat-model.md) | The template every model in section 3 is a copy of |
| [`../nfr/data-classification.md`](../nfr/data-classification.md) | The seven classes a model's assets are classified against |
| [`../nfr/security-operations-targets.md`](../nfr/security-operations-targets.md) | Remediation service levels, rotation cadence and incident targets |
| [`../nfr/traceability.md`](../nfr/traceability.md) | Which security target each control proves, and who is accountable |
| [`../process/definition-of-ready.md`](../process/definition-of-ready.md) | **DOR-05**, the rule section 3.2 satisfies |
| [`../process/definition-of-done.md`](../process/definition-of-done.md) | **DoD 6**, which closes a model's controls per pull request |
| [`../process/release-gates.md`](../process/release-gates.md) | **RG-08** to **RG-11**, the release scans these documents are read against |
| [`../process/waivers.md`](../process/waivers.md) | Where a high residual risk is carried, with an expiry and a corrective issue |
| [`../platform/secrets.md`](../platform/secrets.md) | How credentials and secrets are stored, injected and rotated |
