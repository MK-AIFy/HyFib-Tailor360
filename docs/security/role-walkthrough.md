# Role walkthrough — the two-branch demonstration

This is the scripted manual procedure that demonstrates the authorisation model doing what
[`permission-matrix.md`](permission-matrix.md) says it does: one representative user per role in each of two
branches, every role exercising its permitted actions in its own branch, and the corresponding actions refused
against the other branch. It is the verification issue #24 requires, and it exists because a matrix test proves
that the document and the code agree, and only a person driving the application proves that the agreement is the
one the business wanted.

Read it with [`permission-matrix.md`](permission-matrix.md) for the grants being exercised,
[`../prd/raci.md`](../prd/raci.md) for the accountability each step is checking, and
[`../process/definition-of-done.md`](../process/definition-of-done.md) item 9 for what counts as evidence.

---

## 1. Status

> **Section 3.1 is run and recorded. Sections 3.2 and 4 to 6 are not, and cannot be yet.** They need endpoints
> that enforce a permission and an administration surface that can create staff accounts, and neither exists:
> `docs/security/permission-matrix.md` section 5 shows that no published route demands a permission, and accounts
> are created by the screens of #25. This is the script for those sections and a record for the one that ran.

| | |
| --- | --- |
| **Written by** | Issue #24, on 2026-09-06 |
| **Last run** | 2026-09-06 — section 3.1 only, on PostgreSQL 16, `Development`, a freshly created database |
| **Run by** | The #24 review pass, transcript in the pull request |
| **Blocked on** | Section 3.2 and sections 4 to 6: endpoints that enforce a permission (#32a) and users that can be created (#25). Both issues carry them in their blueprints |
| **Evidence expected** | For each step, either a screenshot or the RFC 9457 problem-details body, with the correlation identifier. Personal data is edited out before the evidence is attached — see section 7 |

---

## 2. What this walkthrough is for

A matrix test cannot catch four things, and each of them has cost a real shop money somewhere:

1. **A grant that is correct and wrong.** The document and the code can agree perfectly on a grant nobody wanted.
   Only a person who runs the shop, reading the refusal on screen, can say so.
2. **Branch scope that is checked in the wrong place.** A permission held plus a branch not held must refuse. The
   failure mode is a check made against the caller's *claim* about their branch rather than against the
   *resource's* branch, and it looks identical until someone tries it.
3. **A refusal that leaks.** A `404` where a `403` was meant, or a problem-details body naming a customer, tells an
   unauthorised caller something. Reading the actual response is the only way to see it.
4. **A refusal nobody can act on.** "Forbidden" with no indication that a re-authentication would have worked
   makes staff phone the Owner instead of tapping *Confirm your identity*.

---

## 3. Setting the stage

### 3.1 The two branches

```bash
./scripts/dev reset                                    # destructive; development only
dotnet run --project src/Tools/Tailor360.Cli -- migrate
dotnet run --project src/Tools/Tailor360.Cli -- init-reference-data
dotnet run --project src/Tools/Tailor360.Cli -- seed-synthetic
```

`init-reference-data` writes the twelve system roles and their default grants; it is idempotent and prints what it
did. `seed-synthetic` opens the two branches the rest of this document refers to, and refuses to run in production.

| Branch | Code | Identifier | Referred to below as |
| --- | --- | --- | --- |
| Main Branch | `MAIN` | `0199a000-0000-7000-8000-000000000001` | **Branch A** |
| Second Branch | `SECOND` | `0199a000-0000-7000-8000-000000000002` | **Branch B** |

| Step | Command | Expected | Result (2026-09-06) |
| --- | --- | --- | --- |
| 3.1.1 | `init-reference-data` on an empty database | `System roles: 12 created, 0 updated, 0 already current.` | **Pass.** `System roles: 12 created, 0 updated, 0 already current.` and `Default grants: 258 added, 0 removed to match docs/security/permission-matrix.md.` |
| 3.1.2 | `init-reference-data` again | `System roles: 0 created, 0 updated, 12 already current.` — nothing changes on a second run | **Pass.** `System roles: 0 created, 0 updated, 12 already current.` and `Default grants: 0 added, 0 removed` |
| 3.1.3 | `seed-synthetic` twice | Both branches present exactly once; the second run changes nothing | **Pass.** Both runs print the same two branch identifiers, `main 0199a000-…-000000000001` and `second 0199a000-…-000000000002`; `SELECT code, name, id FROM identity.branches` returns `MAIN` and `SECOND`, one row each |
| 3.1.4 | `SELECT key, reach, assigned_by_default FROM identity.roles ORDER BY key;` | The twelve rows of [`permission-matrix.md`](permission-matrix.md) section 3 | **Pass.** Twelve rows: `admin`, `auditor`, `branch_manager`, `cashier`, `delivery_staff`, `hyfib_super_user`, `inventory_clerk`, `measurement_staff`, `owner`, `reception`, `tailor`, `tailor_master`. `Organisation` reach for `admin`, `auditor`, `hyfib_super_user` and `owner`; `Branch` for the other eight. `assigned_by_default` false for `hyfib_super_user` and `measurement_staff` only — matching section 3 of the matrix, including the Branch Manager row that OD-13 settles |
| 3.1.5 | `SELECT count(*) FROM identity.role_permissions;` | 258 — the sum of the `Permissions` column in section 3 of the matrix | **Pass.** 258 |

### 3.2 The twenty-four users

One user per role in each branch, so that every "own branch / other branch" pair below has a subject. Accounts are
created through the administration screens of issue #25; until those exist, section 3.2 cannot be run — and
`seed-synthetic` says so in its own output rather than creating them silently.

What *is* verified today, and by a test rather than by hand, is the answer each of these twenty-four people would
get: [`RoleMatrixTests`](../../tests/Tailor360.IntegrationTests/Authorization/RoleMatrixTests.cs) seeds one
account per role in each of two branches — plus one assigned to both — and asks for every permission in the
catalogue as each of them, through a real route table, a real cookie and a real session. That is not a substitute
for this section, because it exercises probe routes rather than the screens a person would use; it is what stands
in until the screens exist.

| User | Role | Branch assignment | Home branch |
| --- | --- | --- | --- |
| `owner.a` | Owner | A and B | A |
| `admin.a` | Admin | A and B | A |
| `manager.a` / `manager.b` | Branch Manager | A only / B only | A / B |
| `reception.a` / `reception.b` | Reception | A only / B only | A / B |
| `measure.a` / `measure.b` | Measurement Staff | A only / B only | A / B |
| `master.a` / `master.b` | Tailor Master | A only / B only | A / B |
| `tailor.a` / `tailor.b` | Tailor | A only / B only | A / B |
| `stores.a` / `stores.b` | Inventory Clerk | A only / B only | A / B |
| `cashier.a` / `cashier.b` | Cashier | A only / B only | A / B |
| `delivery.a` / `delivery.b` | Delivery Staff | A only / B only | A / B |
| `auditor.a` | Auditor | A and B | A |

Two of these are deliberately awkward, and that is the point. `owner.a`, `admin.a` and `auditor.a` are assigned to
both branches *and* hold `admin.organisation.read_all_branches`; the Branch Manager of A is assigned to A alone, so
step 4.3 can show that supervising a branch is not the same as reaching every branch.

| Step | Expected | Result |
| --- | --- | --- |
| 3.2.1 | Every account signs in and reaches `GET /api/v1/me` | |
| 3.2.2 | `GET /api/v1/me` returns the permissions of the account's role, matching the matrix | |
| 3.2.3 | Owner, Admin, Branch Manager, Cashier, Auditor and the vendor principal are required to enrol a second factor; Reception, Tailor Master, Tailor, Inventory Clerk, Delivery Staff and Measurement Staff are not | |
| 3.2.4 | Removing a role from an account takes effect on that account's **next request**, without signing out | |

---

## 4. Every role in its own branch

One row per role. The permitted action is the one the role exists to perform; the reference is the row of
[`../prd/raci.md`](../prd/raci.md) or [`../prd/state-transitions.md`](../prd/state-transitions.md) that says so.

| # | User | Action in Branch A | Permission | Expected | Result |
| --- | --- | --- | --- | --- | --- |
| 4.1.1 | `reception.a` | Create a customer, record consent, capture a measurement version | `customers.create`, `measurements.capture` | Permitted | |
| 4.1.2 | `reception.a` | Build a draft, issue the estimate, confirm the order, print the label | `orders.intake`, `orders.estimate`, `orders.confirm`, `custody.print_label` | Permitted | |
| 4.1.3 | `reception.a` | Record an advance | `payments.record` | Permitted | |
| 4.1.4 | `reception.a` | Print the receipt for it | `billing.print_receipt` | **Refused** — receipts are the Cashier's (`raci.md` rows 5 and 18) | |
| 4.2.1 | `measure.a` | Capture and confirm a measurement version | `measurements.capture` | Permitted | |
| 4.2.2 | `measure.a` | Confirm an order | `orders.confirm` | **Refused** | |
| 4.3.1 | `master.a` | Start production, assign a job, complete a phase, record a QC result | `orders.start_production`, `orders.assign`, `orders.phase_transition`, `orders.record_qc` | Permitted | |
| 4.3.2 | `master.a` | Open a rework after a failed check | `orders.open_rework` | Permitted | |
| 4.3.3 | `master.a` | Reschedule the promised date | `orders.reschedule` | **Refused** — the Branch Manager alone (`state-transitions.md` line 225) | |
| 4.4.1 | `tailor.a` | Scan to take custody, start and complete a phase, record a material issue | `custody.scan`, `orders.phase_transition`, `inventory.record_movement` | Permitted | |
| 4.4.2 | `tailor.a` | Open the job card | `orders.read` | Permitted, and the card shows **no customer contact details, no pricing and no payment state** | |
| 4.4.3 | `tailor.a` | Read the customer's phone number | `customers.read_contact` | **Refused** | |
| 4.5.1 | `stores.a` | Receive stock, issue material, run a stocktake and explain a variance | `inventory.record_movement`, `inventory.stocktake` | Permitted | |
| 4.5.2 | `stores.a` | Approve the variance they just counted | `inventory.approve_variance` | **Refused** — and the refusal must not depend on it being their own count | |
| 4.6.1 | `cashier.a` | Post an invoice, take the balance, allocate it, print the receipt, close the session | `billing.post_invoice`, `payments.record`, `payments.allocate`, `payments.session` | Permitted, after a second factor | |
| 4.6.2 | `cashier.a` | Refund a payment without re-authenticating in the last five minutes | `payments.refund` | **Refused for staleness**, and the response says a re-authentication would work | |
| 4.6.3 | `cashier.a` | Re-authenticate, then refund | `payments.refund` | Permitted; the reason is mandatory and appears in the audit entry | |
| 4.7.1 | `delivery.a` | Work the queue, receive-scan, dispatch, confirm the handover | `custody.receive`, `custody.dispatch`, `custody.confirm_delivery` | Permitted | |
| 4.7.2 | `delivery.a` | Dispatch an order with an unpaid balance | `custody.dispatch` | **Refused** — `custody.dispatch-blocked`, and the attempt is audited | |
| 4.7.3 | `delivery.a` | Approve the dispatch exception themselves | `billing.approve_dispatch_exception` | **Refused** — the Owner only (`raci.md` footnote (15)) | |
| 4.8.1 | `manager.a` | Hold, resume and reschedule a job; decide an alteration | `orders.hold`, `orders.resume`, `orders.reschedule`, `orders.alteration_decide` | Permitted | |
| 4.8.2 | `manager.a` | Merge two customers | `customers.merge` | Permitted after step-up, with a reason | |
| 4.8.3 | `manager.a` | Approve a reconciliation recorded by somebody else | `custody.approve_reconciliation` | Permitted after step-up | |
| 4.8.4 | `manager.a` | Approve a reconciliation they recorded themselves | `custody.approve_reconciliation` | **Refused** — a different user is required | |
| 4.8.5 | `manager.a` | Publish a price list | `billing.publish_price_list` | **Refused** — the Owner's (`raci.md` row 24) | |
| 4.9.1 | `owner.a` | Publish the catalogue and a price list; approve a dispatch exception | `catalog.publish`, `billing.publish_price_list`, `billing.approve_dispatch_exception` | Permitted after step-up | |
| 4.9.2 | `owner.a` | Read a report covering both branches | `reports.read`, `admin.organisation.read_all_branches` | Permitted | |
| 4.9.3 | `owner.a` | Take a custody scan on the shop floor | `custody.scan` | **Refused** — the Owner is not a shop-floor role | |
| 4.10.1 | `admin.a` | Invite a user, assign roles and branches, reprint a label | `admin.users`, `custody.reprint_label` | Permitted after step-up, with a reason | |
| 4.10.2 | `admin.a` | Change a feature flag | `admin.feature_flags` | **Refused** — Owner and the vendor principal only | |
| 4.11.1 | `auditor.a` | Read the audit trail, read reports across both branches, export | `admin.audit.read`, `reports.export`, `audit.export` | Permitted after a second factor | |
| 4.11.2 | `auditor.a` | Change anything at all — confirm an order, post an invoice, record a scan | any write | **Refused.** The Auditor is never a state-changing principal | |

---

## 5. Every role against the other branch

The same users, the same actions, against resources owned by **Branch B**. Every row is expected to refuse, except
the three principals whose reach is the organisation. This is the section that proves branch scope is evaluated
server-side against the resource, and not inferred from what the caller said their branch was.

| # | User | Action against Branch B | Expected | Result |
| --- | --- | --- | --- | --- |
| 5.1 | `reception.a` | Open a Branch B order by its identifier | **Refused** | |
| 5.2 | `reception.a` | Confirm a Branch B draft | **Refused** | |
| 5.3 | `master.a` | Assign a Branch B garment job to a Branch A tailor | **Refused** | |
| 5.4 | `tailor.a` | Scan a Branch B garment | **Refused** | |
| 5.5 | `stores.a` | Record a movement against Branch B stock | **Refused** | |
| 5.6 | `cashier.a` | Post an invoice for a Branch B order | **Refused** | |
| 5.7 | `delivery.a` | Dispatch a Branch B garment | **Refused** | |
| 5.8 | `manager.a` | Hold a Branch B job, or approve a Branch B reconciliation | **Refused** — supervising a branch is not reach across branches | |
| 5.9 | `manager.a` | Read a report for Branch B | **Refused** — reports are scoped to the caller's assigned branches | |
| 5.10 | `owner.a` | Read a Branch B order and a cross-branch report | Permitted — `admin.organisation.read_all_branches` | |
| 5.11 | `owner.a` | **Write** to a Branch B resource while their active branch is A | **Refused.** Organisation reach is a read permission; a write still demands the caller's current branch | |
| 5.12 | `auditor.a` | Read Branch B audit events and export them | Permitted | |
| 5.13 | Any of the above | Repeat 5.1 to 5.9 with the *identifier edited* — a Branch B identifier substituted into a Branch A request path | **Refused, and refused identically.** The response must not differ between "no such resource" and "not yours" in a way that confirms the resource exists | |
| 5.14 | Any refusal above | Inspect the response body | RFC 9457 problem details; no stack trace, no customer name, no order number belonging to the other branch. A correlation identifier is present | |
| 5.15 | Every refusal in section 5 that was a write | Check the audit trail | An `authz.denied` entry per actor, endpoint and minute; never sampled away | |

---

## 6. The session dimension

| # | Scenario | Expected | Result |
| --- | --- | --- | --- |
| 6.1 | A session that has answered the password and not the second factor attempts anything but finishing or ending the sign-in | Refused: `security.sign-in-incomplete` | |
| 6.2 | A role change is made while the holder is signed in | Their next request reflects it; they are not signed out | |
| 6.3 | A role is removed while the holder is mid-task | The next request refuses; nothing already committed is undone | |
| 6.4 | An account is suspended while signed in | The next request refuses and the session is revoked | |
| 6.5 | A step-up action more than five minutes after the last re-authentication | Refused for staleness, distinguishably from "not permitted at all" | |
| 6.6 | Re-authenticating in place, then retrying the pending request with the same `Idempotency-Key` | The action completes exactly once | |
| 6.7 | Sign out everywhere from one device | Every other session refuses on its next request | |

---

## 7. Recording the result

1. **Fill in every result cell.** A blank cell is a step that was not run, and it stays blank rather than being
   marked as passing.
2. **Attach the evidence.** A screenshot for a screen, the problem-details body for a refusal. A description of a
   refusal is not evidence of one.
3. **Edit out the personal data before attaching.** Synthetic data only, and even then: no phone numbers, no
   measurements, no images of a person. Identifiers are shortened to their first eight characters, which is enough
   to correlate the steps and not enough to be an identifier.
4. **Record the failures as issues, not as notes.** A step that refuses when the matrix says it should permit — or
   permits when it should refuse — is a defect in one of the two, and the pull request that fixes it changes both
   the code and [`permission-matrix.md`](permission-matrix.md).
5. **Update section 1** with the date, the runner and the build the run was made against.
