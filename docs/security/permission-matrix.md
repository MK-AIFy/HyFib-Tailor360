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
| `owner` | Owner | organisation | yes | 51 | Approves the catalogue, prices, tax configuration, the permission matrix and alert policies; approves dispatch exceptions; reads across every branch. |
| `admin` | Admin | organisation | yes | 22 | Administers users, branches, roles, templates and integrations across the organisation. Holds no shop-floor permission and cannot publish the catalogue or change a price. |
| `branch_manager` | Branch Manager | branch | yes | 77 | Supervises the day in the assigned branches: exception queues, holds, reschedules, reconciliation cases, stocktake and variance approvals, service recovery. |
| `reception` | Reception | branch | yes | 25 | The counter: finds or creates the customer, records consent, captures measurements and images, builds and confirms the order, prints labels and takes the advance. |
| `measurement_staff` | Measurement Staff | branch | no | 7 | The measurement bundle on its own, for a shop that staffs the measurements queue separately from the counter. Assigned to nobody by default: the same permissions are granted to Reception, so either reading of OD-13 works without a change here. |
| `tailor_master` | Tailor Master | branch | yes | 21 | The workshop lead: starts production, pins the workflow version, assigns garment jobs, runs the workboard, records quality results and decides rework. |
| `tailor` | Tailor | branch | yes | 9 | The stitching role: takes custody by scan, starts and completes phases, records material issue, consumption, return and wastage. |
| `inventory_clerk` | Inventory Clerk | branch | yes | 11 | The store room: items, suppliers, locations and reorder rules, purchase receipts, low-stock response, stocktakes and variance explanations. |
| `cashier` | Cashier | branch | yes | 17 | The money: invoices, payments, allocations, receipts, and the cashier session with its denomination count and reconciliation. |
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
| `catalog.read` | Catalog | branch | no | no | no | `owner`, `admin`, `branch_manager`, `reception`, `measurement_staff`, `tailor_master`, `tailor`, `cashier` | #29 | Everybody who takes, measures, works or prices an order has to know what the shop offers. Branch-scoped rather than organisation-scoped because the answer is: a category is offered at a set of branches and a sub-category at a subset of its parent's (`category-hierarchy.md` section 8), so "what may be ordered" is a different list at each counter. Neither flagged nor reasoned — the catalogue is the shop's own service list and holds no personal data |
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
| `customers.read_consent` | Customers | branch | no | no | no | `owner`, `branch_manager`, `reception`, `auditor` | #26 | Which purposes were agreed decides which messages may be sent at all. Covers the communication preferences too — channels, language and quiet hours — because [`../nfr/data-classification.md`](../nfr/data-classification.md) section 5.3 is one inventory row over both and names one set of people who may see it |
| `customers.read_contact` | Customers | branch | no | no | no | `owner`, `branch_manager`, `reception`, `cashier`, `delivery_staff`, `auditor` | #26 | Split from `customers.read` because `data-classification.md` classifies contact separately and delivery is the only shop-floor role that needs it |
| `customers.read_notes` | Customers | branch | no | no | no | `owner`, `branch_manager` | #26 | Free text about a person, so it stays with the counter's supervisor and the Owner |
| `customers.request_deletion` | Customers | organisation | yes | yes | yes | `owner`, `admin` | #26 | Starts erasure, which cannot be undone once the retention job has run |
| `customers.restrict` | Customers | organisation | yes | no | yes | `owner`, `admin` | #26 | Restriction stops messages and analytics for that person and must survive a later edit |
| `customers.update` | Customers | branch | no | no | yes | `branch_manager`, `reception` | #26 | A correction to a customer record carries a reason so the timeline reads as a history rather than a mystery. The same permission records a consent answer and sets the communication preference, and those carry no reason: the reason an answer exists is that the customer gave it, and the trail already carries the purpose, the outcome and the wording version |
| `measurements.capture` | Customers | branch | no | no | no | `branch_manager`, `reception`, `measurement_staff` | #28 | Granted to Reception **and** to Measurement Staff, so either reading of OD-13 works without a change here |
| `measurements.read_sheet` | Customers | branch | no | no | no | `owner`, `branch_manager`, `reception`, `measurement_staff`, `tailor_master`, `tailor` | #28 | A measurement sheet is sensitive personal data; the read is audited explicitly (`data-classification.md` DC-06) |
| `admin.branches` | Identity | organisation | yes | yes | yes | `owner`, `admin` | #25 | A branch's timezone and calendar move every due date and report cut-off in it |
| `admin.roles` | Identity | organisation | yes | yes | yes | `owner`, `admin` | #25 | Editing a grant edits this document's meaning, so it is the one action that must never be possible from a merely-remembered session. It is bounded twice over: a grant may only pass on authority the granter already holds — read from the database, not from their session, so a permission taken away since sign-in cannot still be given away — and section 4's reach rule is checked on every edit, not only against the seeded register |
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
| `admin.feature_flags` | Platform | not-branch-owned | yes | yes | yes | `owner`, `hyfib_super_user` | #25 | Owner and the vendor principal only, with a mandatory reason and an evaluation audit (`00-overview.md` section 2.1, plan Section 4.4). **Not branch-owned**, not organisation-scoped: a flag reaches no branch's data, so demanding organisation-wide reach for it would not make it safer — it made the one role approved to change a flag unable to reach the endpoint that changes it. #25 settled that recorded gap by adding the third scope rather than granting the vendor principal reach over every branch |
| `admin.health.read` | Platform | organisation | no | no | no | `owner`, `admin` | #58 | The detailed health report carries backup age and provider state (`raci.md` row 28) |
| `admin.organisation.read_all_branches` | Platform | organisation | yes | no | no | `owner`, `admin`, `auditor` | #24 | For a person, reach and never authority: it satisfies an organisation-wide read and grants no write outside the branch their session is working in (`branch-scenarios.md` section 3.3). A background job declaring organisation branch scope carries the same key and additionally acts in every branch by declaration, which is a different thing and is bounded by its `[WorkerJob]` permissions |
| `admin.outbox.replay` | Platform | organisation | yes | yes | yes | `owner`, `admin` | #25 | A replay can duplicate an outbound effect — a second message to a customer, a second push to an accounting system — so it carries a reason and, from #25, step-up. The catalogue said step-up was not required while the operator path was the console, where the shell session is the authentication; opening the endpoint made the flag disagree with `outbox.md` line 76 and `failure-modes.md` line 136, which had both promised step-up all along, and the flag is what was wrong |
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

The table holds three kinds of route. The administrative endpoints of #25 and the customer endpoints of #26
demand a permission from section 4. The authentication and session endpoints of #23 are self-service or
anonymous, so their `Permission` column is empty and the `Declaration` cell carries the whole rule. The host's
own routes — the shell fallback, the health probes, the version handshake — are the rest. Every business
endpoint fills in a row here in the same pull request that maps it.

What a row does **not** say is which columns come back. The six customer routes that answer with a record —
`POST /customers`, `GET`, `PUT` and `POST .../open` on one customer, the two status commands, and the merge —
all project their body through the approved response view `customers.record`, and the two that answer with search
cards project through `customers.search_card`. Which fields each caller is shown is
[`field-visibility.md`](field-visibility.md)'s decision, not this one; the `GET` row spells it out once because it
is the route a reader looks at first.

<!-- matrix:endpoints -->
| Method | Route | Declaration | Permission | Branch scope | Resource | Audited as | What it is for |
| --- | --- | --- | --- | --- | --- | --- | --- |
| `POST` | `/api/v1/admin/users/` | permission | `admin.users` | organisation | — | `identity.user.invited` | Invites somebody: creates an account that cannot yet sign in and sends a single-use link that lets them set their own first password. No `If-Match`, because there is nothing yet to have changed |
| `GET` | `/api/v1/admin/audit/` | permission | `admin.audit.read` | organisation | — | — | Reads the audit trail, filtered by subject, actor, action prefix and time, paged by keyset on the sequence. No step-up: the permission is catalogued as demanding a second factor and not a recent re-authentication, and an auditor reading the trail changes nothing |
| `POST` | `/api/v1/admin/audit/export` | permission | `audit.export` | organisation | — | `platform.audit.exported` | Exports a slice of the trail. Audited although it changes nothing, because taking a copy out of the system *is* the act: it is how the record of what everybody did leaves the building |
| `GET` | `/api/v1/admin/branches/` | permission | `admin.branches` | organisation | — | — | Lists the organisation's branches |
| `POST` | `/api/v1/admin/branches/` | permission | `admin.branches` | organisation | — | `identity.branch.opened` | Opens a branch. The code is set once here and never again: it is embedded in every order, estimate and invoice number the branch produces |
| `GET` | `/api/v1/admin/branches/{branchId}` | permission | `admin.branches` | organisation | — | — | Reads one branch with the version an edit must be made against |
| `PUT` | `/api/v1/admin/branches/{branchId}` | permission | `admin.branches` | organisation | — | `identity.branch.reconfigured` | Changes a branch's name, timezone, address and contacts. The timezone is checked against the running system, because a typo there quietly computes every due date for that branch somewhere else |
| `POST` | `/api/v1/admin/branches/{branchId}/close` | permission | `admin.branches` | organisation | — | `identity.branch.closed` | Closes a branch. Refused while any account is assigned to it or calls it home, and answered with a count rather than names. Nothing is deleted |
| `POST` | `/api/v1/admin/branches/{branchId}/reopen` | permission | `admin.branches` | organisation | — | `identity.branch.reopened` | Reopens a closed branch |
| `GET` | `/api/v1/admin/feature-flags/` | permission | `admin.feature_flags` | not-branch-owned | — | — | Lists the configured flags and module toggles |
| `GET` | `/api/v1/admin/feature-flags/{key}` | permission | `admin.feature_flags` | not-branch-owned | — | — | Reads one flag with the version an edit must be made against |
| `PUT` | `/api/v1/admin/feature-flags/modules/{code}` | permission | `admin.feature_flags` | not-branch-owned | — | `platform.module.toggled` | Switches a module and its menu entries on or off. The toggle is a flag under a reserved `module.` prefix, not a second store |
| `PUT` | `/api/v1/admin/feature-flags/{key}` | permission | `admin.feature_flags` | not-branch-owned | — | `platform.feature-flag.set` | Turns a flag on or off for the organisation. `If-Match` is required once the flag exists and omitted when configuring it for the first time, because there is then no version to have changed |
| `GET` | `/api/v1/admin/outbox/dead-letters` | permission | `admin.outbox.replay` | organisation | — | — | Lists the outbox messages that exhausted their delivery attempts, oldest failure first, each with the error it was given up on. Step-up, because it is the reading half of an operation whose permission demands it and the catalogue's flag is per permission, not per route |
| `POST` | `/api/v1/admin/outbox/{messageId}/replay` | permission | `admin.outbox.replay` | organisation | — | `platform.outbox.replayed` | Puts one dead-lettered message back on the queue. Step-up, a written reason and an `Idempotency-Key`; no `If-Match`, because a message carries no version and "already replayed" is answered as not-found rather than as a lost race. There is deliberately no drain-everything route: the command-line tool keeps that one |
| `GET` | `/api/v1/admin/permissions/` | permission | `admin.roles` | organisation | — | — | Lists every permission the application declares, with its module, scope and the three flags. Published because a screen offering checkboxes has to say what each one allows and which carry a second factor, a step-up or a reason — otherwise an administrator is picking from a list of dotted keys and guessing |
| `GET` | `/api/v1/admin/roles/` | permission | `admin.roles` | organisation | — | — | Lists the organisation's roles with their grants and the number of accounts holding each |
| `POST` | `/api/v1/admin/roles/` | permission | `admin.roles` | organisation | — | `identity.role.defined` | Defines a custom role, which starts granting nothing. Grants go through the permissions route, which is where the four invariants are checked; letting creation carry a set would have meant either duplicating them or having a way in that skipped them. No `If-Match`, because nothing exists yet to have changed |
| `GET` | `/api/v1/admin/roles/{roleId}` | permission | `admin.roles` | organisation | — | — | Reads one role with the version an edit must be made against |
| `PUT` | `/api/v1/admin/roles/{roleId}` | permission | `admin.roles` | organisation | — | `identity.role.described` | Renames a role and rewrites its description. Neither the key nor the reach is accepted: the key is what this document, the seed data and the tests name a role by, and changing the reach would silently invalidate grants already approved under the old one |
| `PUT` | `/api/v1/admin/roles/{roleId}/permissions` | permission | `admin.roles` | organisation | — | `identity.role.permissions-replaced` | Replaces what a role grants with exactly the set sent. Four refusals: a key the catalogue does not declare; an organisation-scoped permission on a branch-reach role, which is the runtime half of section 4's rule; a grant of a permission the administrator does not themselves hold, read from the database rather than from their session; and a change that would leave nobody able to edit roles |
| `POST` | `/api/v1/admin/roles/{roleId}/delete` | permission | `admin.roles` | organisation | — | `identity.role.deleted` | Deletes a custom role that nobody holds. A system role is refused, and so is one somebody still holds, answered with a count rather than names. `POST` to a `/delete` segment rather than the `DELETE` verb because the operation carries a body: it demands a written reason, and a reason sent as a query parameter would end up in every proxy's access log |
| `GET` | `/api/v1/admin/users/` | permission | `admin.users` | organisation | — | — | Lists staff accounts, filtered by status, role and branch and paged by keyset. The free-text term matches names only: matching an address would answer "is this email a member of staff" for anyone who reached the endpoint, and no address is returned either |
| `GET` | `/api/v1/admin/users/{userId}` | permission | `admin.users` | organisation | — | — | Reads one staff account for an administration screen, with the version an edit must be made against. Not audited: it discloses no more than the list the same administrator may already read, and auditing every read would bury the changes in the trail |
| `POST` | `/api/v1/admin/users/{userId}/suspend` | permission | `admin.users` | organisation | — | `identity.user.suspended` | Suspends an account and ends its live sessions in the same request. Step-up, a written reason, `If-Match` against the version the administrator saw, and an `Idempotency-Key` so a retried request does not suspend twice |
| `POST` | `/api/v1/admin/users/{userId}/deactivate` | permission | `admin.users` | organisation | — | `identity.user.deactivated` | Closes an account for good, ending its sessions and its remembered devices. Nothing is deleted: every order, invoice and audit entry that names the account must stay resolvable |
| `POST` | `/api/v1/admin/users/{userId}/reactivate` | permission | `admin.users` | organisation | — | `identity.user.reactivated` | Reopens a closed account as an invitation. The password and every second factor are cleared, so the person returning proves who they are from the beginning |
| `POST` | `/api/v1/admin/users/{userId}/reinstate` | permission | `admin.users` | organisation | — | `identity.user.reinstated` | Lifts a suspension. The account may sign in again and its failed-attempt count is cleared |
| `POST` | `/api/v1/admin/users/{userId}/reset-mfa` | permission | `admin.users` | organisation | — | `identity.user.mfa-reset` | Clears the second factor after the holder has been identified out of band, and ends their sessions — the account must not stay signed in on a device the holder cannot see |
| `POST` | `/api/v1/admin/users/{userId}/revoke-sessions` | permission | `admin.users` | organisation | — | `identity.user.sessions-revoked` | Ends every session the account holds without changing its standing. The emergency lever for a device left somewhere |
| `GET` | `/api/v1/admin/users/{userId}/access` | permission | `admin.users` | organisation | — | — | Reads the roles a staff account holds and the branches it works in, with the version an edit must be made against |
| `PUT` | `/api/v1/admin/users/{userId}/branches` | permission | `admin.users` | organisation | — | `identity.user.branches-replaced` | Replaces the branches the account works in. The whole set, not a list of additions, so the audit entry reads as a state rather than as a difference |
| `PUT` | `/api/v1/admin/users/{userId}/roles` | permission | `admin.users` | organisation | — | `identity.user.roles-replaced` | Replaces the roles the account holds. Refused when it would leave nobody able to administer accounts, because the way back from that is a database edit |
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
| `GET` | `/api/v1/billing/gst-registrations` | permission | `billing.manage_price_lists` | organisation | — | — | Lists the organisation's GST registrations, by branch and first day (#41, #145). The GSTIN is business configuration printed on every invoice, not personal data |
| `POST` | `/api/v1/billing/gst-registrations` | permission | `billing.manage_price_lists` | organisation | — | `billing.gst_registration.added` | Records a branch's GST registration. The GSTIN is checked for shape and check character, never against the tax portal; at most one registration of a branch is in force on any day, refused by a read and settled by an exclusion constraint |
| `GET` | `/api/v1/billing/gst-registrations/{registrationId}` | permission | `billing.manage_price_lists` | organisation | — | — | Reads one registration; the `ETag` is the token an amendment is made against |
| `PUT` | `/api/v1/billing/gst-registrations/{registrationId}` | permission | `billing.manage_price_lists` | organisation | — | `billing.gst_registration.changed` | Amends a registration's dates, names and number. The branch never changes and a registration is never deleted: invoices were issued under it |
| `GET` | `/api/v1/billing/invoices` | permission | `billing.create_invoice` | current-branch | — | — | The caller's branch's invoices, newest first, by cursor, filtered by status. No read permission exists for an invoice in the catalogue; a cashier who may draft one may read one (data classification 5.10) |
| `POST` | `/api/v1/billing/invoices` | permission | `billing.create_invoice` | current-branch | — | `billing.invoice.drafted` | Drafts an invoice from an order's stored calculation: the order confirmed, not cancelled and taken at the caller's branch; the calculation reproducing on the versions it names; no live invoice already charging for its garment jobs. Nothing is re-priced |
| `GET` | `/api/v1/billing/invoices/{invoiceId}` | permission | `billing.create_invoice` | current-branch | `billing.invoice` | — | Reads an invoice with its lines and the tag a change sends back as `If-Match`; scoped to the invoice's own branch |
| `PUT` | `/api/v1/billing/invoices/{invoiceId}` | permission | `billing.update_invoice` | current-branch | `billing.invoice` | `billing.invoice.updated` | Replaces a draft's lines by pricing them afresh under a reference of the draft's own; an override or a discount beyond its threshold needs `billing.override_price` on a recently re-authenticated session, as everywhere |
| `POST` | `/api/v1/billing/invoices/{invoiceId}/discard` | permission | `billing.update_invoice` | current-branch | `billing.invoice` | `billing.invoice.discarded` | Abandons a draft with a reason, freeing its garment jobs for another draft; a posted invoice is never discarded |
| `POST` | `/api/v1/billing/invoices/{invoiceId}/post` | permission | `billing.post_invoice` | current-branch | `billing.invoice` | `billing.invoice.posted` | Posts a draft: the figures recomputed against the stored calculation, the number drawn under the sequence lock in the transaction that freezes the row, the `I-` barcode minted, `InvoicePosted` on the outbox. Idempotent; `If-Match` |
| `POST` | `/api/v1/billing/invoices/{invoiceId}/cancel` | permission | `billing.cancel_invoice` | current-branch | `billing.invoice` | `billing.invoice.cancelled` | Cancels a posted invoice by appending its record and posting a credit note for the whole amount; the number and totals stand, and the garment jobs are freed for another invoice. A reason is required; the permission carries step-up (SQ-05, as the catalogue says) and the route declares it. Within the cancellation window where one is configured (OD-23). No `If-Match`: the invoice's row does not move, so it is locked and re-read in the transaction |
| `POST` | `/api/v1/billing/invoices/{invoiceId}/credit-notes` | permission | `billing.post_credit_note` | current-branch | `billing.invoice` | `billing.credit_note.posted` | Posts a credit note per line at the line's own rates; a line is relieved at most to what it still carries. Numbered from its own sequence; immutable. No `If-Match`, for the reason the cancel route gives |
| `POST` | `/api/v1/billing/invoices/{invoiceId}/debit-notes` | permission | `billing.post_credit_note` | current-branch | `billing.invoice` | `billing.debit_note.posted` | Posts a debit note per line, adding to what the customer owes; the same permission as a credit note, both being the compensating documents a posted invoice is corrected by |
| `GET` | `/api/v1/billing/invoices/{invoiceId}/document` | permission | `billing.create_invoice` | current-branch | `billing.invoice` | `billing.invoice.downloaded` | Streams the rendered invoice as the worker stored it; no URL to the object exists. A sensitive read, audited by the handler against the invoice (`CLAUDE.md` section 4 rule 9) before the first byte is streamed, so an aborted transfer is still on record. Not available until rendered |
| `GET` | `/api/v1/billing/invoices/{invoiceId}/notes/{noteId}/document` | permission | `billing.create_invoice` | current-branch | `billing.invoice` | `billing.invoice.downloaded` | Streams a rendered credit or debit note, reached and audited through its invoice |
| `POST` | `/api/v1/billing/invoices/{invoiceId}/print` | permission | `billing.create_invoice` | current-branch | `billing.invoice` | `billing.invoice.printed` | Sends the rendered invoice to the branch's print queue through `IPrintQueue`, one to five copies; answered 202 with the job's identifier, and audited. Acknowledged only: the interim adapter (ADR-0014) logs the job, and no printer is reached until #55's bridge replaces it |
| `GET` | `/api/v1/billing/barcodes/{payload}` | permission | `billing.create_invoice` | current-branch | — | — | Resolves an `I-` payload to the invoice it was printed on, for the branch that issued it; another branch's, another organisation's, a failed check character and nothing at all read alike as 404, confirming the existence of nothing. No resource scope: the payload is the lookup, and the branch is checked against the caller's in the handler |
| `GET` | `/api/v1/billing/payment-modes` | permission | `billing.manage_price_lists` | organisation | — | — | Lists the organisation's payment modes, active or not: configuration of the same kind as the tax configuration and the price lists, read by the same people |
| `GET` | `/api/v1/billing/payment-modes/{paymentModeId}` | permission | `billing.manage_price_lists` | organisation | — | — | Reads one payment mode with the ETag a change is made against; the list carries none |
| `PUT` | `/api/v1/billing/payment-modes/{paymentModeId}` | permission | `billing.manage_price_lists` | organisation | — | `billing.payment_mode.changed` | Changes a mode's name, flags, branch restriction or active state against its ETag; the code never changes and a recorded payment keeps its mode. Idempotent; `If-Match` |
| `GET` | `/api/v1/billing/cashier-sessions` | permission | `payments.session` | current-branch | — | — | Lists the caller's branch's sessions, newest first, optionally one status; `status=open` is how a cashier finds the session they have open. No resource scope: the branch is the caller's own |
| `POST` | `/api/v1/billing/cashier-sessions` | permission | `payments.session` | current-branch | — | `payments.open_session` | Opens a session at the caller's branch with the float put in the drawer; a second open session for the cashier at the branch is refused by the index (INV-CSH-01). Idempotent. No resource scope: there is no resource until it is created |
| `GET` | `/api/v1/billing/cashier-sessions/{sessionId}` | permission | `payments.session` | current-branch | `billing.cashier_session` | — | Reads one session with its count sheet; another branch's reads as 404 |
| `POST` | `/api/v1/billing/cashier-sessions/{sessionId}/close` | permission | `payments.session` | current-branch | `billing.cashier_session` | `payments.close_session` | Closes the session against the denomination sheet and the counted totals by mode, only by the cashier who opened it; a variance beyond the threshold needs a reason; the close is a conditional update, so a second close is a 409 (INV-CSH-05). Idempotent; no `If-Match`, the row is the guard |
| `GET` | `/api/v1/billing/payment-modes/available` | permission | `payments.record` | current-branch | — | — | The active modes the caller's branch may take money in, with the code, the name and whether a reference is required; a mode through a provider is left off until one is configured (OD-03). No resource scope: the branch is the caller's own |
| `POST` | `/api/v1/billing/payments` | permission | `payments.record` | current-branch | — | `payments.record` | Records a payment against an order in the caller's open cashier session and allocates it at once, oldest posted invoice first (INV-PAY-04), the rest held as an advance (INV-PAY-05); refused without an open session (INV-CSH-02), in a mode the branch does not take, without the reference the mode requires, or with a reference that reads as a card number. Idempotent, and the mode-and-reference and the cashier-and-key are unique on the row. No resource scope: the payment is created at the caller's own branch, against an order the handler checks is the branch's |
| `GET` | `/api/v1/billing/payments/{paymentId}` | permission | `payments.record` | current-branch | `billing.payment` | — | Reads one payment with its allocations and what of it is still held; another branch's reads as 404 |
| `POST` | `/api/v1/billing/payments/{paymentId}/allocations` | permission | `payments.allocate_manual` | current-branch | `billing.payment` | `payments.allocate_manual` | Applies part of a payment's held advance to a posted invoice of the same order by hand, against the automatic rule: step-up and a reason (`raci.md` row 5); never more than is held or than the invoice owes. Idempotent |
| `GET` | `/api/v1/billing/orders/{orderId}/balance` | permission | `payments.record` | current-branch | `billing.order` | — | What the order still owes across its posted invoices and what is held against it, computed from rows on every read (INV-PAY-06); the resource is Billing's own fact of the order, so an order at another branch, or one Billing has not heard of, reads as 404 |
| `POST` | `/api/v1/billing/pricing/preview` | permission | `billing.manage_price_lists` | organisation | — | `billing.pricing.previewed` | Runs the pricing engine against a named price-list version, draft or published, and answers the result without storing it; an override beyond the version's threshold or a discount beyond its rule's counter maximum is refused unless the caller also holds `billing.override_price` |
| `GET` | `/api/v1/billing/price-lists` | permission | `billing.manage_price_lists` | organisation | — | — | Lists the organisation's price lists, by code (#41, #146). Price-list structure is Internal; the rates inside a version are Confidential, which is why every route here is on the administration permission |
| `POST` | `/api/v1/billing/price-lists` | permission | `billing.manage_price_lists` | organisation | — | `billing.price_list.created` | Creates a price list. Most shops have one; a shop pricing branches differently has one per group of branches |
| `GET` | `/api/v1/billing/price-lists/versions/{versionId}` | permission | `billing.manage_price_lists` | organisation | — | — | Reads one version and everything in it, whatever its status. The `ETag` is the token every change is made against |
| `PUT` | `/api/v1/billing/price-lists/versions/{versionId}` | permission | `billing.manage_price_lists` | organisation | — | `billing.price_list_version.changed` | Changes a draft's name, notes, effective date, conventions and branches, whole-value |
| `POST` | `/api/v1/billing/price-lists/versions/{versionId}/discount-rules` | permission | `billing.manage_price_lists` | organisation | — | `billing.discount_rule.added` | Adds a discount rule — what kind, how much on the counter's own authority, how much with approval — to a draft |
| `PUT` | `/api/v1/billing/price-lists/versions/{versionId}/discount-rules/{ruleId}` | permission | `billing.manage_price_lists` | organisation | — | `billing.discount_rule.changed` | Replaces what a draft says about a discount rule, whole-value |
| `POST` | `/api/v1/billing/price-lists/versions/{versionId}/discount-rules/{ruleId}/delete` | permission | `billing.manage_price_lists` | organisation | — | `billing.discount_rule.removed` | Removes a discount rule from a draft. A `POST` sub-resource, so it carries a reason and a retry key |
| `POST` | `/api/v1/billing/price-lists/versions/{versionId}/items` | permission | `billing.manage_price_lists` | organisation | — | `billing.price_list_item.added` | Adds an item — a service's base charge, a surcharge or a material, with its tax code — to a draft. The item's code is what the catalogue's link 4 names |
| `PUT` | `/api/v1/billing/price-lists/versions/{versionId}/items/{itemId}` | permission | `billing.manage_price_lists` | organisation | — | `billing.price_list_item.changed` | Replaces what a draft says about an item, whole-value |
| `POST` | `/api/v1/billing/price-lists/versions/{versionId}/items/{itemId}/delete` | permission | `billing.manage_price_lists` | organisation | — | `billing.price_list_item.removed` | Removes an item from a draft |
| `POST` | `/api/v1/billing/price-lists/versions/{versionId}/publish` | permission | `billing.publish_price_list` | organisation | — | `billing.price_list_version.published` | Publishes a draft, retiring the list's published version in the same transaction; refused while a check fails — an item naming a tax code nobody published, a branch another list already prices. Step-up and a reason, because publication changes what every future order is quoted at (`raci.md` row 24) |
| `GET` | `/api/v1/billing/price-lists/versions/{versionId}/validation` | permission | `billing.manage_price_lists` | organisation | — | — | Runs the publication checks against a version and reports what they found |
| `GET` | `/api/v1/billing/price-lists/{priceListId}` | permission | `billing.manage_price_lists` | organisation | — | — | Reads one price list; the `ETag` is the token a rename is made against |
| `PUT` | `/api/v1/billing/price-lists/{priceListId}` | permission | `billing.manage_price_lists` | organisation | — | `billing.price_list.renamed` | Renames a price list. Its code never changes: seeds and exports refer to it |
| `GET` | `/api/v1/billing/price-lists/{priceListId}/versions` | permission | `billing.manage_price_lists` | organisation | — | — | Lists a price list's versions, newest first. Summaries only |
| `POST` | `/api/v1/billing/price-lists/{priceListId}/versions` | permission | `billing.manage_price_lists` | organisation | — | `billing.price_list_version.drafted` | Starts a draft version, empty or cloned from an existing version of the same list. Cloning the published version is the ordinary way to change a rate |
| `GET` | `/api/v1/billing/tax-configuration/versions` | permission | `billing.manage_price_lists` | organisation | — | — | Lists the organisation's tax configuration versions, newest first (#41, #145). Summaries only |
| `POST` | `/api/v1/billing/tax-configuration/versions` | permission | `billing.manage_price_lists` | organisation | — | `billing.tax_configuration.drafted` | Starts a draft tax configuration version, empty or cloned from an existing one. Cloning the published version is the ordinary way to change what is in force, because a published version is immutable |
| `GET` | `/api/v1/billing/tax-configuration/versions/{versionId}` | permission | `billing.manage_price_lists` | organisation | — | — | Reads one version and its codes, whatever its status. The `ETag` is the token every change is made against |
| `PUT` | `/api/v1/billing/tax-configuration/versions/{versionId}` | permission | `billing.manage_price_lists` | organisation | — | `billing.tax_configuration.changed` | Changes a draft's name, notes and effective date |
| `GET` | `/api/v1/billing/tax-configuration/versions/{versionId}/validation` | permission | `billing.manage_price_lists` | organisation | — | — | Runs the publication checks against a version and reports what they found: a CGST without its SGST, an IGST that is not their sum, a code re-spelled after invoices carried it. They encode no rate |
| `POST` | `/api/v1/billing/tax-configuration/versions/{versionId}/publish` | permission | `billing.publish_price_list` | organisation | — | `billing.tax_configuration.published` | Publishes a draft, retiring the version it supersedes in the same transaction; refused while a check fails, with every finding in the problem detail. Step-up and a reason, because every calculation from the effective date reads it |
| `POST` | `/api/v1/billing/tax-configuration/versions/{versionId}/tax-codes` | permission | `billing.manage_price_lists` | organisation | — | `billing.tax_code.added` | Adds a tax code — an HSN or SAC classification and the components it carries — to a draft |
| `PUT` | `/api/v1/billing/tax-configuration/versions/{versionId}/tax-codes/{taxCodeId}` | permission | `billing.manage_price_lists` | organisation | — | `billing.tax_code.changed` | Replaces what a draft says about a tax code, whole-value |
| `POST` | `/api/v1/billing/tax-configuration/versions/{versionId}/tax-codes/{taxCodeId}/delete` | permission | `billing.manage_price_lists` | organisation | — | `billing.tax_code.removed` | Removes a tax code from a draft. A `POST` sub-resource rather than a `DELETE`, as every removal in the catalogue is, so it carries a reason and a retry key |
| `GET` | `/api/v1/catalog/current` | permission | `catalog.read` | current-branch | — | — | Lists what the caller's branch may order today, from the published catalogue version. A grouping node's services, a category outside its active period, a branch that does not offer it, a category behind a flag that is off and a service published with a link missing are all absent, so intake never offers something the confirmation would refuse. The only catalogue route every operational role holds a permission for |
| `GET` | `/api/v1/catalog/versions` | permission | `catalog.edit` | organisation | — | — | Lists the organisation's catalogue versions, newest first. Summaries only: a list of twenty versions carrying twenty trees is a page nobody needed |
| `POST` | `/api/v1/catalog/versions` | permission | `catalog.edit` | organisation | — | `catalog.version.drafted` | Starts a draft, empty or cloned from an existing version. Cloning the published version is the ordinary way to change a published catalogue, because a published version is immutable |
| `GET` | `/api/v1/catalog/versions/{versionId}` | permission | `catalog.edit` | organisation | — | — | Reads one version and everything in it, whatever its status — a retired version reads exactly as it did, which is what makes a two-year-old job card render. The `ETag` is the token a publication is made against |
| `GET` | `/api/v1/catalog/versions/{versionId}/validation` | permission | `catalog.edit` | organisation | — | — | Runs every registered validator against a version and reports what they found, changing nothing. The same code path publication takes: a preview running different checks from the command it previews would be worse than no preview |
| `POST` | `/api/v1/catalog/versions/{versionId}/categories` | permission | `catalog.edit` | organisation | — | `catalog.category.added` | Adds a category to a draft. This is the route that makes "an administrator adds a category without a deployment" true |
| `PUT` | `/api/v1/catalog/versions/{versionId}/categories/{categoryId}` | permission | `catalog.edit` | organisation | — | `catalog.category.changed` | Replaces what a draft says about a category. Whole-value, so an omitted branch list means offered nowhere rather than unchanged; re-parenting that would make the hierarchy circular is refused |
| `POST` | `/api/v1/catalog/versions/{versionId}/categories/{categoryId}/delete` | permission | `catalog.edit` | organisation | — | `catalog.category.removed` | Removes a category, its sub-categories and their service types from a draft. A `POST` sub-resource rather than a `DELETE`, following the pattern the role administration already uses for a removal that carries a reason. Only from a draft: a database trigger refuses the delete on a published version as well |
| `POST` | `/api/v1/catalog/versions/{versionId}/categories/{categoryId}/service-types` | permission | `catalog.edit` | organisation | — | `catalog.service_type.added` | Adds a service type to a category in a draft. Its five links may all be null here; whether that is acceptable is decided at publication |
| `PUT` | `/api/v1/catalog/versions/{versionId}/service-types/{serviceTypeId}` | permission | `catalog.edit` | organisation | — | `catalog.service_type.changed` | Replaces what a draft says about a service type, its five links included |
| `POST` | `/api/v1/catalog/versions/{versionId}/service-types/{serviceTypeId}/delete` | permission | `catalog.edit` | organisation | — | `catalog.service_type.removed` | Removes a service type from a draft |
| `POST` | `/api/v1/catalog/versions/{versionId}/categories/{categoryId}/presentation` | permission | `catalog.publish` | organisation | — | `catalog.label_corrected` | Corrects the label, Tamil label, description or display order of a **published** category. The only edit a published version admits, and a publish-level act because the row it changes is one confirmed orders are pinned to. Nothing downstream reads a label — every price list, report and export refers to the code |
| `POST` | `/api/v1/catalog/versions/{versionId}/service-types/{serviceTypeId}/presentation` | permission | `catalog.publish` | organisation | — | `catalog.label_corrected` | The same correction for a published service type |
| `POST` | `/api/v1/catalog/versions/{versionId}/categories/{categoryId}/design-groups` | permission | `catalog.edit` | organisation | — | `catalog.design_group.added` | Adds a design option group to a category in a draft (#30, #137). A group belongs to one category of one version and is offered by the service types of that category that name it; its code is fixed once the version is published |
| `PUT` | `/api/v1/catalog/versions/{versionId}/design-groups/{designOptionGroupId}` | permission | `catalog.edit` | organisation | — | `catalog.design_group.changed` | Replaces what a draft says about a design option group, whole-value, its branch availability included |
| `POST` | `/api/v1/catalog/versions/{versionId}/design-groups/{designOptionGroupId}/delete` | permission | `catalog.edit` | organisation | — | `catalog.design_group.removed` | Removes a group, its options and every rule that reads it from a draft, and drops it from the service types that offered it. Only from a draft: a database trigger refuses the delete on a published version as well |
| `POST` | `/api/v1/catalog/versions/{versionId}/design-groups/{designOptionGroupId}/options` | permission | `catalog.edit` | organisation | — | `catalog.design_option.added` | Adds an option to a group in a draft. Help text and alternative text are required, because the picker is a picture first and the job card is read in monochrome |
| `PUT` | `/api/v1/catalog/versions/{versionId}/design-options/{designOptionId}` | permission | `catalog.edit` | organisation | — | `catalog.design_option.changed` | Replaces what a draft says about a design option |
| `POST` | `/api/v1/catalog/versions/{versionId}/design-options/{designOptionId}/delete` | permission | `catalog.edit` | organisation | — | `catalog.design_option.removed` | Removes an option from a draft. On a published version an option is retired in a new draft, never deleted |
| `POST` | `/api/v1/catalog/versions/{versionId}/categories/{categoryId}/design-rules` | permission | `catalog.edit` | organisation | — | `catalog.design_rule.added` | Adds a requires, excludes, requires-attachment or note rule to a category in a draft. The `DR-nn` number is allocated by the catalogue and never re-used; the operands read groups of the rule's own category |
| `PUT` | `/api/v1/catalog/versions/{versionId}/design-rules/{designRuleId}` | permission | `catalog.edit` | organisation | — | `catalog.design_rule.changed` | Replaces what a draft says about a rule. Its number and its category never change |
| `POST` | `/api/v1/catalog/versions/{versionId}/design-rules/{designRuleId}/delete` | permission | `catalog.edit` | organisation | — | `catalog.design_rule.removed` | Removes a rule from a draft. Its number is retired with it |
| `POST` | `/api/v1/catalog/versions/{versionId}/design-groups/{designOptionGroupId}/presentation` | permission | `catalog.publish` | organisation | — | `catalog.label_corrected` | Corrects the label, Tamil label or display order of a published design group, with a reason. The same one edit a published category admits, and for the same reason |
| `POST` | `/api/v1/catalog/versions/{versionId}/design-options/{designOptionId}/presentation` | permission | `catalog.publish` | organisation | — | `catalog.label_corrected` | Corrects the label, Tamil label, help text, alternative text or display order of a published design option, with a reason. The illustration is not correctable: the drawing the customer was shown is part of what they agreed to |
| `POST` | `/api/v1/catalog/versions/{versionId}/publish` | permission | `catalog.publish` | organisation | — | `catalog.version.published` | Publishes a draft, retiring the version it supersedes in the same transaction — with the retirement validators asked about the outgoing version and this one named as its successor. Two administrators publishing different drafts at once is settled by a partial unique index: one commits and the other is answered `409` |
| `POST` | `/api/v1/catalog/versions/{versionId}/retire` | permission | `catalog.publish` | organisation | — | `catalog.version.retired` | Retires the published version. Stops new orders and nothing else: work already in production runs to dispatch on the configuration it was pinned to, and the retired version stays fully readable |
| `GET` | `/api/v1/customers/measurement-templates` | permission | `catalog.templates.edit` | organisation | — | — | Lists the measurement templates and the state of every version of each, without their fields. Gated on the drafting permission because this is the administration surface: what a counter needs in order to *capture* against a template is #28's read, not this one |
| `POST` | `/api/v1/customers/measurement-templates` | permission | `catalog.templates.edit` | organisation | — | `customers.measurement_template.created` | Creates a template with no versions. The template is what a catalogue service type points at; it captures nothing until a version is drafted, reviewed and published |
| `GET` | `/api/v1/customers/measurement-templates/{templateId}` | permission | `catalog.templates.edit` | organisation | — | — | Reads one template and every version of it, with their fields. The entity tag is the template's, so an `If-Match` on any command below is a precondition on the whole template |
| `GET` | `/api/v1/customers/measurement-templates/{templateId}/versions/{versionId}/validation` | permission | `catalog.templates.edit` | organisation | — | — | Runs publish validation without changing anything. Reports every finding rather than the first, because a published version cannot be corrected in place and an administrator fixing one wants the whole list |
| `POST` | `/api/v1/customers/measurement-templates/{templateId}/versions` | permission | `catalog.templates.edit` | organisation | — | `customers.measurement_template.version.drafted` | Starts a draft version, empty or cloned from an existing one. Cloning copies the fields with fresh identities and the same keys, so a published version is changed by superseding it rather than by editing it |
| `POST` | `/api/v1/customers/measurement-templates/{templateId}/versions/{versionId}/fields` | permission | `catalog.templates.edit` | organisation | — | `customers.measurement_template.field.added` | Adds a field to a draft. The key is unique within the version and is what captured values are filed under |
| `PUT` | `/api/v1/customers/measurement-templates/{templateId}/versions/{versionId}/fields/{fieldId}` | permission | `catalog.templates.edit` | organisation | — | `customers.measurement_template.field.changed` | Replaces a field of a draft, keeping its key. The key in the body is ignored: renaming through an edit would orphan every value already filed under the old one |
| `POST` | `/api/v1/customers/measurement-templates/{templateId}/versions/{versionId}/fields/{fieldId}/delete` | permission | `catalog.templates.edit` | organisation | — | `customers.measurement_template.field.removed` | Removes a field from a draft. A `POST` sub-resource rather than a `DELETE`, so the reason travels in a body like every other command here |
| `POST` | `/api/v1/customers/measurement-templates/{templateId}/versions/{versionId}/submit` | permission | `catalog.templates.edit` | organisation | — | `customers.measurement_template.version.submitted` | Submits a draft for review. The draft stops being editable the moment it is submitted, so a reviewer reads a version that cannot change under them |
| `POST` | `/api/v1/customers/measurement-templates/{templateId}/versions/{versionId}/return` | permission | `catalog.templates.publish` | organisation | — | `customers.measurement_template.version.returned` | Sends a version in review back to its author, with a reason and a second factor. The approval goes back with it, so a resubmitted version is reviewed in the state it is then in |
| `POST` | `/api/v1/customers/measurement-templates/{templateId}/versions/{versionId}/approve` | permission | `catalog.templates.publish` | organisation | — | `customers.measurement_template.version.approved` | Approves a reviewed version so that it may be published. Step-up, because the permission carries it and approval is the decision publication then executes. The submitter does not also approve — unless they are the only administrator holding this permission, because a rule that locks a one-owner shop out of its own templates is not one that shop can follow |
| `POST` | `/api/v1/customers/measurement-templates/{templateId}/versions/{versionId}/publish` | permission | `catalog.templates.publish` | organisation | — | `customers.measurement_template.version.published` | Makes a version the one measurements are captured against, retiring the version it supersedes in the same act. Step-up and a stated reason: it changes what every counter in the shop is asked to measure. Two administrators publishing at once are settled by a partial unique index — one commits, the other is answered `409` |
| `POST` | `/api/v1/customers/measurement-templates/{templateId}/versions/{versionId}/retire` | permission | `catalog.templates.publish` | organisation | — | `customers.measurement_template.version.retired` | Stops new captures against the published version. Step-up and a stated reason. Refused while a published catalogue version still points at this template, which would leave a service a counter can order with nothing to measure it by |
| `POST` | `/api/v1/customers/measurement-drafts` | permission | `measurements.capture` | current-branch | — | `customers.measurement.draft.started` | Starts measuring a garment, or hands back the measuring already under way. A branch has at most one open draft per customer and template, held by a partial unique index, so asking twice reaches the first rather than leaving two people each filling in half of a different one. A session working at no branch is refused: measurements are taken where the customer is standing, and there is no sensible default to file them under |
| `GET` | `/api/v1/customers/measurement-drafts/{draftId}` | permission | `measurements.capture` | current-branch | `customers.measurement_draft` | — | Reads a garment being measured, with the tag a section save sends back as `If-Match`. Scoped to the draft's own branch: a draft is shared *within* a branch, and another branch reading a half-finished garment is not something this surface offers |
| `POST` | `/api/v1/customers/measurement-drafts/{draftId}/sections` | permission | `measurements.capture` | current-branch | `customers.measurement_draft` | `customers.measurement.section.saved` | Saves one step of the wizard. The values replace that step rather than merging into it, which is what lets a value be cleared. Nothing is checked against the template's ranges here: a half-measured garment is a normal state, and refusing an implausible number while somebody is holding the tape teaches them to type a plausible lie |
| `GET` | `/api/v1/customers/measurement-drafts/{draftId}/check` | permission | `measurements.capture` | current-branch | `customers.measurement_draft` | — | Says what stands between a draft and a confirmed measurement, changing nothing. Reports every field that would be refused rather than the first, and each finding names the **field** and never the value — a problem detail is logged, relayed and read by people the measurement is not for |
| `GET` | `/api/v1/customers/measurement-drafts/{draftId}/template` | permission | `measurements.capture` | current-branch | `customers.measurement_draft` | — | Reads the template version a draft is pinned to, with its fields, for the wizard to render through (#123). The capture-side read of a template: the administration reads in this table demand `catalog.templates.edit`, which a counter does not hold, so this one answers through the draft — it demands what starting the draft demanded, is scoped to the draft's branch like every other draft route, and reaches only the version the draft will be confirmed against, never the one being drafted to replace it. Carries nothing about the customer |
| `POST` | `/api/v1/customers/measurement-drafts/{draftId}/confirm` | permission | `measurements.capture` | current-branch | `customers.measurement_draft` | `customers.measurement.confirmed` | Turns a draft into the immutable record of a measurement, with the values, the consumed draft and the event in one transaction. Refused without a current `measurement_storage` consent (INV-MSR-05). The draft is consumed exactly once (INV-MSR-02), so a retry after a lost answer is a `409` rather than a second measurement. Correcting an earlier measurement creates a new one with a reason and leaves the old readable; it never edits it |
| `GET` | `/api/v1/customers/measurements/{measurementVersionId}` | permission | `measurements.capture` | assigned-branches | — | — | Reads one confirmed measurement, rendered through the template version it was captured under. Declares `TouchesNoBranchOwnedResource` (#121): a confirmed measurement is a fact about a customer, and [`../prd/workflows/branch-scenarios.md`](../prd/workflows/branch-scenarios.md) section 3.2 places the customer record organisation-wide — a garment measured at one branch and stitched at another is the ordinary case, so locking the read to the branch that held the tape would refuse exactly the tailor who needs it. The narrower `measurements.read_sheet` permission and the sensitive-read audit arrive with the sheet in #122 |
| `GET` | `/api/v1/customers/measurements/{measurementVersionId}/template` | permission | `measurements.capture` | assigned-branches | — | — | Reads the template version a confirmed measurement renders through, with its fields (#124): the labels, groups and units a comparison screen and a correction need, read by way of the measurement as the draft route reads them by way of the draft. Declares `TouchesNoBranchOwnedResource` for the reason the read of a measurement gives; what it answers is a fact about the template, and nothing about the customer travels with it |
| `GET` | `/api/v1/customers/measurements/{measurementVersionId}/sheet` | permission | `measurements.read_sheet` | assigned-branches | — | `customers.measurement.sheet.read` | Reads a customer's measurements as a tailor reads them: the measurements and **nothing else about the customer** — no name, no telephone number, no address — which is what lets the sheet be printed and handed to whoever is cutting. A **sensitive read** (INV-MSR-06): audited explicitly in the query handler rather than as a page view, and shown on the customer's own timeline gated on this permission, because "who looked at my measurements" is a question she may ask. Narrowed by the permission rather than by the branch: `measurements.read_sheet` is held by fewer people than `measurements.capture` |
| `GET` | `/api/v1/customers/measurements/{beforeId}/compare/{afterId}` | permission | `measurements.capture` | assigned-branches | — | — | What changed between two of a customer's measurements, oldest first. Matched on the field **key** rather than on field identity, because a template version mints new identifiers for every field it carries — matching on identity would report every field of a version change as dropped and re-added. A renamed field therefore reads as one dropped and one added, which is what a rename is once values are filed under a key. Two measurements of different customers or different templates are refused rather than compared, so it cannot be used to read across a customer |
| `GET` | `/api/v1/customers/{customerId}/measurements` | permission | `measurements.capture` | assigned-branches | — | — | Every measurement a customer has, newest first and **without the values**: a list is for choosing which to reuse or compare, and the choice is made on the date, who took it and whether it corrected something. Every one rather than the latest, because offering only the newest would make reuse mean reuse the last one — the silent reuse #28 forbids. The field-level minimisation of [`../nfr/data-classification.md`](../nfr/data-classification.md) section 5.2, not an omission |
| `GET` | `/api/v1/customers/` | permission | `customers.read` | assigned-branches | — | — | Finds a customer by name, native name, customer number or the tail of a telephone number. Answers across the organisation: a record one of the caller's branches can see comes back in full, one it cannot comes back as a masked disambiguation card. A term under three characters returns nothing, so a single keystroke cannot page the organisation's whole customer list |
| `POST` | `/api/v1/customers/` | permission | `customers.create` | assigned-branches | — | `customers.customer.registered` | Creates a customer record and allocates its number from the branch the caller is working in. Refused with 409 and a `candidates` member when an existing record resembles this one strongly enough to be worth reading; the caller then either opens that record or confirms this is somebody new, and the trail records that a person took the decision |
| `GET` | `/api/v1/customers/{customerId}` | permission | `customers.read` | assigned-branches | — | — | Reads one record with the version a correction must be made against, projected through the approved response view `customers.record`. The six contact fields are populated only for a caller holding `customers.read_contact`; for everybody else they arrive as null with `contactIncluded` false, which is the field-level minimisation of [`../nfr/data-classification.md`](../nfr/data-classification.md) section 5.2 rather than an omission. What the view carries is approved in [`field-visibility.md`](field-visibility.md) |
| `PUT` | `/api/v1/customers/{customerId}` | permission | `customers.update` | assigned-branches | — | `customers.customer.corrected` | Corrects what the record says about the person. A changed name is kept as an alias, so somebody who married last year is still found under the name on her old receipts. The trail names the fields that changed and never the values they changed to. A change to any contact field also requires `customers.read_contact`; otherwise the whole correction returns 403 without changing the record (Owner decision, 2026-09-10, #83) |
| `GET` | `/api/v1/customers/{customerId}/communication-preferences` | permission | `customers.read_consent` | assigned-branches | — | — | Reads which channels the customer accepts, the language she is written to in and the hours she would rather not be messaged in. Gated on `customers.read_consent` rather than `customers.read` because [`../nfr/data-classification.md`](../nfr/data-classification.md) section 5.3 is one inventory row covering consent records **and** communication preferences, and names Reception, Branch Manager and Auditor as who may see it — which is exactly who holds this permission. `hasBeenRecorded` distinguishes a customer who chose no channel from one nobody has asked, and an unrecorded preference carries no ETag because there is no version of a row that does not exist |
| `PUT` | `/api/v1/customers/{customerId}/communication-preferences` | permission | `customers.update` | assigned-branches | — | `customers.preferences.changed` | Replaces the whole preference, so the trail reads as a state rather than a difference. An empty `allowedChannels` is how she says do not message her and withdraws consent to nothing. `If-Match` is required once a preference exists and must be omitted before then. No reason is demanded: a reason belongs to a correction, where the trail has to say why somebody changed what the record says about a person, and this records what she asked for |
| `GET` | `/api/v1/customers/{customerId}/consent` | permission | `customers.read_consent` | assigned-branches | — | — | Reads the organisation's whole consent register with this customer's answers against each, newest first. Purposes she has never been asked about come back too, because that is how the counter knows to ask, and so do retired ones, because what she said about one still stands. `canBeAnswered` says whether a new answer may be recorded now |
| `POST` | `/api/v1/customers/{customerId}/consent` | permission | `customers.update` | assigned-branches | — | `customers.consent.recorded` | Records what the customer said about one purpose. It appends and never edits: withdrawing is a `Withdrawn` answer and agreeing again is another `Granted` one, so the evidence that she once withdrew survives her changing her mind ([`../nfr/data-classification.md`](../nfr/data-classification.md) section 5.3). The wording version is read from the register rather than sent, because a client that could name a version could record an answer against words she was never read. **A withdrawal is written to the trail as `customers.consent.withdrawn`**, not as the action this route declares — one route, because the counter screen is a set of toggles, and two trail actions, because a withdrawal is what somebody reviewing the trail is looking for. Gated on `customers.update`, whose catalogue description is "correct a customer record and record or withdraw consent"; no reason is demanded, because the reason an answer exists is that she gave it |
| `POST` | `/api/v1/customers/{customerId}/deactivate` | permission | `customers.deactivate` | assigned-branches | — | `customers.customer.deactivated` | Withdraws the record from ordinary use. Not a deletion: an order placed last year still names the person who placed it. What changes is that a search no longer offers the record when somebody starts a new order |
| `POST` | `/api/v1/customers/{customerId}/reactivate` | permission | `customers.deactivate` | assigned-branches | — | `customers.customer.reactivated` | Returns a withdrawn record to ordinary use. Gated on the permission that withdrew it, following the suspend-and-reinstate precedent above, so a record cannot be put beyond the reach of everybody present |
| `POST` | `/api/v1/customers/{customerId}/open` | permission | `customers.read` | assigned-branches | — | `customers.customer.opened-at-branch` | Records that the caller's branch has begun serving this customer, which adds that branch to the record's visibility and writes the cross-branch entry [`../prd/workflows/branch-scenarios.md`](../prd/workflows/branch-scenarios.md) section 3.1 requires. Gated on `customers.read` because that is what section 3.3 of the same document approves for opening the record at a second branch, and because the effect is bounded to the caller's own branch. Whether this should be automatic instead is open decision OD-13 |
| `GET` | `/api/v1/customers/{customerId}/duplicates` | permission | `customers.read` | assigned-branches | — | — | Lists the records that may be the same person as this one — the screen a merge is decided from. It scores the records as they stand rather than reading back the suspicions raised when either was created, because a correction to either can create a resemblance or remove one. Gated on `customers.read` and **not** on `customers.merge`: [`../prd/raci.md`](../prd/raci.md) note (1) has Reception "consulted-then-blocked rather than free to merge", and reading who might be a duplicate is what she does before asking a manager — demanding the merge permission to look would mean nobody could prepare the decision. Cards are masked exactly as they are in search, and a record already merged is never offered |
| `POST` | `/api/v1/customers/{customerId}/merge` | permission | `customers.merge` | assigned-branches | — | `customers.customer.merged` | **Needs a fresh re-authentication.** Folds the record named in the body into the record named in the path, irreversibly — exception EX-01 in [`../prd/exceptions.md`](../prd/exceptions.md). The merged number stays searchable as an alias on the survivor, every branch that could see the folded record can see the survivor, and measurements and orders are re-pointed by `customers.customer-merged.v1` while a snapshot already frozen onto settled work is never rewritten (INV-CUS-04). Needs a reason, a fresh re-authentication and a precondition on **both** halves of the pair — an `If-Match` carrying the survivor's version and a `mergedCustomerVersion` in the body carrying the folded record's, so a correction to either since they were read is a 409 rather than an irreversible merge of a pair nobody approved; `mergedCustomerVersion` is a concrete version and never an `If-Match` value, so `*` is refused. The two halves refuse differently: a stale survivor is `customers.version-conflict` with `currentVersion` and an `ETag`, a stale record being folded in is `customers.merged-record-changed` with `mergedCustomerVersion` and no `ETag`; the permission is declared `RequiresMfa` and `RequiresStepUp` in the catalogue, so a session that merely holds it is refused. A **second** trail entry, `customers.customer.merged-away`, is written against the record that was folded in — one action on the route and two in the trail, because the trail is read by entity and a merge is the one change whose interesting answer for one record is only ever written against the other. There is no un-merge |
| `POST` | `/api/v1/customers/{customerId}/export` | permission | `customers.export` | organisation | — | `customers.customer.exported` | Generates the copy of a customer's personal data that answers a subject-access request — profile, full consent history and communication preferences, as JSON. No images, no duplicate scores and no merge reasons ([`../nfr/data-classification.md`](../nfr/data-classification.md) section 5.2.1 keeps the last two inside Customers), and no measurements, because the system records none until #28. The response is a receipt naming the export and its expiry, **not** the document. Needs a reason; the permission is declared `RequiresMfa` in the catalogue but **not** `RequiresStepUp`, so a session that has merely signed in with a password is refused while one that has answered a second factor is not. Generating destroys any earlier export for the same customer, so at most one copy of a person's record exists outside the record at a time |
| `GET` | `/api/v1/customers/{customerId}/exports/{exportId}` | permission | `customers.export` | organisation | — | `customers.customer.export-downloaded` | Streams a generated export. The permission, the organisation and the expiry are re-checked on every request, and **the read itself is audited** — the entry is written by the handler rather than by a filter, because a filter watches state changes and this is a read, and the route declares the action so that the audit is visible where every other endpoint's is. Section 10 of [`../nfr/data-classification.md`](../nfr/data-classification.md) lists an export download among the reads that are audited explicitly, and no filter watches a read. There is no link and no signed URL: section 9 requires an export to be streamed by a re-authorising endpoint. The download filename is the export's UUIDv7, because personal data is never a filename. Answers 404 `customers.export-expired` once the copy has been destroyed, which happens when it expires or when a newer export replaces it; the record that the export was taken is kept |
| `GET` | `/api/v1/customers/{customerId}/timeline` | permission | `customers.read` | assigned-branches | — | — | Reads one customer's history, merged by the **host** from every module that holds part of it — the first route the host owns rather than a module, because no module could answer it without reading another's tables ([`../architecture/architecture-rules.md`](../architecture/architecture-rules.md), ROD-02). Gated on `customers.read`, which is what opens the record the history is about; what a caller is shown *within* it is narrower, and each contributing module applies that. Consent and communication-preference entries need `customers.read_consent` and subject-access export entries need `customers.export` — the entry is withheld whole rather than blanked, because its title alone says the thing those permissions exist to gate. The reason an actor gave needs `customers.read_notes`, which is free text a member of staff typed about a named person, and is the first field in the application to gate on it; the field set is approved in [`field-visibility.md`](field-visibility.md) as `customers.timeline`. **Not audited**: it reads history rather than personal data, and section 10 of [`../nfr/data-classification.md`](../nfr/data-classification.md) names the export *download* as the read that is audited explicitly, not the record of it. A module that cannot answer is named in `unavailableSources` rather than silently omitted |
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
| `payments.allocate` as an audit action | Also the audit action of the rule that applies a held advance when the order posts an invoice (INV-PAY-05), recorded without a user; the permission of the same name gates nothing on its own yet, because the rule is not a command anyone runs |
| `custody.recipient_confirmation` | Branch configuration: `otp_or_signature`, `signature` or `otp`. It grants nobody anything |
| `custody.dispatch_policy`, `dispatch.allow_on_advance` | Branch configuration read by the dispatch gate |
| `authz.denied`, `custody.event-conflict`, `media.unavailable` | Audit actions and problem-detail codes |

---

## 7. What this document does not yet cover

Sections 3, 4 and 5 are declared, seeded, published and tested. Most of section 4 is still forward-declared: the
`Built by` column names the issue that puts each permission to work, and #26 is the first to have done so —
`customers.read`, `customers.create`, `customers.update`, `customers.deactivate`, `customers.merge`,
`customers.read_consent` and `customers.export` gate the fifteen customer routes in section 5, and
`customers.read_contact` decides which fields the six of them that answer with a record return. Every other row is
a declaration waiting for its issue. That is a statement about how far the application has been built, not about how
far it has been checked — the grants in section 4 are exercised as real HTTP requests today, by every role, in two
branches, against a real database (section 8).

What is genuinely absent, and where it is owned:

| Absent | Where it lands |
| --- | --- |
| **Field-level minimisation** — which columns each role sees, rather than which rows | [`field-visibility.md`](field-visibility.md), with [`../nfr/data-classification.md`](../nfr/data-classification.md) as its source. This document decides reach; that one decides detail. Two of its five views are served today, both by the customer routes below; the other three wait for #28, #32a and #33 |
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
