# Permission matrix

This document is the contract between what the business intends and what the software enforces. It lists every
permission the application declares, the roles that hold it by default, whether the action is about one branch or
the whole organisation, and whether it demands multi-factor authentication, a fresh re-authentication or a stated
reason. It is written to be read by the owner who approves it and parsed by the test that enforces it: the tables
inside the `<!-- matrix:… -->` markers are read by
[`tests/Tailor360.UnitTests/Security/PermissionMatrixDocumentTests.cs`](../../tests/Tailor360.UnitTests/Security/PermissionMatrixDocumentTests.cs),
so approval and enforcement cannot drift apart without a build failing.

Read it with [`../prd/raci.md`](../prd/raci.md) for who is accountable for each activity,
[`../prd/state-transitions.md`](../prd/state-transitions.md) for the transitions each permission gates,
[`../prd/00-overview.md`](../prd/00-overview.md) for the roles in operator language,
[`../nfr/data-classification.md`](../nfr/data-classification.md) for what each field is worth protecting, and
[`role-walkthrough.md`](role-walkthrough.md) for the two-branch procedure that demonstrates the model working.

---

## 1. Status

> **Not approved.** This document is the thing owner decision **OD-13** approves, and OD-13 is open. Everything
> below is the engineering proposal, traced clause by clause to the product documents. Nothing here is a decision
> the business has taken.

| | |
| --- | --- |
| **Delivered by** | Issue #24, on 2026-09-06 |
| **Approves** | OD-13 in [`../prd/assumptions-and-open-decisions.md`](../prd/assumptions-and-open-decisions.md) — the default role-to-permission grants and any custom roles required at launch |
| **Approval state** | **Open.** Not reviewed with the business owner; not signed off |
| **Enforced today** | The catalogue, the twelve roles and their default grants are code and seed data, and the tests in section 8 hold this document equal to them, to the route table and to the answers the application actually gives. **No business endpoint declares a permission yet** — the twenty-eight routes in section 5 are #23's authentication endpoints and the host's own — so every permission row is forward-declared. The grants are nevertheless exercised as real requests: `RoleMatrixTests` asks for each of the 104 permissions as each of the twelve roles, in the caller's own branch and in another, against a real database and a real session |
| **Review cadence once approved** | Quarterly, and whenever a custom role is created ([`../nfr/security-operations-targets.md`](../nfr/security-operations-targets.md)) |

### 1.1 What the owner is being asked to approve

1. **The role list** — twelve entries in section 3, including Branch Manager as a distinct role and Measurement
   Staff as a bundle nobody holds by default. Section 2 explains why, and what changes if the answer is different.
2. **The default grants** — the `Granted to` column of section 4. Every one of them is editable afterwards by an
   administrator holding `admin.roles`; approving them settles what a new installation starts with.
3. **The flagged set** — which actions demand a second factor, which demand a fresh re-authentication, and which
   demand a stated reason. These are properties of the permission and are *not* editable by an administrator, so
   changing one is a release and an amendment to this document.
4. **The exposed routes** — section 5 lists every route the application publishes and how each decides who may
   reach it. Fifteen of them are reachable by anybody with the address — twelve declared `anonymous`, and the
   three orchestrator health probes, which are exempt by path and are just as unauthenticated. That is the part
   of this document worth reading twice.

---

## 2. The Branch Manager decision, and what it costs to change

**The question OD-13 settles.** Whether Branch Manager is a distinct role or a branch-scoped variant of Admin, and
whether Measurement Staff is separate from Reception.

**What was shipped, and why.** The plan's blueprint for issue #24 names ten roles and does not include Branch
Manager. The product documentation names it 155 times across 35 files. The two cannot both be followed, so this
document follows the documentation, on the narrow ground that it is the only reading under which no existing
document has to be edited:

| Evidence | Where |
| --- | --- |
| Branch Manager is the **sole** named actor for `orders.reschedule` | [`../prd/state-transitions.md`](../prd/state-transitions.md) line 225 |
| It is the actor on ten further transitions — revise, hold, resume, cancel, alteration decision, custody reconciliation, label reprint and invalidation, invoice cancellation | [`../prd/state-transitions.md`](../prd/state-transitions.md) sections 2 to 5 |
| It is **accountable** for four of the twenty-eight RACI rows: alteration decision, feedback capture, service recovery, stocktake | [`../prd/raci.md`](../prd/raci.md) rows 15, 20, 21, 23 |
| Admin is **defined in terms of it** — "a superset of Branch Manager without shop-floor duties" | [`../prd/00-overview.md`](../prd/00-overview.md) line 61 |
| It is a subject of six access-control rows, including recorded decision DC-11 | [`../nfr/data-classification.md`](../nfr/data-classification.md) |
| Both documents already record "treated as a distinct role" as their interim position | [`../prd/raci.md`](../prd/raci.md) section 5, [`../prd/assumptions-and-open-decisions.md`](../prd/assumptions-and-open-decisions.md) section 4 |

Reading it the other way — folding Branch Manager into Admin — would mean re-attributing eleven state transitions
to a role that `00-overview.md` says has no shop-floor duties, adding a column to the RACI grid, and rewriting six
rows of the data-classification document including a recorded decision. That is a change to the product, and a
change to the product is the owner's to make, not this issue's.

**What it costs if the owner decides otherwise.** Less than it looks, and this is the part worth reading before
the workshop:

| If the owner decides | What changes | What does not |
| --- | --- | --- |
| Branch Manager is a branch-scoped Admin | The `branch_manager` row leaves section 3 and its 76 grants move onto `admin`, whose reach becomes `branch`. One edit to `SystemRoles`, one seeding run | The permission catalogue, every flag, every endpoint. No migration. No code outside the role register |
| Measurement Staff is not a separate role | Nothing at all. It is already assigned to nobody by default, and its six permissions are already granted to Reception | Everything |
| Measurement Staff **is** staffed separately | Nothing at all. The role already exists; an administrator assigns it | Everything |

Measurement Staff is deliberately built so that **both** answers are already true: the role exists and is assigned
to nobody, and `measurements.capture` is granted to Reception as well. Whichever way OD-13 goes, no code changes.

**The vendor principal.** `hyfib_super_user` is seeded as a role holding `admin.feature_flags` and nothing else,
assigned to nobody. The plan's #25 blueprint restricts that permission to "Owner and the HyFib super-user role
only", and a permission granted to a role that does not exist is a sentence with no subject; seeding the role
empty makes the restriction checkable now and costs nothing.

---

## 3. The roles

Twelve rows. `Reach` says whether the role is meant to work inside its assigned branches or across the
organisation; it is a description of intent, and what a request actually reaches is decided by the caller's branch
assignments and by whether they hold `admin.organisation.read_all_branches`. `Assign at onboarding` says whether a
new installation is expected to put somebody in the role — `no` marks a role that exists and waits.

<!-- matrix:roles -->
| Role key | Display name | Reach | Assign at onboarding | Permissions | What the role is for |
| --- | --- | --- | --- | --- | --- |
| `owner` | Owner | organisation | yes | 50 | Approves the catalogue, prices, tax configuration, the permission matrix and alert policies; approves dispatch exceptions; reads across every branch. |
| `admin` | Admin | organisation | yes | 21 | Administers users, branches, roles, templates and integrations across the organisation. Holds no shop-floor permission and cannot publish the catalogue or change a price. |
| `branch_manager` | Branch Manager | branch | yes | 76 | Supervises the day in the assigned branches: exception queues, holds, reschedules, reconciliation cases, stocktake and variance approvals, service recovery. |
| `reception` | Reception | branch | yes | 24 | The counter: finds or creates the customer, records consent, captures measurements and images, builds and confirms the order, prints labels and takes the advance. |
| `measurement_staff` | Measurement Staff | branch | no | 6 | The measurement bundle on its own, for a shop that staffs the measurements queue separately from the counter. Assigned to nobody by default: the same permissions are granted to Reception, so either reading of OD-13 works without a change here. |
| `tailor_master` | Tailor Master | branch | yes | 20 | The workshop lead: starts production, pins the workflow version, assigns garment jobs, runs the workboard, records quality results and decides rework. |
| `tailor` | Tailor | branch | yes | 8 | The stitching role: takes custody by scan, starts and completes phases, records material issue, consumption, return and wastage. |
| `inventory_clerk` | Inventory Clerk | branch | yes | 11 | The store room: items, suppliers, locations and reorder rules, purchase receipts, low-stock response, stocktakes and variance explanations. |
| `cashier` | Cashier | branch | yes | 16 | The money: invoices, payments, allocations, receipts, and the cashier session with its denomination count and reconciliation. |
| `delivery_staff` | Delivery Staff | branch | yes | 12 | The delivery queue: the receive scan that evaluates the dispatch gate, dispatch, the doorstep handover, and failed or returned deliveries. |
| `auditor` | Auditor | organisation | yes | 13 | Read-only across the organisation: audit events, financial records, reports and the exports a review needs. Never a state-changing principal. |
| `hyfib_super_user` | HyFib Super User | organisation | no | 1 | The vendor-side principal, permitted to change feature flags with a mandatory reason and an evaluation audit, and nothing else. Assigned to nobody by default. |
<!-- /matrix:roles -->

Every role in this table is a **system role**: this release ships it, the seeding command writes it, and it cannot
be deleted. Its name, description and grants remain editable by an administrator holding `admin.roles`, and custom
roles may be created alongside these. The one thing an administrator cannot do is invent a permission: a grant is
refused unless the key is in the catalogue below.

---

## 4. The permissions

One row per permission, 104 of them, ordered by module. `Scope` says whether the action is about one branch's
operational data or about the organisation as a whole — and the rule that follows from it is checked: **an
organisation-scoped permission is granted only to a role whose reach is the organisation.** `MFA`, `Step-up` and
`Reason` are properties of the permission in code, not of the endpoint, and this document and the code are held
equal by test.

- **MFA** — the holder's session must have completed a second factor. Any principal whose effective permissions
  include one of these must enrol one, custom roles included.
- **Step-up** — a fresh re-authentication within the last five minutes, on top of a session that has already
  answered a second factor. Every step-up permission also demands MFA; the reverse is not true.
- **Reason** — free text the caller supplies, written to the audit event. Never defaulted by the client.

<!-- matrix:permissions -->
| Permission | Module | Scope | MFA | Step-up | Reason | Granted to | Built by | Rationale |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| `billing.approve_dispatch_exception` | Billing | branch | yes | yes | yes | `owner` | #43 | Single-use, approved by someone other than the dispatcher. The plan's interim position is the Owner only (`raci.md` footnote (15), XQ-02) |
| `billing.cancel_invoice` | Billing | branch | yes | yes | yes | `owner`, `branch_manager`, `cashier` | #42 | A posted invoice is cancelled by a compensating record, never edited; Cashier with approval, or the Branch Manager (`state-transitions.md` line 333) |
| `billing.create_invoice` | Billing | branch | no | no | no | `branch_manager`, `cashier` | #42 | Drafting an invoice from a deliverable order |
| `billing.manage_price_lists` | Billing | organisation | no | no | no | `owner` | #41 | Drafting a price-list version |
| `billing.override_price` | Billing | branch | yes | yes | yes | `owner`, `branch_manager` | #41 | A discount or override above the threshold; the Owner is consulted and holds it, the Branch Manager applies it (`raci.md` footnote (3)) |
| `billing.post_credit_note` | Billing | branch | yes | no | yes | `branch_manager`, `cashier` | #42 | The other compensating record, and the ordinary way to correct a posted invoice |
| `billing.post_invoice` | Billing | branch | no | no | no | `branch_manager`, `cashier` | #42 | Posting numbers the invoice and makes it immutable |
| `billing.print_receipt` | Billing | branch | no | no | no | `branch_manager`, `cashier` | #43 | Receipts are the Cashier's; Reception takes the advance and the Cashier receipts it (`raci.md` row 5) |
| `billing.publish_price_list` | Billing | organisation | yes | yes | yes | `owner` | #41 | Publication changes what every future order is quoted at (`raci.md` row 24) |
| `billing.update_invoice` | Billing | branch | no | no | no | `branch_manager`, `cashier` | #42 | A draft may be amended; a posted invoice may not |
| `payments.allocate` | Billing | branch | no | no | no | `branch_manager`, `cashier` | #43 | Allocation under the automatic rule |
| `payments.allocate_manual` | Billing | branch | yes | yes | yes | `branch_manager`, `cashier` | #43 | Allocating against the automatic rule moves money between invoices; step-up and a reason (`raci.md` row 5) |
| `payments.record` | Billing | branch | no | no | no | `branch_manager`, `reception`, `cashier` | #43 | Reception may record an advance and the Cashier is accountable for the money (`raci.md` footnote (5)) |
| `payments.record_intent` | Billing | branch | no | no | no | `branch_manager`, `reception`, `cashier` | #43 | The intent recorded before a provider is called, so a timeout is resolvable rather than assumed |
| `payments.refund` | Billing | branch | yes | yes | yes | `owner`, `branch_manager`, `cashier` | #43 | Money leaving the business |
| `payments.reverse` | Billing | branch | yes | yes | yes | `owner`, `branch_manager`, `cashier` | #43 | A payment recorded that never cleared; reversal is how the day still reconciles |
| `payments.session` | Billing | branch | yes | no | no | `branch_manager`, `cashier` | #43 | The cashier session with its denomination count. Flagged for multi-factor because it is what the day's takings reconcile to (`state-transitions.md` line 351) |
| `catalog.checklists.edit` | Catalog | organisation | no | no | no | `owner` | #34 | Quality checklists are drafted alongside the workflow they check |
| `catalog.checklists.publish` | Catalog | organisation | yes | yes | yes | `owner` | #34 | A QC result records the checklist version it was judged against |
| `catalog.edit` | Catalog | organisation | no | no | no | `owner` | #29 | Drafting the catalogue is the Owner's, who is accountable for row 25 of `raci.md` |
| `catalog.publish` | Catalog | organisation | yes | yes | yes | `owner` | #29 | A published version is quoted, worked to and invoiced against; publication is the point of no return |
| `catalog.workflows.edit` | Catalog | organisation | no | no | no | `owner` | #33 | A workflow version decides the phases a garment passes through |
| `catalog.workflows.publish` | Catalog | organisation | yes | yes | yes | `owner` | #33 | Orders in flight are pinned to the version that was live when production started |
| `custody.approve_reconciliation` | Custody | branch | yes | yes | yes | `owner`, `branch_manager` | #37 | Above threshold, by a different user from the one who recorded it; step-up and a reason (`state-transitions.md` line 291) |
| `custody.bulk_print_label` | Custody | branch | no | no | no | `branch_manager`, `reception` | #35 | A batch print for a whole order at the counter |
| `custody.confirm_delivery` | Custody | branch | no | no | no | `branch_manager`, `delivery_staff` | #48 | The doorstep handover, with OTP or signature as the branch configuration decides |
| `custody.dispatch` | Custody | branch | no | no | no | `branch_manager`, `delivery_staff` | #37 | Fails closed: an unevaluated payment answer blocks the scan and the attempt is audited |
| `custody.generate_identity` | Custody | branch | yes | yes | yes | `branch_manager` | #35 | An identity created outside confirmation was created by a person, not by the system (`raci.md` footnote (7)) |
| `custody.invalidate_label` | Custody | branch | yes | yes | yes | `admin`, `branch_manager` | #35 | The other half of a reprint, and dangerous for the same reason |
| `custody.manual_lookup` | Custody | branch | no | no | yes | `branch_manager`, `reception`, `tailor_master`, `tailor`, `delivery_staff` | #36 | Typing an identity instead of scanning it needs a reason, because it is the path that bypasses the check character |
| `custody.open_case` | Custody | branch | no | no | yes | `branch_manager`, `tailor_master`, `delivery_staff` | #37 | Any staff member may open a reconciliation case (`state-transitions.md` line 290); resolving one is a different permission |
| `custody.print_label` | Custody | branch | no | no | no | `branch_manager`, `reception` | #35 | Printing the label that bonds a garment to its identity |
| `custody.receive` | Custody | branch | no | no | no | `branch_manager`, `tailor_master`, `delivery_staff` | #37 | The receive scan is what evaluates the dispatch gate |
| `custody.reconcile` | Custody | branch | no | no | yes | `branch_manager` | #37 | A correction event on a case, which is how a custody chain is repaired rather than rewritten |
| `custody.record_delivery_outcome` | Custody | branch | no | no | yes | `branch_manager`, `delivery_staff` | #48 | A failed delivery spends the dispatch authorisation; the second attempt is re-evaluated from scratch |
| `custody.reject_transfer` | Custody | branch | no | no | yes | `branch_manager`, `tailor_master` | #37 | Refusing an incoming garment needs the reason the sender will read |
| `custody.reprint_label` | Custody | branch | yes | yes | yes | `admin`, `branch_manager` | #35 | Breaks the one-to-one bond between a garment and its identity; step-up and a reason (`raci.md` footnote (8), EX-07) |
| `custody.scan` | Custody | branch | no | no | no | `branch_manager`, `reception`, `tailor_master`, `tailor`, `delivery_staff` | #37 | The universal shop-floor action: every handover of a garment is a scan, not a memory |
| `custody.transfer_out` | Custody | branch | no | no | no | `branch_manager`, `tailor_master` | #37 | A two-sided transfer with condition evidence on both legs |
| `custody.verify_label` | Custody | branch | no | no | no | `branch_manager`, `reception`, `tailor_master` | #35 | Recording that the printed label was checked against the garment |
| `catalog.templates.edit` | Customers | organisation | no | no | no | `owner`, `admin` | #27 | Measurement templates are owned by Customers/Measurements; drafting is Owner and Admin (plan #27) |
| `catalog.templates.publish` | Customers | organisation | yes | yes | yes | `owner`, `admin` | #27 | A published template version is what confirmed measurements are pinned to and cannot be withdrawn from them |
| `customers.create` | Customers | branch | no | no | no | `branch_manager`, `reception` | #26 | Intake is Reception's accountability (`raci.md` row 1) |
| `customers.deactivate` | Customers | branch | no | no | yes | `branch_manager` | #26 | Records are deactivated, never deleted, so history stays resolvable |
| `customers.export` | Customers | organisation | yes | no | yes | `owner`, `auditor` | #26 | A subject access request hands a person everything held about them; Owner and Auditor only (#25 restricts export to those two) |
| `customers.merge` | Customers | branch | yes | yes | yes | `owner`, `branch_manager` | #26 | Irreversible. `raci.md` footnote (1) puts it with the Branch Manager, with step-up and a reason, so Reception is consulted rather than free to merge |
| `customers.read` | Customers | branch | no | no | no | `owner`, `admin`, `branch_manager`, `reception`, `measurement_staff`, `cashier`, `delivery_staff`, `auditor` | #26 | The identifying fields only. Every workshop role needs to find a customer; none of them needs the phone number |
| `customers.read_consent` | Customers | branch | no | no | no | `owner`, `branch_manager`, `reception`, `auditor` | #26 | Which purposes were agreed decides which messages may be sent at all |
| `customers.read_contact` | Customers | branch | no | no | no | `owner`, `branch_manager`, `reception`, `cashier`, `delivery_staff`, `auditor` | #26 | Split from `customers.read` because `data-classification.md` classifies contact separately and delivery is the only shop-floor role that needs it |
| `customers.read_notes` | Customers | branch | no | no | no | `owner`, `branch_manager` | #26 | Free text about a person, so it stays with the counter's supervisor and the Owner |
| `customers.request_deletion` | Customers | organisation | yes | yes | yes | `owner`, `admin` | #26 | Starts erasure, which cannot be undone once the retention job has run |
| `customers.restrict` | Customers | organisation | yes | no | yes | `owner`, `admin` | #26 | Restriction stops messages and analytics for that person and must survive a later edit |
| `customers.update` | Customers | branch | no | no | yes | `branch_manager`, `reception` | #26 | A correction to a customer record carries a reason so the timeline reads as a history rather than a mystery |
| `measurements.capture` | Customers | branch | no | no | no | `branch_manager`, `reception`, `measurement_staff` | #28 | Granted to Reception **and** to Measurement Staff, so either reading of OD-13 works without a change here |
| `measurements.read_sheet` | Customers | branch | no | no | no | `owner`, `branch_manager`, `reception`, `measurement_staff`, `tailor_master`, `tailor` | #28 | A measurement sheet is sensitive personal data; the read is audited explicitly (`data-classification.md` DC-06) |
| `admin.branches` | Identity | organisation | yes | yes | yes | `owner`, `admin` | #25 | A branch's timezone and calendar move every due date and report cut-off in it |
| `admin.roles` | Identity | organisation | yes | yes | yes | `owner`, `admin` | #25 | Editing a grant edits this document's meaning, so it is the one action that must never be possible from a merely-remembered session |
| `admin.users` | Identity | organisation | yes | yes | yes | `owner`, `admin` | #25 | Changing who may sign in is the most consequential change in the system; step-up and a reason, per `raci.md` row 26 where both columns read "All" |
| `integration.manage_webhooks` | Integration | organisation | yes | yes | yes | `owner`, `admin` | #54 | A subscription names an external destination for this shop's data |
| `integration.replay_delivery` | Integration | organisation | yes | no | yes | `owner`, `admin` | #54 | A replay sends that data again |
| `inventory.approve_negative_stock` | Inventory | branch | yes | yes | yes | `owner`, `branch_manager` | #39 | Letting a balance go negative is a decision about the branch's policy, not about one movement (EX-02) |
| `inventory.approve_variance` | Inventory | branch | yes | yes | yes | `owner`, `branch_manager` | #40 | A different user from the one who counted, above the configured threshold (`raci.md` footnote (17)) |
| `inventory.manage_items` | Inventory | branch | no | no | no | `branch_manager`, `inventory_clerk` | #38 | The item register, its units and its conversions |
| `inventory.manage_locations` | Inventory | branch | no | no | no | `branch_manager`, `inventory_clerk` | #38 | Storage locations, which stock balances are held against |
| `inventory.manage_reorder_rules` | Inventory | branch | no | no | no | `branch_manager`, `inventory_clerk` | #38 | Reorder points are what the low-stock alert fires on |
| `inventory.manage_suppliers` | Inventory | branch | no | no | no | `branch_manager`, `inventory_clerk` | #38 | Suppliers and their terms |
| `inventory.record_movement` | Inventory | branch | no | no | yes | `branch_manager`, `tailor`, `inventory_clerk` | #39 | Every receipt, issue, return and wastage is a ledger entry with a reason |
| `inventory.stocktake` | Inventory | branch | no | no | yes | `branch_manager`, `inventory_clerk` | #40 | Counting and recounting, which is the Inventory Clerk's work; approving what the count found is not |
| `inventory.view_reports` | Inventory | branch | no | no | no | `owner`, `branch_manager`, `inventory_clerk`, `auditor` | #40 | Balances, movement history and the low-stock queue |
| `inventory.view_valuation` | Inventory | branch | no | no | no | `owner`, `branch_manager`, `inventory_clerk`, `auditor` | #40 | Valuation carries cost prices, which is why it is separate from the balance report |
| `media.delete` | Media | branch | no | no | yes | `owner`, `branch_manager` | #31 | Deleting evidence before its retention date needs a reason on the record |
| `media.read` | Media | branch | no | no | no | `owner`, `admin`, `branch_manager`, `reception`, `measurement_staff`, `tailor_master`, `tailor`, `inventory_clerk`, `delivery_staff`, `auditor` | #31 | Necessary and never sufficient: the streaming endpoint re-authorises against the owning job and logs the access (`data-classification.md` DC-08) |
| `media.upload` | Media | branch | no | no | no | `branch_manager`, `reception`, `measurement_staff`, `tailor_master`, `tailor`, `inventory_clerk`, `delivery_staff` | #31 | Uploading is the outer gate; the bytes go to quarantine and are decoded in the worker |
| `feedback.manage_cases` | Notifications | branch | no | no | yes | `branch_manager`, `reception` | #49 | Service recovery: the Branch Manager is accountable and Reception does the work (`raci.md` row 21) |
| `feedback.read` | Notifications | branch | no | no | no | `owner`, `branch_manager`, `reception` | #49 | Responses arrive through a one-time customer link and are read by the branch that has to act on them |
| `notifications.manage_templates` | Notifications | organisation | yes | no | yes | `owner`, `admin` | #47 | A template change changes what every future message says, in every language |
| `notifications.replay` | Notifications | organisation | yes | no | yes | `owner`, `admin` | #47 | A replay can send a customer a second message |
| `orders.alteration_decide` | Orders | branch | no | no | yes | `branch_manager` | #34 | Price, due date and who bears the cost — the Branch Manager is accountable (`raci.md` row 15) |
| `orders.assign` | Orders | branch | no | no | no | `branch_manager`, `tailor_master` | #33 | Assignment against capability and capacity is the Tailor Master's (`00-overview.md` section 2) |
| `orders.cancel` | Orders | branch | no | no | yes | `owner`, `branch_manager` | #34 | Branch Manager or Owner (`state-transitions.md` line 134) |
| `orders.cancel_job` | Orders | branch | no | no | yes | `branch_manager` | #34 | One garment off a live order, which changes what the customer is invoiced for |
| `orders.complete_rework` | Orders | branch | no | no | yes | `branch_manager`, `tailor_master` | #34 | Closing a rework is what reopens the ready-for-delivery gate's predicates |
| `orders.confirm` | Orders | branch | no | no | no | `branch_manager`, `reception` | #32a | Confirmation freezes every snapshot and allocates the barcode identities inside the same transaction |
| `orders.estimate` | Orders | branch | no | no | no | `branch_manager`, `reception` | #32a | Issuing an estimate prices the draft and sends the customer a link; the audit action is `orders.issue_estimate` |
| `orders.hold` | Orders | branch | no | no | yes | `branch_manager`, `tailor_master` | #34 | Supervisory: the Branch Manager and, where a hold reopens production, the Tailor Master (`state-transitions.md` line 130) |
| `orders.intake` | Orders | branch | no | no | no | `branch_manager`, `reception` | #32a | The draft is shared across the branch's holders of this permission (`state-transitions.md` line 124) |
| `orders.open_rework` | Orders | branch | no | no | yes | `branch_manager`, `tailor_master` | #34 | Rework after a failed check is the Tailor Master's decision (`raci.md` row 14) |
| `orders.phase_transition` | Orders | branch | no | no | no | `branch_manager`, `tailor_master`, `tailor` | #33 | The everyday shop-floor action; every phase start and completion is also a custody scan |
| `orders.read` | Orders | branch | no | no | no | `owner`, `admin`, `branch_manager`, `reception`, `measurement_staff`, `tailor_master`, `tailor`, `inventory_clerk`, `cashier`, `delivery_staff`, `auditor` | #32a | The order and its jobs. Field-level minimisation, not this permission, is what keeps pricing off a Tailor's job card |
| `orders.record_qc` | Orders | branch | no | no | no | `branch_manager`, `tailor_master` | #34 | Recorded against the pinned checklist version, with a reason on failure |
| `orders.reschedule` | Orders | branch | no | no | yes | `branch_manager` | #34 | The Branch Manager alone. `state-transitions.md` line 225 names no other actor, and a promised date is a promise to a customer |
| `orders.resume` | Orders | branch | no | no | yes | `branch_manager`, `tailor_master` | #34 | The other half of a hold, and held by the same two roles |
| `orders.revise` | Orders | branch | no | no | yes | `branch_manager`, `reception` | #34 | Reception or Branch Manager, with a reason (`state-transitions.md` line 127) |
| `orders.revise_design` | Orders | branch | no | no | yes | `branch_manager`, `reception` | #34 | A design change after confirmation and before cutting reprices the garment |
| `orders.start_production` | Orders | branch | no | no | no | `branch_manager`, `tailor_master` | #33 | Pins the workflow version the job is worked to |
| `admin.audit.read` | Platform | organisation | yes | no | no | `owner`, `auditor` | #25 | The audit trail names actors and resources; Owner and Auditor only (#25 blueprint) |
| `admin.diagnostics.read` | Platform | organisation | no | no | no | `owner`, `admin` | #58 | Background job state and queue depth; operational, not personal data |
| `admin.feature_flags` | Platform | organisation | yes | yes | yes | `owner`, `hyfib_super_user` | #25 | Owner and the vendor principal only, with a mandatory reason and an evaluation audit (`00-overview.md` section 2.1, plan Section 4.4) |
| `admin.health.read` | Platform | organisation | no | no | no | `owner`, `admin` | #58 | The detailed health report carries backup age and provider state (`raci.md` row 28) |
| `admin.organisation.read_all_branches` | Platform | organisation | yes | no | no | `owner`, `admin`, `auditor` | #24 | For a person, reach and never authority: it satisfies an organisation-wide read and grants no write outside the branch their session is working in (`branch-scenarios.md` section 3.3). A background job declaring organisation branch scope carries the same key and additionally acts in every branch by declaration, which is a different thing and is bounded by its `[WorkerJob]` permissions |
| `admin.outbox.replay` | Platform | organisation | yes | no | yes | `owner`, `admin` | #25 | A replay can duplicate an outbound effect, so it carries a reason; the operator path is the command line until #25 adds the endpoint |
| `audit.export` | Platform | organisation | yes | no | yes | `owner`, `auditor` | #57 | An export leaves the system; Owner and Auditor only, with a reason recorded on the export itself |
| `reports.export` | Reporting | branch | yes | no | yes | `owner`, `branch_manager`, `auditor` | #46 | Flagged outright rather than "above the row threshold": a flag is a property of a permission, and the threshold stays as #46's separate control |
| `reports.read` | Reporting | branch | no | no | no | `owner`, `admin`, `branch_manager`, `tailor_master`, `auditor` | #44 | Branch scope narrows the rows and field minimisation narrows the columns, which is how the Tailor Master sees throughput and not money (`data-classification.md` DC-11) |
<!-- /matrix:permissions -->

### 4.1 What the flags add up to

| | Count |
| --- | --- |
| Permissions declared | 104 |
| Demanding multi-factor authentication | 36 |
| Demanding a fresh re-authentication as well | 24 |
| Demanding a stated reason | 54 |
| Organisation-scoped | 27 |

**Which roles this forces into enrolment.** Multi-factor enrolment is demanded of any principal whose effective
permission set contains a flagged permission, or whose role name is in `Identity:Mfa:RequiredRoles` (Owner, Admin
and Cashier by default), or who holds a permission under a configured prefix (`admin.`, `billing.`). Against the
default grants that resolves to:

| Role | Must enrol a second factor | Because |
| --- | --- | --- |
| Owner, Admin, Auditor | Yes | Named roles, and organisation-scoped flagged permissions |
| Branch Manager | Yes | Holds `customers.merge`, `inventory.approve_variance`, `payments.refund` and twelve other flagged permissions |
| Cashier | Yes | Named role, and holds `payments.session`, `payments.refund` and `billing.cancel_invoice` |
| HyFib Super User | Yes | `admin.feature_flags` |
| Reception, Tailor Master, Tailor, Inventory Clerk, Delivery Staff, Measurement Staff | No | No flagged permission and no configured prefix. Reception holds `payments.record`, which is not flagged; the Cashier holds the receipt and the session |

That last row is a decision, not an accident. Reception was deliberately not granted `billing.print_receipt`:
receipts are the Cashier's under [`../prd/raci.md`](../prd/raci.md) rows 5 and 18, and granting it would have
matched the `billing.` prefix and pushed every counter phone into authenticator enrolment for an action the
business does not give Reception anyway.

---

## 5. The endpoints

One row per route the application publishes, per method it answers. `Declaration` says how the route decides
who may reach it, and there are four kinds rather than two:

| Declaration | Means |
| --- | --- |
| `permission` | The route demands a permission from section 4, and the `Branch scope` cell says how far that demand reaches |
| `anonymous` | The route is deliberately reachable without a session, and carries a recorded justification and a review reference |
| `self-service:<level>` | The route is about the caller's own session or account rather than about the business, and the level is the weakest session it will act on: `live-session`, `sign-in-complete` or `second-factor-satisfied` |
| `health-probe` | An orchestrator probe under `/health/`. The one standing exemption, and it is listed here rather than left out |

`Audited as` is the action written to the audit trail. Every state-changing route has one — that is ARCH-008 — and a
read has one only when the read itself is sensitive.

**Every route the application publishes is in this table, and every row names a route it publishes.** Neither
direction is a convention: `AuthorisationMatrixTests` reads the composed route table and fails on a route with no
row and on a row with no route. A route added without a row here does not quietly go unreviewed; it fails the build.

Today no route demands a permission. The twenty-eight below are the authentication endpoints of #23 and the host's
own routes, which is why the `Permission` column is empty throughout and section 4 is entirely forward-declared.
The first business endpoint fills in a row here in the same pull request that maps it.

<!-- matrix:endpoints -->
| Method | Route | Declaration | Permission | Branch scope | Resource | Audited as | What it is for |
| --- | --- | --- | --- | --- | --- | --- | --- |
| `GET` | `/api/v1/admin/users/{userId}` | permission | `admin.users` | organisation | — | — | Reads one staff account for an administration screen, with the version an edit must be made against. Not audited: it discloses no more than the list the same administrator may already read, and auditing every read would bury the changes in the trail |
| `POST` | `/api/v1/admin/users/{userId}/suspend` | permission | `admin.users` | organisation | — | `identity.user.suspended` | Suspends an account and ends its live sessions in the same request. Step-up, a written reason, `If-Match` against the version the administrator saw, and an `Idempotency-Key` so a retried request does not suspend twice |
| `GET` | `/api/v1/antiforgery` | anonymous | — | — | — | — | The token pair a browser must hold before any state-changing request. Reachable without a session because a caller who has no token cannot sign in either |
| `POST` | `/api/v1/auth/login` | anonymous | — | — | — | `identity.sign-in.succeeded` | Sign in. Anonymous by definition; the attempt is rate-limited, throttled per credential and audited whether it succeeds or fails |
| `POST` | `/api/v1/auth/logout` | self-service:live-session | — | — | — | `identity.session.signed-out` | Ends this session. A half-signed-in session may end itself, which is the one thing besides finishing the sign-in that it may do |
| `POST` | `/api/v1/auth/logout-all` | self-service:sign-in-complete | — | — | — | `identity.session.signed-out-everywhere` | Ends every session of this account. Needs a finished sign-in, because ending somebody's sessions from a half-authenticated one would be a denial of service with one factor |
| `POST` | `/api/v1/auth/mfa/challenge` | self-service:live-session | — | — | — | `identity.mfa.satisfied` | Answers the second factor. The whole point is that it is reached by a session that has not yet answered it |
| `POST` | `/api/v1/auth/mfa/enrol` | self-service:live-session | — | — | — | `identity.mfa.enrolment-started` | Starts enrolling an authenticator, including on the sign-in where enrolment is first demanded |
| `POST` | `/api/v1/auth/mfa/enrol/confirm` | self-service:live-session | — | — | — | `identity.mfa.enrolment-confirmed` | Confirms the authenticator by proving a code from it |
| `POST` | `/api/v1/auth/mfa/recovery-codes` | self-service:second-factor-satisfied | — | — | — | `identity.mfa.recovery-codes-issued` | Issues a fresh set of recovery codes, and invalidates the old set. Only for a session that has just proved a factor |
| `GET` | `/api/v1/auth/passkeys` | self-service:sign-in-complete | — | — | — | — | Lists the caller's own passkeys. A read of the caller's own account, not of anybody else's |
| `POST` | `/api/v1/auth/passkeys/assert` | anonymous | — | — | — | `identity.passkey.signed-in` | Signs in with a passkey. Anonymous for the same reason the password path is |
| `POST` | `/api/v1/auth/passkeys/assert/options` | anonymous | — | — | — | `identity.passkey.ceremony-started` | Starts the passkey sign-in ceremony and returns its challenge |
| `POST` | `/api/v1/auth/passkeys/register` | self-service:sign-in-complete | — | — | — | `identity.passkey.registered` | Registers a passkey against the caller's own account |
| `POST` | `/api/v1/auth/passkeys/register/options` | self-service:sign-in-complete | — | — | — | `identity.passkey.ceremony-started` | Starts the registration ceremony and returns its challenge |
| `DELETE` | `/api/v1/auth/passkeys/{passkeyId}` | self-service:second-factor-satisfied | — | — | — | `identity.passkey.removed` | Removes one of the caller's own passkeys. The identifier is another account's to guess, so the handler answers a foreign one exactly as it answers one that does not exist |
| `POST` | `/api/v1/auth/recovery/confirm` | anonymous | — | — | — | `identity.recovery.completed` | Completes a password recovery with the emailed token. Anonymous because the caller has by definition lost their way in |
| `POST` | `/api/v1/auth/recovery/request` | anonymous | — | — | — | `identity.recovery.requested` | Requests a password recovery. Answers identically whether or not the address is known |
| `GET` | `/api/v1/me/` | self-service:live-session | — | — | — | — | What this session is and what it still owes. Reached by a half-signed-in session so the client can find out what to ask for next |
| `GET` | `/api/v1/sessions/` | self-service:sign-in-complete | — | — | — | — | The caller's own device and session inventory |
| `DELETE` | `/api/v1/sessions/{sessionId}` | self-service:sign-in-complete | — | — | — | `identity.session.revoked` | Revokes one of the caller's own sessions. The other identifier-guessing surface, and answered the same uninformative way |
| `GET` | `/api/version` | anonymous | — | — | — | — | Build and version, for the client's minimum-version check. Reviewed in #20 |
| `ANY` | `/api/{**path}` | anonymous | — | — | — | — | The API fallback: an unmatched `/api/**` path is a problem document, never the client shell |
| `ANY` | `/health/live` | health-probe | — | — | — | — | Process liveness for the orchestrator. The standing exemption: an orchestrator has no session and never will |
| `ANY` | `/health/ready` | health-probe | — | — | — | — | Readiness for the load balancer, on essential dependencies only |
| `ANY` | `/health/startup` | health-probe | — | — | — | — | Startup, which refuses traffic until migrations have been applied |
| `ANY` | `/health/{**path}` | anonymous | — | — | — | — | The health fallback, so an unmatched probe path is a 404 and not the client shell |
| `GET` | `/openapi/{documentName}.json` | anonymous | — | — | — | — | The generated API document. **Development only** — it is not mapped in any other environment, and this row exists because the tests run the application in Development |
| `GET` | `/{*path}` | anonymous | — | — | — | — | The client shell. Every non-file path returns `index.html` so the application can route it |
| `HEAD` | `/{*path}` | anonymous | — | — | — | — | The same, for a caller that only wants the headers |
<!-- /matrix:endpoints -->

---
## 6. Names that look like permissions and are not

A reader who greps the product documents for dotted names will find these. None of them is in the catalogue, and
each is left out for a stated reason.

| Name | What it actually is |
| --- | --- |
| `orders.ready_state_changed` | An audit action. [`../prd/raci.md`](../prd/raci.md) row 16 is explicit: no role, however senior, declares a garment ready. The ready-for-delivery gate computes it from workflow completion, QC, evidence, holds and custody |
| `orders.draft_create`, `orders.issue_estimate`, `orders.close` | Audit actions in [`../prd/state-transitions.md`](../prd/state-transitions.md). The permissions those transitions demand are `orders.intake`, `orders.estimate` and — for closure — none, because it is computed |
| `payments.open_session`, `payments.close_session` | Audit actions. The permission is `payments.session`, as [`../prd/raci.md`](../prd/raci.md) row 5 states in the same sentence |
| `custody.recipient_confirmation` | Branch configuration: `otp_or_signature`, `signature` or `otp`. It grants nobody anything |
| `custody.dispatch_policy`, `dispatch.allow_on_advance` | Branch configuration read by the dispatch gate |
| `authz.denied`, `custody.event-conflict`, `media.unavailable` | Audit actions and problem-detail codes |

---

## 7. What this document does not yet cover

Sections 3, 4 and 5 are declared, seeded, published and tested. What is not yet true is that any of it gates a
piece of business: **no business endpoint declares a permission**, so every row of section 4 is forward-declared
and the `Built by` column names the issue that puts each to work. That is a statement about how far the
application has been built, not about how far it has been checked — the grants in section 4 are exercised as real
HTTP requests today, by every role, in two branches, against a real database (section 8).

What is genuinely absent, and where it is owned:

| Absent | Where it lands |
| --- | --- |
| **Field-level minimisation** — which columns each role sees, rather than which rows | [`field-visibility.md`](field-visibility.md), with [`../nfr/data-classification.md`](../nfr/data-classification.md) as its source. This document decides reach; that one decides detail |
| **A branch scope that is not about branches** | An organisation-scoped permission held by a principal with no branch assignments cannot satisfy `BranchScope.Organisation`, which is defined as cross-branch *reading reach*. The one case that exists is the vendor principal and `admin.feature_flags`, recorded in `tests/Tailor360.IntegrationTests/Authorization/matrix.yaml` with the reason and the two ways #25 could settle it. It is a recorded disagreement between this document and the software, and a test fails if a second one appears |
| **`transfer-pending` branch scope** | #37: a cross-branch transfer grants the destination branch exactly receive, reject and resolve while the transfer is pending. It is a fourth reach, and it has no permission rows here yet |
| **The foreign key on `users.home_branch_id`** | #25, which builds the screen that can create a user against the branch register. The column is a *default* and not a grant: it is what a new session opens onto, and reach is the branch-assignment rows alone, so removing somebody from a branch removes their reach into it whatever their home branch still says |
| **Branch reach for collections, searches and exports** | Nothing. `ScopedToResource` decides one identifier taken from the route table, so a list, a search, an export, a bulk command, or an identifier carried in a body or a query string reaches its handler with no branch decision taken at all — there is no scoped-query port and no global query filter behind it. #32a, the first issue to publish such an endpoint, builds the collection half and adds the matrix dimensions for it. Recorded here and in `matrix.yaml` so that nobody writes the first list endpoint believing the question is answered |
| **Media and download authorisation** | #28 and #29. The rule that "media and download endpoints re-check authorisation on every request, and no object is ever given a URL" has nothing to attach to yet: there is no media endpoint, no download endpoint and no object-storage code. The one artefact today is the `referenceImages` field of the job-card view, gated on `media.read` in [`field-visibility.md`](field-visibility.md), which is a rule about a column and not about delivery |
| **An authorisation threat model** | #32a, with the first permissioned route. Two controls are already decided and tested and belong in it when it is written: a foreign resource and a missing one are both answered `404 security.resource-not-found`, and the resource pipeline fails closed — a host that omits `UseTailor360ResourceScope()` refuses every request rather than skipping the check |

## 8. How this document is enforced

The tables above are the fixture, not a description of one. `PermissionMatrixDocument` reads only the text between
the paired `<!-- matrix:… -->` markers, so the prose around them can say anything without affecting the tests, and
[`PermissionMatrixDocumentTests`](../../tests/Tailor360.UnitTests/Security/PermissionMatrixDocumentTests.cs)
asserts:

| # | Assertion | The drift it catches |
| --- | --- | --- |
| 1 | Every catalogued permission has exactly one row in section 4 | A permission added in code and never approved |
| 2 | Every section 4 row names a catalogued permission | An approved permission nobody implemented, and a typo in either direction |
| 3 | Module, scope and the three flags in section 4 equal the code, permission by permission | The approval and the enforcement saying different things — the failure this whole design exists to prevent |
| 4 | Every role key in a `Granted to` cell exists in section 3 | A grant to a role that does not exist |
| 5 | Section 3 equals the seeded role register: keys, names, reach, onboarding flag and grant counts | A role edited in one place and not the other |
| 6 | The `Granted to` column equals the default grants `init-reference-data` writes | The seed and the approval diverging silently |
| 7 | Every declared role receives at least one permission, and every permission is granted to at least one role | A role or a permission that does nothing |
| 8 | An organisation-scoped permission is granted only to an organisation-reach role | A branch role given an organisation-wide power |
| 9 | The parser refuses a malformed table, and every "every X" assertion is guarded by a non-empty check | The vacuous pass: a mangled table yielding zero rows and satisfying every rule about all of them |

### 8.1 Section 5 against the routes the server publishes

[`AuthorisationMatrixTests`](../../tests/Tailor360.IntegrationTests/Authorization/AuthorisationMatrixTests.cs)
reads the composed route table — what the server will actually serve, not what the source appears to say — and
reconciles it with the endpoint block:

| # | Assertion | The drift it catches |
| --- | --- | --- |
| 10 | Every published route has exactly one row in section 5 | **The failure this whole design exists to prevent.** A route added without a row is a route whose exposure nobody agreed to, and nothing else in the system would ever mention it |
| 11 | Every section 5 row names a published route | An approval outliving the route it was written for, which then hides the route that replaced it |
| 12 | The declaration, permission, branch scope, resource and audit action in section 5 equal the endpoint's own metadata | An endpoint quietly swapped to a laxer permission or a wider reach while its approved row still says otherwise |
| 13 | Every permission a route demands is in the catalogue and has a row in section 4 | A typo, which closes the route to everybody rather than opening it — silently, until somebody is refused at a counter |
| 14 | **ARCH-018**: a route demanding a step-up permission declares `.RequireStepUp()`, and one that declares it demands a step-up permission | A demand for fresh re-authentication that is invisible to every reader of the route, and a demand attached to one route rather than to the action |
| 15 | No route is unclassified, and only the `/health/` probes are exempt from declaring a policy | A route the inventory cannot read, which would otherwise be absent from both sides of the comparison and pass by not being there |

Two further rules run in the contract tier, over the same composed route table, because both are about how a route
is *declared* rather than about whether the declaration matches this document:

| # | Assertion | The drift it catches |
| --- | --- | --- |
| 15a | **ARCH-022**: no route declares both a permission and a justified anonymous exposure | `AllowAnonymous` wins at run time, so such a route is open while its source — and its row here — say it is permission-gated. The inventory also classifies anonymous-first, so even an exempted one would be listed honestly |
| 15b | **ARCH-023**: a route demanding a branch-scoped permission and carrying a route parameter declares `ScopedToResource`, or says with `TouchesNoBranchOwnedResource` why what it names belongs to no branch | Omitting the resource declaration does not fail — it silently removes the branch check, and the row here still reconciles because a blank `Resource` cell agrees with an endpoint that declares none. That is a tailor in one branch reading another branch's order by editing the identifier |

Each of these has a negative control that feeds the same reconciler a deliberately wrong input and asserts it
complains, because every assertion above is of the form "these two collections agree" and two collections agree
vacuously when one of them is empty.

### 8.2 The grants against the answers the application gives

[`RoleMatrixTests`](../../tests/Tailor360.IntegrationTests/Authorization/RoleMatrixTests.cs) asks for every
permission as every role, through a real route table, a real cookie and a real session rebuilt from a real
database. The expected answers are not written into the test: which roles hold what is read from the seeded role
register, which the unit tier holds equal to section 4, and what each class of caller is owed is read from
[`matrix.yaml`](../../tests/Tailor360.IntegrationTests/Authorization/matrix.yaml). Moving a grant in this document
therefore moves an HTTP outcome.

| # | Assertion | What it is really asking |
| --- | --- | --- |
| 16 | Each role is answered in its own branch exactly as section 4 approves — 1,248 requests, one per permission per role | If a person in this role opens this, what happens? |
| 17 | No branch-scoped permission reaches another branch, for any role | Holding it in Coimbatore is not holding it in Chennai |
| 18 | An organisation-scoped permission is not narrowed by the branch named | Configuring the organisation is not a branch action |
| 19 | A branch-scoped permission declared organisation-wide on a read is served across branches only to a holder of `admin.organisation.read_all_branches` | The cross-branch search of [`../prd/workflows/branch-scenarios.md`](../prd/workflows/branch-scenarios.md) section 3.3 — reach is a permission, not seniority and not holding several branches |
| 20 | An identifier matching nothing is answered exactly as another branch's is | Editing the address bar tells the person doing it nothing |
| 21 | Nothing in the catalogue is reachable without a session | Deny by default, stated as an outcome rather than as an absence |
| 22 | A flagged permission is refused to a session that has not satisfied a second factor; a step-up permission is refused to one whose last strong factor is older than the window and allowed to one inside it | The two flags in section 4, measured rather than described |
| 23 | The recorded disagreements between approval and enforcement are exactly the ones that exist | An exception list that grows silently, or one nobody prunes after the problem is fixed |
| 24 | A person assigned to two branches, working in the first, is refused a row in the second on a `current-branch` route and served it on an `assigned-branches` one | That the two branch reaches are two things. Every other caller in the suite is assigned to exactly one branch, so for them the active branch and the assigned set are the same set and both declarations answer alike however the handler is written — including when it ignores the declaration altogether. A `current-branch` row in section 5 that silently meant "any branch you are assigned to" would be approving something wider than it says, and nothing else here could see it |

Refusals that change something are themselves recorded: `authz.denied`, coalesced per actor, endpoint and minute
and never sampled, asserted by
[`DenialAuditTests`](../../tests/Tailor360.IntegrationTests/Authorization/DenialAuditTests.cs).

---

## 9. Changing the matrix

| Change | Route |
| --- | --- |
| Add a permission | Declare it in its module's group in `src/Platform/Tailor360.Platform.Security/Permissions/`, add its row here, grant it to at least one role in `SystemRoles`. The tests fail until all three agree |
| Change a flag | Change it in code and here in the same pull request. It is an owner-visible change: a flag is what a session has to prove, and the owner approved the current set |
| Change a default grant | Change `SystemRoles` and the `Granted to` cell together, and run `init-reference-data` to reconcile existing databases |
| Change a grant on a live installation | An administrator holding `admin.roles` edits it (#25). This document keeps recording the default a new installation starts with, not the state of any particular database |
| Add a custom role | An administrator creates it. It is not a system role, it is not in this document, and it is subject to the same enrolment rule: granting it a flagged permission forces its holders to enrol a second factor |
| Retire a permission | It is not deleted from the catalogue while any role or audit entry names it; it is removed from every grant first, then from the catalogue in a later release |
| Add an endpoint | Add its row to section 5 in the pull request that maps it. Until you do, `AuthorisationMatrixTests` fails and names your route. If it demands a branch-scoped permission and carries an identifier in its path, declare `ScopedToResource` too — without it nothing loads the row's branch and the branch check passes for every signed-in caller (ARCH-023 fails the build, and the `Resource` cell here is what records the decision). If the identifier is one a caller could type rather than follow, add it to `identifier-editing` in [`matrix.yaml`](../../tests/Tailor360.IntegrationTests/Authorization/matrix.yaml) as well — item 2 of [`../process/definition-of-done.md`](../process/definition-of-done.md) |
| Change what an endpoint demands | Change the endpoint and its row in section 5 together. A route that demands a different permission, a wider reach or a different audit action from the one approved is a failure, not a discrepancy to be tidied later |
