# Field visibility

Branch scope decides which rows a caller may reach. This document decides which **columns** of those rows reach
them. It names every response view the application declares, every field each view may carry, the permission
behind each field, and — derived from those two things and from the seeded role grants — exactly what each role is
shown.

> **Read this first: nothing served today is shaped by this document.** No endpoint in the solution projects a
> response through `IFieldVisibilityPolicy`, because no endpoint returns any of the three declared views yet. What
> is live is the mechanism and its invariants, which are code and are tested. Everything below is therefore a
> statement of what a view *will* carry when the issue that publishes it lands — see section 1 — and not a
> description of a behaviour you could observe. Treating it as the second is the one way this document could
> mislead somebody.

It is the second half of the authorisation model of issue #24. The first half is
[`permission-matrix.md`](permission-matrix.md), which the owner approves and an administrator then edits per
installation; this half is a property of the software and changes only in a release. Read the two together:
a permission opens a screen, and this document says what is on it.

Like the matrix, the tables inside the `<!-- matrix:… -->` markers are parsed by a test —
[`tests/Tailor360.UnitTests/Security/FieldVisibilityDocumentTests.cs`](../../tests/Tailor360.UnitTests/Security/FieldVisibilityDocumentTests.cs)
— so what is approved here and what a response carries cannot become two different things. The prose around the
markers is never read by anything, so it can say as much as its readers need.

Its source is [`../nfr/data-classification.md`](../nfr/data-classification.md), which classifies every field the
system holds and states who may access each class. Where this document cites a section number, that is the
document it cites.

---

## 1. Status

> **Not approved, and not yet enforced anywhere.** No endpoint in the solution returns any of these views: the
> three declared below are forward declarations, named by the issue blueprint and traced to product documents,
> waiting for the modules that will serve them. What *is* live is the mechanism and its invariants — the
> catalogue refuses to build a view that carries a class it withholds, and the tests below hold this document
> equal to the code and to the seeded grants.

| | |
| --- | --- |
| **Delivered by** | Issue #24, the field-level minimisation half |
| **Approval state** | Open. Section 6 lists the three consequences of the derivation that want an owner's answer |
| **Enforced today** | The view catalogue, the mask and the payload builder are code and are tested. Nothing is served |
| **Changes with** | #26 (customer views), #28 (measurement sheet), #32a and #33 (job card, work queue), #45 (workload) |

---

## 2. Why a declared field set and not a projection per handler

The failure this design prevents is not a missing `if`. It is the third pull request.

The first handler that returns a job card will omit the customer's phone number, because whoever writes it will
have read the classification document that morning. The second will copy the first. The third will add a field
during a busy week, from a data-transfer object that happens to carry a total, and no reviewer will notice that a
price has arrived on a workshop screen — because there is nothing to notice it *against*. A rule that lives in
each handler is a rule that is re-derived, correctly or otherwise, every time somebody touches one.

So the field set is declared once, in one place, and three things follow from the declaration rather than from
anybody remembering:

1. **A view cannot carry a class it withholds.** `ResponseView`'s constructor throws. "The job card has no price
   on it" stops being an observation about today's code and becomes something that cannot be written.
2. **A field nobody declared cannot reach a response.** `MaskedPayload.Set` throws on an undeclared name. A field
   a caller may not see is dropped in silence, because that is the purpose; a field *nobody has approved* is a
   different mistake and gets a different answer.
3. **What each role sees is computed, not asserted.** Section 5 is derived from section 4 and the role grants in
   `permission-matrix.md`. Nobody writes it, so nobody can write it wrongly.

A mask is computed from **permissions, never from roles**. An administrator who invents a custom role gets the
right answer without anybody adding a case, which is the same reason the permission model is built on permissions
rather than on role names.

---

## 3. The views

A view is one named response shape. `Requires` is the permission an endpoint returning it must demand — reaching
the view at all. `Withheld` is the classes it may never carry, enforced when the catalogue is built.

<!-- matrix:views -->
| View | Module | Requires | Withheld | Purpose |
| --- | --- | --- | --- | --- |
| `customers.measurement_sheet` | `Customers` | `measurements.read_sheet` | `CustomerContact`, `CustomerNotes`, `Pricing`, `PaymentState` | The figures the garment is cut to, rendered for the workshop and for print. |
| `orders.job_card` | `Orders` | `orders.read` | `CustomerContact`, `CustomerNotes`, `Pricing`, `PaymentState` | One garment job as the workshop needs it: what to make, from whose measurements, by when. |
| `orders.work_queue` | `Orders` | `orders.read` | `CustomerContact`, `CustomerNotes`, `Pricing`, `PaymentState` | The branch's jobs as a list, for picking up the next piece of work. |
<!-- /matrix:views -->

All three withhold the same four classes, and the sentence behind that is in
[`../nfr/data-classification.md`](../nfr/data-classification.md) section 3: *"a job card shows the customer's name
and job number and never their phone number, **which is why a Tailor can be shown a job card at all**"*.
Minimisation here is not a restriction bolted onto a screen. It is what makes the screen shareable with the people
who do the work.

### 3.1 A view is a response shape, and it is not the only place a field is withheld

A view governs what leaves the server *to a browser*. It says nothing about what leaves one module *to another*,
and those are different questions with different answers.

`Customers.Contracts.ICustomerSnapshotQuery` is the first case. Orders (#32a) and Billing (#42) copy a customer
onto an estimate, an order and an invoice, and neither may read `customers.customers`
([`../architecture/module-ownership.md`](../architecture/module-ownership.md) section 5.2), so the snapshot is how
those facts leave the module. It takes the caller's permissions and populates the contact fields only for a caller
holding `customers.read_contact` — the same rule, applied a step earlier. The mask is evaluated inside the SQL
projection, so for a caller who does not hold it the telephone number, email address and postal address are never
read out of PostgreSQL at all.

This does not re-derive the rule section 2 warns about. The rule is written once, keyed off one permission whose
key the contract repeats from the catalogue under a test that fails if the two ever differ. What it adds is a
second boundary in front of the first: Orders will declare its own view withholding `CustomerContact`, and by then
a masked caller's contact fields will not have reached Orders to be withheld.

The customer record's own response views — `customers.read` returning name, number, branch and status,
`customers.read_contact` adding the contact fields, `customers.read_notes` adding notes, and consent history
behind `customers.read_consent` — are the `CustomerViewPolicy` half of #26 and are **not built yet**. Until they
are, the customer endpoints mask by hand in their payload projection, which is exactly the arrangement section 2
argues against and is why it is written down here rather than left to be discovered.

---

## 4. The fields

One row per field. `Class` is the handling class from [`../nfr/data-classification.md`](../nfr/data-classification.md).
`Requires` is the permission a caller must hold to be shown the field, and `—` means the view's own permission is
the whole gate.

`—` is a decision, not a default. The clearest case is `customerName` on the job card. Neither Tailor nor Tailor
Master holds `customers.read`, because neither has any business opening a customer record; but
[`../prd/glossary.md`](../prd/glossary.md) defines a job card as showing "the customer name and job number only",
and requiring `customers.read` for the name would empty the job card for exactly the two roles it is printed for.
What keeps the name safe is not a permission on it — it is that contact details cannot appear beside it.

<!-- matrix:fields -->
| View | Field | Class | Requires | Rationale |
| --- | --- | --- | --- | --- |
| `customers.measurement_sheet` | `jobNumber` | `Operational` | — | Which job the sheet belongs to, so a loose sheet is never anonymous. |
| `customers.measurement_sheet` | `templateName` | `Operational` | — | The measurement template the figures were captured against. |
| `customers.measurement_sheet` | `templateVersion` | `Operational` | — | The published version pinned at confirmation, so a sheet reprinted a year later renders exactly as it did. |
| `customers.measurement_sheet` | `capturedAt` | `Operational` | — | When the figures were taken, which is how a stale set is spotted. |
| `customers.measurement_sheet` | `capturedBy` | `Operational` | — | Who took them, so a question about a figure has somebody to ask. |
| `customers.measurement_sheet` | `customerName` | `CustomerIdentity` | — | Whose figures these are. Nothing else about the customer is on the sheet. |
| `customers.measurement_sheet` | `values` | `Measurement` | — | The measured figures, stored in millimetres and rendered in the branch's display unit. The view's own permission is the gate; there is no sheet without them. |
| `customers.measurement_sheet` | `easeNotes` | `Measurement` | — | Ease and growth-allowance notes, which are read with the figures and classified with them. |
| `customers.measurement_sheet` | `fieldDiagrams` | `Media` | `media.read` | The template's line drawings, streamed by the authorising endpoint like all media. |
| `orders.job_card` | `jobNumber` | `Operational` | — | The job's own number, which is how the workshop, the label and the customer all refer to it. |
| `orders.job_card` | `orderNumber` | `Operational` | — | The order the job belongs to, so a multi-garment order can be kept together. |
| `orders.job_card` | `categoryLabel` | `Operational` | — | The stitching category, from the catalogue snapshot frozen at confirmation. |
| `orders.job_card` | `serviceLabel` | `Operational` | — | The service type within the category, likewise from the snapshot. |
| `orders.job_card` | `designSnapshot` | `Operational` | — | The design selections as agreed, embedding labels and illustration references so the card renders identically after the catalogue changes (docs/prd/design-options.md). |
| `orders.job_card` | `garmentInstructions` | `Operational` | — | Free-text craft instructions carried into the snapshot; they never price and never move the due date (OD-DES-06). |
| `orders.job_card` | `dueDate` | `Operational` | — | The promised date the workshop works to. |
| `orders.job_card` | `priority` | `Operational` | — | Priority and any rush marking, which decides the order of work. |
| `orders.job_card` | `currentPhase` | `Operational` | — | Where the job has got to in its pinned workflow version. |
| `orders.job_card` | `assignedTo` | `Operational` | — | Who the job is assigned to. A name, not a measure: throughput about a named person is a separate class and is not on this card. |
| `orders.job_card` | `barcodePayload` | `Operational` | — | The label's opaque payload. It carries no personal data by construction (docs/nfr/data-classification.md section 9). |
| `orders.job_card` | `customerName` | `CustomerIdentity` | — | Whose garment this is. The whole of the customer that reaches the workshop (docs/prd/glossary.md, job card). |
| `orders.job_card` | `measurements` | `Measurement` | `measurements.read_sheet` | The measurement snapshot frozen at confirmation. Sensitive personal data, and the read is audited explicitly (docs/nfr/data-classification.md section 5.4). |
| `orders.job_card` | `referenceImages` | `Media` | `media.read` | Reference photographs, streamed and re-authorised per request — never a URL (docs/nfr/data-classification.md section 5.5). |
| `orders.work_queue` | `jobNumber` | `Operational` | — | The job's own number. |
| `orders.work_queue` | `categoryLabel` | `Operational` | — | What kind of garment it is, so the queue can be scanned at a glance. |
| `orders.work_queue` | `dueDate` | `Operational` | — | The promised date, which is what the queue is ordered by. |
| `orders.work_queue` | `priority` | `Operational` | — | Priority and rush marking. |
| `orders.work_queue` | `currentPhase` | `Operational` | — | The phase the job is waiting in. |
| `orders.work_queue` | `holdState` | `Operational` | — | Whether the job is held and under which reason code, so nobody starts work on a garment that is waiting for a decision. |
| `orders.work_queue` | `assignedTo` | `Operational` | — | Who has it, which is how a queue distinguishes unassigned work from somebody else's. |
| `orders.work_queue` | `customerName` | `CustomerIdentity` | — | Whose garment it is. The same single identifying field the job card carries. |
| `orders.work_queue` | `assigneeThroughput` | `StaffPerformance` | `reports.read` | Completed-per-day and on-time rate for a named member of staff. Decision DC-11 says these are for the Tailor Master, the Branch Manager and the Owner and are never a wall display, so they are gated rather than shown beside the name. |
<!-- /matrix:fields -->

---

## 5. What each role is shown

**This table is derived.** It is computed from section 4 and from the default grants in
[`permission-matrix.md`](permission-matrix.md), and the test asserts the document carries exactly the computation.
When a grant changes, this table changes with it or the build fails; when it fails, the failure prints the
replacement.

`Reaches` says whether the role holds the view's own permission. A role that does not reach a view is shown
nothing of it — not an empty shell, not a redacted skeleton: the request is refused before a body exists.

<!-- matrix:role-fields -->
| View | Role | Reaches | Visible fields |
| --- | --- | --- | --- |
| `customers.measurement_sheet` | `owner` | yes | `jobNumber`, `templateName`, `templateVersion`, `capturedAt`, `capturedBy`, `customerName`, `values`, `easeNotes`, `fieldDiagrams` |
| `customers.measurement_sheet` | `admin` | no | — |
| `customers.measurement_sheet` | `branch_manager` | yes | `jobNumber`, `templateName`, `templateVersion`, `capturedAt`, `capturedBy`, `customerName`, `values`, `easeNotes`, `fieldDiagrams` |
| `customers.measurement_sheet` | `reception` | yes | `jobNumber`, `templateName`, `templateVersion`, `capturedAt`, `capturedBy`, `customerName`, `values`, `easeNotes`, `fieldDiagrams` |
| `customers.measurement_sheet` | `measurement_staff` | yes | `jobNumber`, `templateName`, `templateVersion`, `capturedAt`, `capturedBy`, `customerName`, `values`, `easeNotes`, `fieldDiagrams` |
| `customers.measurement_sheet` | `tailor_master` | yes | `jobNumber`, `templateName`, `templateVersion`, `capturedAt`, `capturedBy`, `customerName`, `values`, `easeNotes`, `fieldDiagrams` |
| `customers.measurement_sheet` | `tailor` | yes | `jobNumber`, `templateName`, `templateVersion`, `capturedAt`, `capturedBy`, `customerName`, `values`, `easeNotes`, `fieldDiagrams` |
| `customers.measurement_sheet` | `inventory_clerk` | no | — |
| `customers.measurement_sheet` | `cashier` | no | — |
| `customers.measurement_sheet` | `delivery_staff` | no | — |
| `customers.measurement_sheet` | `auditor` | no | — |
| `customers.measurement_sheet` | `hyfib_super_user` | no | — |
| `orders.job_card` | `owner` | yes | `jobNumber`, `orderNumber`, `categoryLabel`, `serviceLabel`, `designSnapshot`, `garmentInstructions`, `dueDate`, `priority`, `currentPhase`, `assignedTo`, `barcodePayload`, `customerName`, `measurements`, `referenceImages` |
| `orders.job_card` | `admin` | yes | `jobNumber`, `orderNumber`, `categoryLabel`, `serviceLabel`, `designSnapshot`, `garmentInstructions`, `dueDate`, `priority`, `currentPhase`, `assignedTo`, `barcodePayload`, `customerName`, `referenceImages` |
| `orders.job_card` | `branch_manager` | yes | `jobNumber`, `orderNumber`, `categoryLabel`, `serviceLabel`, `designSnapshot`, `garmentInstructions`, `dueDate`, `priority`, `currentPhase`, `assignedTo`, `barcodePayload`, `customerName`, `measurements`, `referenceImages` |
| `orders.job_card` | `reception` | yes | `jobNumber`, `orderNumber`, `categoryLabel`, `serviceLabel`, `designSnapshot`, `garmentInstructions`, `dueDate`, `priority`, `currentPhase`, `assignedTo`, `barcodePayload`, `customerName`, `measurements`, `referenceImages` |
| `orders.job_card` | `measurement_staff` | yes | `jobNumber`, `orderNumber`, `categoryLabel`, `serviceLabel`, `designSnapshot`, `garmentInstructions`, `dueDate`, `priority`, `currentPhase`, `assignedTo`, `barcodePayload`, `customerName`, `measurements`, `referenceImages` |
| `orders.job_card` | `tailor_master` | yes | `jobNumber`, `orderNumber`, `categoryLabel`, `serviceLabel`, `designSnapshot`, `garmentInstructions`, `dueDate`, `priority`, `currentPhase`, `assignedTo`, `barcodePayload`, `customerName`, `measurements`, `referenceImages` |
| `orders.job_card` | `tailor` | yes | `jobNumber`, `orderNumber`, `categoryLabel`, `serviceLabel`, `designSnapshot`, `garmentInstructions`, `dueDate`, `priority`, `currentPhase`, `assignedTo`, `barcodePayload`, `customerName`, `measurements`, `referenceImages` |
| `orders.job_card` | `inventory_clerk` | yes | `jobNumber`, `orderNumber`, `categoryLabel`, `serviceLabel`, `designSnapshot`, `garmentInstructions`, `dueDate`, `priority`, `currentPhase`, `assignedTo`, `barcodePayload`, `customerName`, `referenceImages` |
| `orders.job_card` | `cashier` | yes | `jobNumber`, `orderNumber`, `categoryLabel`, `serviceLabel`, `designSnapshot`, `garmentInstructions`, `dueDate`, `priority`, `currentPhase`, `assignedTo`, `barcodePayload`, `customerName` |
| `orders.job_card` | `delivery_staff` | yes | `jobNumber`, `orderNumber`, `categoryLabel`, `serviceLabel`, `designSnapshot`, `garmentInstructions`, `dueDate`, `priority`, `currentPhase`, `assignedTo`, `barcodePayload`, `customerName`, `referenceImages` |
| `orders.job_card` | `auditor` | yes | `jobNumber`, `orderNumber`, `categoryLabel`, `serviceLabel`, `designSnapshot`, `garmentInstructions`, `dueDate`, `priority`, `currentPhase`, `assignedTo`, `barcodePayload`, `customerName`, `referenceImages` |
| `orders.job_card` | `hyfib_super_user` | no | — |
| `orders.work_queue` | `owner` | yes | `jobNumber`, `categoryLabel`, `dueDate`, `priority`, `currentPhase`, `holdState`, `assignedTo`, `customerName`, `assigneeThroughput` |
| `orders.work_queue` | `admin` | yes | `jobNumber`, `categoryLabel`, `dueDate`, `priority`, `currentPhase`, `holdState`, `assignedTo`, `customerName`, `assigneeThroughput` |
| `orders.work_queue` | `branch_manager` | yes | `jobNumber`, `categoryLabel`, `dueDate`, `priority`, `currentPhase`, `holdState`, `assignedTo`, `customerName`, `assigneeThroughput` |
| `orders.work_queue` | `reception` | yes | `jobNumber`, `categoryLabel`, `dueDate`, `priority`, `currentPhase`, `holdState`, `assignedTo`, `customerName` |
| `orders.work_queue` | `measurement_staff` | yes | `jobNumber`, `categoryLabel`, `dueDate`, `priority`, `currentPhase`, `holdState`, `assignedTo`, `customerName` |
| `orders.work_queue` | `tailor_master` | yes | `jobNumber`, `categoryLabel`, `dueDate`, `priority`, `currentPhase`, `holdState`, `assignedTo`, `customerName`, `assigneeThroughput` |
| `orders.work_queue` | `tailor` | yes | `jobNumber`, `categoryLabel`, `dueDate`, `priority`, `currentPhase`, `holdState`, `assignedTo`, `customerName` |
| `orders.work_queue` | `inventory_clerk` | yes | `jobNumber`, `categoryLabel`, `dueDate`, `priority`, `currentPhase`, `holdState`, `assignedTo`, `customerName` |
| `orders.work_queue` | `cashier` | yes | `jobNumber`, `categoryLabel`, `dueDate`, `priority`, `currentPhase`, `holdState`, `assignedTo`, `customerName` |
| `orders.work_queue` | `delivery_staff` | yes | `jobNumber`, `categoryLabel`, `dueDate`, `priority`, `currentPhase`, `holdState`, `assignedTo`, `customerName` |
| `orders.work_queue` | `auditor` | yes | `jobNumber`, `categoryLabel`, `dueDate`, `priority`, `currentPhase`, `holdState`, `assignedTo`, `customerName`, `assigneeThroughput` |
| `orders.work_queue` | `hyfib_super_user` | no | — |
<!-- /matrix:role-fields -->

---

## 6. What the derivation exposes

Deriving the table rather than writing it surfaces three things a hand-written one would have hidden. None is a
defect in this mechanism; each is a question about a grant, and each is recorded here rather than quietly decided.

| # | What the derivation shows | Why it happens | What would close it |
| --- | --- | --- | --- |
| **FV-01** | Cashier, Delivery Staff, Inventory Clerk, Admin and Auditor all reach the job card | They hold `orders.read`, which [`permission-matrix.md`](permission-matrix.md) grants broadly and describes as "the order and its jobs" | Nothing here. Which *endpoints* exist is #32a's decision, and a role that never opens a job-card endpoint never sees one. The field sets above are correct for whoever does reach it |
| **FV-02** | Admin and Auditor see `assigneeThroughput` on the work queue | It is gated on `reports.read`, and decision **DC-11** names three roles — Tailor Master, Branch Manager, Owner — where `reports.read` is held by five | DC-11's concern is peers: no Tailor sees another Tailor's figures, and that holds. If the owner reads DC-11 strictly, #44 or #45 splits `reports.read` into a workload permission, and only the `Requires` cell of one row changes |
| **FV-03** | Cashier's job card carries no `referenceImages` | Cashier is the one branch role without `media.read` in the default grants | Nothing, if that is intended. It is listed because it is the kind of asymmetry that looks like a bug in six months and is not |

---

## 7. How a field is added

In one change, or the tests fail — which is the point of them:

| Step | Where |
| --- | --- |
| 1. Declare the field, its class, its permission and its rationale | The view's group in `src/Platform/Tailor360.Platform.Security/FieldVisibility/` |
| 2. Add its row to section 4 | This document |
| 3. Re-run the tests and paste the replacement role table the failure prints | This document, section 5 |
| 4. Set it in the handler through `MaskedPayload.Set` | The endpoint that returns the view |

A field of a class the view withholds cannot be added at all. If it belongs there, the withheld set is what is
wrong, and changing it is a change to what an owner approved: it goes in the pull request with the reason, and
[`../nfr/data-classification.md`](../nfr/data-classification.md) is what the reason has to agree with.

---

## 8. How this document is enforced

| # | Assertion | The drift it catches |
| --- | --- | --- |
| 1 | Section 3 names every declared view and no others | A view added in code and never approved |
| 2 | Each view's module, permission and withheld classes equal the code | An approval and an implementation saying different things |
| 3 | Section 4 has exactly one row per declared field, and no row without a field | A field shipped without approval, and an approved field nobody built |
| 4 | Each field's class and required permission equal the code | The dangerous direction: sensitive data approved as operational, or a permission quietly dropped |
| 5 | Every permission either document names is in the permission catalogue | A typo, which would hide a field from everybody for ever |
| 6 | Section 5 equals the derivation from section 4 and the seeded grants | A grant changed in the matrix and not reflected in what a role is shown |
| 7 | Every role named is a system role | A role invented in prose |
| 8 | Every view withholds all four workshop-forbidden classes, in the document as well as in the code | The sentence the owner approved becoming true only in the source |
| 9 | No role, on any view, is shown a field of a class that view withholds | The invariant checked end to end rather than at construction only |
| 10 | Every table is non-empty before any "every row…" assertion runs | The vacuous pass: a mangled table satisfying every rule about all of its rows |
