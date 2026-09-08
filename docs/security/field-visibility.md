# Field visibility

Branch scope decides which rows a caller may reach. This document decides which **columns** of those rows reach
them. It names every response view the application declares, every field each view may carry, the permission
behind each field, and — derived from those two things and from the seeded role grants — exactly what each role is
shown.

> **Read this first: two of the five views below are served, and three are not.** The customer record and the
> search card are what the Customers endpoints of #26 return, and every row about them describes a body you could
> go and observe. The measurement sheet, the job card and the work queue are forward declarations waiting for the
> modules that will serve them, and every row about those is a statement of what the view *will* carry — see
> section 1. Reading the second kind as the first is the one way this document could mislead somebody, so the
> status table says which is which.

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

> **Not approved, and enforced on three of the six views.** `customers.record` is returned by six customer
> endpoints, each of which projects its body through the mask this document approves. `customers.timeline` is
> returned by the host's composition endpoint and gates one field, `reason`, on `customers.read_notes` — the
> first thing in the application to gate on that permission. `customers.search_card` is returned by the search
> and the duplicate list; it declares no gated field, so there is nothing for a mask to withhold and the
> projection consults none — what the view fixes there is the field *set*, asserted against the payload type,
> and a test fails the build the day somebody gates a card field without teaching the projection to withhold it.
> The other three views are forward declarations, waiting for the modules that will serve them. The mechanism
> and its invariants are live for all six: the catalogue refuses to build a view that carries a class its
> surface forbids, and the tests below hold this document equal to the code and to the seeded grants.

| | |
| --- | --- |
| **Delivered by** | Issue #24, the field-level minimisation half. The customer views are #26 |
| **Approval state** | Open. Section 6 lists the four consequences of the derivation that want an owner's answer |
| **Served today** | `customers.record` and `customers.search_card`, by the Customers endpoints; `customers.timeline`, by the host |
| **Declared, not served** | `customers.measurement_sheet`, `orders.job_card`, `orders.work_queue` |
| **Changes with** | #28 (measurement sheet), #32a and #33 (job card, work queue), #45 (workload). Every module that registers an `ITimelineSource` widens what `customers.timeline` carries without changing its field set |

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
2. **A field nobody declared cannot reach a response.** Two mechanisms give the same guarantee, and which one
   applies depends on how the body is built. Where a handler assembles a body field by field, `MaskedPayload.Set`
   throws on an undeclared name: a field a caller may not see is dropped in silence, because that is the purpose,
   while a field *nobody has approved* is a different mistake and gets a different answer. Where the response is a
   published schema — the customer record and the search card, which the client is generated from —
   [`ResponseViewPayloadTests`](../../tests/Tailor360.ContractTests/ResponseViewPayloadTests.cs) holds the payload
   type's properties equal to the view's declared fields, so the same field fails the build instead. That one is
   the stronger of the two: it fires on the pull request rather than on the first request that carries the field.
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
| View | Module | Surface | Requires | Withheld | Purpose |
| --- | --- | --- | --- | --- | --- |
| `customers.measurement_sheet` | `Customers` | `Workshop` | `measurements.read_sheet` | `CustomerContact`, `CustomerNotes`, `Pricing`, `PaymentState` | The figures the garment is cut to, rendered for the workshop and for print. |
| `customers.record` | `Customers` | `Counter` | `customers.read` | `Measurement`, `Media`, `Pricing`, `PaymentState`, `StaffPerformance` | One customer as the counter opens them: who they are, how to reach them, and whether the record still stands. |
| `customers.search_card` | `Customers` | `Counter` | `customers.read` | `CustomerNotes`, `Measurement`, `Media`, `Pricing`, `PaymentState`, `StaffPerformance` | One customer as a search result: enough to tell two people apart before a second record is created for one of them. |
| `customers.timeline` | `Customers` | `Counter` | `customers.read` | `Measurement`, `Media`, `Pricing`, `PaymentState`, `StaffPerformance` | What has happened to one customer, merged from every module that holds part of it. |
| `orders.job_card` | `Orders` | `Workshop` | `orders.read` | `CustomerContact`, `CustomerNotes`, `Pricing`, `PaymentState` | One garment job as the workshop needs it: what to make, from whose measurements, by when. |
| `orders.work_queue` | `Orders` | `Workshop` | `orders.read` | `CustomerContact`, `CustomerNotes`, `Pricing`, `PaymentState` | The branch's jobs as a list, for picking up the next piece of work. |
<!-- /matrix:views -->

`Surface` is who reads the view, and it is what decides the withheld set. The three **workshop** surfaces withhold
the same four classes — contact, notes, pricing and payment state — and the sentence behind that is in
[`../nfr/data-classification.md`](../nfr/data-classification.md) section 2: *"a job card shows the customer's name
and job number and never their phone number, **which is why a Tailor can be shown a job card at all**"*.
Minimisation here is not a restriction bolted onto a screen. It is what makes the screen shareable with the people
who do the work.

The two **counter** surfaces withhold a different five — measurements, media, pricing, payment state and staff
performance — and may carry contact details, to the callers permitted them. That is the same sentence applied to
the screen it was written about rather than a relaxation of it: the argument above is about a card that is printed
and left on a bench, and ringing a customer to say her blouse is ready is what the counter screen exists for. It is
recorded as a column because reading the rule as *"no view carries contact details"* is what kept the customer
record undeclared and hand-masked until #26 — the rule was right and its scope was unwritten, so the scope is now a
declared property of each view rather than an accident of there being only workshop views.

Neither list is written twice. Both are `SurfaceRules.ForbiddenOn(surface)`, and `ResponseView`'s constructor
refuses a view that withholds less than its surface forbids, so the `Withheld` column above can be **wider** than
the surface's minimum and never narrower. `customers.search_card` is the one that is wider: a search result is not
where free text about a person belongs, so it withholds `CustomerNotes` as well.

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

The customer record's own response views are the `CustomerViewPolicy` half of #26 and are **now built**:
`customers.record` returns name, number, branch and status to a caller holding `customers.read`, and adds the six
contact fields for one who also holds `customers.read_contact`; `customers.search_card` is the same decision over a
search result. Every customer endpoint that returns a record projects it through the mask
`IFieldVisibilityPolicy` computes from these tables, so the hand-written ternaries section 2 argues against are
gone from the payload.

Two things the plan named for this half are deliberately **not** views, and saying so here is cheaper than leaving
somebody to wonder:

- **Notes.** The module still holds no notes *field*: there is no column, no payload and no note somebody typed
  as a note. What it does hold, and what the timeline surfaced, is the **reason** an actor gave for a change —
  "she asked us to close the record" — which is free text a member of staff typed about a named person and is
  therefore the same class. So `customers.read_notes` is no longer a permission that gates nothing: it gates
  `customers.timeline.reason`. A note field proper, when one is added, joins `customers.record` behind the same
  permission, which is one row here and one property there.
- **Consent history and communication preferences.** Both are gated whole, by `customers.read_consent` on the
  endpoint, and neither has a field a caller may hold the endpoint's permission and still not see. A view would
  add an approved field list and no masking. That is worth having eventually — it is the same "third pull request"
  argument — but it is a declaration exercise rather than the field-level minimisation this half of #24 is about,
  and it is left to the issue that next changes those payloads. The timeline is where the split does bite: an
  entry saying consent was withdrawn is withheld **whole** from a caller without `customers.read_consent`,
  because its title alone discloses what that permission exists to gate. That withholding is the contributing
  module's, not this document's — a view masks fields, and there is no field whose absence would hide an entry.

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
| `customers.record` | `customerId` | `Operational` | — | The record's identifier, which every other call about this person is made with. |
| `customers.record` | `customerNumber` | `CustomerIdentity` | — | The number written on the card the customer carries. |
| `customers.record` | `displayName` | `CustomerIdentity` | — | The name as the customer gave it, which is the whole point of opening the record. |
| `customers.record` | `nativeName` | `CustomerIdentity` | — | The Tamil-script name, where there is one, so the counter can read it back as written. |
| `customers.record` | `phone` | `CustomerContact` | `customers.read_contact` | The primary telephone number. Contact is split from identity because docs/nfr/data-classification.md section 5.2 classifies it separately. |
| `customers.record` | `alternatePhone` | `CustomerContact` | `customers.read_contact` | The second number, tried when the first does not answer. |
| `customers.record` | `email` | `CustomerContact` | `customers.read_contact` | The email address, under the same permission as the telephone numbers. |
| `customers.record` | `addressLine` | `CustomerContact` | `customers.read_contact` | The street line, needed to deliver and for nothing else. |
| `customers.record` | `locality` | `CustomerContact` | `customers.read_contact` | The area or town, under the same permission. |
| `customers.record` | `postcode` | `CustomerContact` | `customers.read_contact` | The postal code, under the same permission. |
| `customers.record` | `contactIncluded` | `Operational` | — | Whether the contact fields above were included for this caller, so that a client can tell a withheld number from a customer who never gave one. |
| `customers.record` | `language` | `Operational` | — | The language the customer is written to in, which decides what a message looks like rather than saying anything about the person. |
| `customers.record` | `status` | `Operational` | — | Whether the record is in use, so a deactivated one is not offered actions. |
| `customers.record` | `owningBranchId` | `Operational` | — | The branch that created the record. |
| `customers.record` | `visibilityBranchIds` | `Operational` | — | The branches that see the record in ordinary search results (docs/prd/workflows/branch-scenarios.md section 3.2). |
| `customers.record` | `aliases` | `CustomerIdentity` | — | Previous names, spellings and merged customer numbers — identity under another writing, and never a telephone number or an address. |
| `customers.record` | `createdAt` | `Operational` | — | When the record was created. |
| `customers.record` | `updatedAt` | `Operational` | — | When it was last changed. |
| `customers.record` | `version` | `Operational` | — | The concurrency token an edit must be made against. |
| `customers.record` | `mergedIntoCustomerId` | `Operational` | — | The record this one was folded into, or null while it stands on its own. Never gated: whether the record still stands is a fact about the record rather than about the person, and a screen that cannot see it offers actions against a customer who no longer exists. |
| `customers.record` | `mergedAt` | `Operational` | — | When it was folded in, or null while it stands on its own. |
| `customers.search_card` | `customerId` | `Operational` | — | The record the card leads to. |
| `customers.search_card` | `customerNumber` | `CustomerIdentity` | — | The number, which is what an old bill or a card in a purse carries. |
| `customers.search_card` | `displayName` | `CustomerIdentity` | — | The name as given, which is what the counter searched for. |
| `customers.search_card` | `nativeName` | `CustomerIdentity` | — | The Tamil-script name, where there is one. |
| `customers.search_card` | `maskedPhone` | `CustomerContact` | — | The number with everything but its last four digits replaced. It carries no permission because it is masked for everybody, whatever they hold: enough to confirm a number a customer is reading out, and not enough to be a contact list, which is what makes a search that reaches across branches safe (docs/prd/workflows/branch-scenarios.md section 3.1). |
| `customers.search_card` | `owningBranchId` | `Operational` | — | The branch that created the record. |
| `customers.search_card` | `visibleToCaller` | `Operational` | — | False when the record is outside the caller's branches, which is what turns the card into a disambiguation card rather than a result. |
| `customers.search_card` | `status` | `Operational` | — | Whether the record is in use. |
| `customers.search_card` | `lastSeenAt` | `Operational` | — | When the record was last changed, which is what the list is ordered by. |
| `customers.timeline` | `entryId` | `Operational` | — | The entry, which is half of the position the next page resumes from. |
| `customers.timeline` | `occurredAt` | `Operational` | — | When it happened, by the server's clock — the client's clock is evidence and is never what a history is ordered by (docs/architecture/conventions.md 2.4). |
| `customers.timeline` | `source` | `Operational` | — | Which module contributed it, so a screen can say where a fact came from and a missing source can be named. |
| `customers.timeline` | `kind` | `Operational` | — | The stable dotted kind, which is the module's own audit action and is what a screen turns into an icon and a label. |
| `customers.timeline` | `title` | `Operational` | — | What happened, in the shop's words. |
| `customers.timeline` | `detail` | `Operational` | — | The longer description the recording module wrote. Operational prose about the record, never the customer's own data and never anything typed freehand. |
| `customers.timeline` | `reason` | `CustomerNotes` | `customers.read_notes` | The reason the actor gave. Free text a member of staff typed about a named person, which data-classification.md classifies as customer notes — so it is gated separately from the entry that carries it, and this is the first field in the application to gate on customers.read_notes. |
| `customers.timeline` | `reasonPermission` | `Operational` | — | What would have shown the reason, set whenever one was given. It is what lets a screen distinguish 'no reason was given' from 'a reason was given that you may not read', and it names a permission rather than repeating any of the text. |
| `customers.timeline` | `referenceType` | `Operational` | — | The kind of thing the entry links to, where it links to one. |
| `customers.timeline` | `referenceId` | `Operational` | — | What it links to. A UUIDv7, like every identifier that crosses the wire. |
| `customers.timeline` | `expandPermission` | `Operational` | — | What a caller must hold to open the reference. The entry is on the timeline either way: what it links to is a different question from whether it happened. |
| `customers.timeline` | `branchId` | `Operational` | — | The branch the entry belongs to, where it belongs to one. |
| `customers.timeline` | `actorDisplayName` | `Operational` | — | Who did it, as their name was at the time. A member of staff's name is not the customer's data and is not a measure of that member of staff, which is why it is operational and not StaffPerformance. |
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

One case is not a refusal and is worth stating, because the table above does not show it. A **command** that
answers with the record it just changed — registering a customer, correcting one, deactivating one, merging two —
demands `customers.create`, `customers.update`, `customers.deactivate` or `customers.merge`, and not
`customers.read`. Refusing such a caller the record they were just authorised to change would be a refusal of
their own write, so those endpoints ask for their mask through `IFieldVisibilityPolicy.MaskForReached`, naming the
permission the route demanded. Every **field** gate in section 4 still applies, which is why a caller who may
correct a record but not read contact details is answered with the corrected record and no telephone number. The
permission named must be one the caller actually holds, so a handler cannot assert reach on anybody's behalf.

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
| `customers.record` | `owner` | yes | `customerId`, `customerNumber`, `displayName`, `nativeName`, `phone`, `alternatePhone`, `email`, `addressLine`, `locality`, `postcode`, `contactIncluded`, `language`, `status`, `owningBranchId`, `visibilityBranchIds`, `aliases`, `createdAt`, `updatedAt`, `version`, `mergedIntoCustomerId`, `mergedAt` |
| `customers.record` | `admin` | yes | `customerId`, `customerNumber`, `displayName`, `nativeName`, `contactIncluded`, `language`, `status`, `owningBranchId`, `visibilityBranchIds`, `aliases`, `createdAt`, `updatedAt`, `version`, `mergedIntoCustomerId`, `mergedAt` |
| `customers.record` | `branch_manager` | yes | `customerId`, `customerNumber`, `displayName`, `nativeName`, `phone`, `alternatePhone`, `email`, `addressLine`, `locality`, `postcode`, `contactIncluded`, `language`, `status`, `owningBranchId`, `visibilityBranchIds`, `aliases`, `createdAt`, `updatedAt`, `version`, `mergedIntoCustomerId`, `mergedAt` |
| `customers.record` | `reception` | yes | `customerId`, `customerNumber`, `displayName`, `nativeName`, `phone`, `alternatePhone`, `email`, `addressLine`, `locality`, `postcode`, `contactIncluded`, `language`, `status`, `owningBranchId`, `visibilityBranchIds`, `aliases`, `createdAt`, `updatedAt`, `version`, `mergedIntoCustomerId`, `mergedAt` |
| `customers.record` | `measurement_staff` | yes | `customerId`, `customerNumber`, `displayName`, `nativeName`, `contactIncluded`, `language`, `status`, `owningBranchId`, `visibilityBranchIds`, `aliases`, `createdAt`, `updatedAt`, `version`, `mergedIntoCustomerId`, `mergedAt` |
| `customers.record` | `tailor_master` | no | — |
| `customers.record` | `tailor` | no | — |
| `customers.record` | `inventory_clerk` | no | — |
| `customers.record` | `cashier` | yes | `customerId`, `customerNumber`, `displayName`, `nativeName`, `phone`, `alternatePhone`, `email`, `addressLine`, `locality`, `postcode`, `contactIncluded`, `language`, `status`, `owningBranchId`, `visibilityBranchIds`, `aliases`, `createdAt`, `updatedAt`, `version`, `mergedIntoCustomerId`, `mergedAt` |
| `customers.record` | `delivery_staff` | yes | `customerId`, `customerNumber`, `displayName`, `nativeName`, `phone`, `alternatePhone`, `email`, `addressLine`, `locality`, `postcode`, `contactIncluded`, `language`, `status`, `owningBranchId`, `visibilityBranchIds`, `aliases`, `createdAt`, `updatedAt`, `version`, `mergedIntoCustomerId`, `mergedAt` |
| `customers.record` | `auditor` | yes | `customerId`, `customerNumber`, `displayName`, `nativeName`, `phone`, `alternatePhone`, `email`, `addressLine`, `locality`, `postcode`, `contactIncluded`, `language`, `status`, `owningBranchId`, `visibilityBranchIds`, `aliases`, `createdAt`, `updatedAt`, `version`, `mergedIntoCustomerId`, `mergedAt` |
| `customers.record` | `hyfib_super_user` | no | — |
| `customers.search_card` | `owner` | yes | `customerId`, `customerNumber`, `displayName`, `nativeName`, `maskedPhone`, `owningBranchId`, `visibleToCaller`, `status`, `lastSeenAt` |
| `customers.search_card` | `admin` | yes | `customerId`, `customerNumber`, `displayName`, `nativeName`, `maskedPhone`, `owningBranchId`, `visibleToCaller`, `status`, `lastSeenAt` |
| `customers.search_card` | `branch_manager` | yes | `customerId`, `customerNumber`, `displayName`, `nativeName`, `maskedPhone`, `owningBranchId`, `visibleToCaller`, `status`, `lastSeenAt` |
| `customers.search_card` | `reception` | yes | `customerId`, `customerNumber`, `displayName`, `nativeName`, `maskedPhone`, `owningBranchId`, `visibleToCaller`, `status`, `lastSeenAt` |
| `customers.search_card` | `measurement_staff` | yes | `customerId`, `customerNumber`, `displayName`, `nativeName`, `maskedPhone`, `owningBranchId`, `visibleToCaller`, `status`, `lastSeenAt` |
| `customers.search_card` | `tailor_master` | no | — |
| `customers.search_card` | `tailor` | no | — |
| `customers.search_card` | `inventory_clerk` | no | — |
| `customers.search_card` | `cashier` | yes | `customerId`, `customerNumber`, `displayName`, `nativeName`, `maskedPhone`, `owningBranchId`, `visibleToCaller`, `status`, `lastSeenAt` |
| `customers.search_card` | `delivery_staff` | yes | `customerId`, `customerNumber`, `displayName`, `nativeName`, `maskedPhone`, `owningBranchId`, `visibleToCaller`, `status`, `lastSeenAt` |
| `customers.search_card` | `auditor` | yes | `customerId`, `customerNumber`, `displayName`, `nativeName`, `maskedPhone`, `owningBranchId`, `visibleToCaller`, `status`, `lastSeenAt` |
| `customers.search_card` | `hyfib_super_user` | no | — |
| `customers.timeline` | `owner` | yes | `entryId`, `occurredAt`, `source`, `kind`, `title`, `detail`, `reason`, `reasonPermission`, `referenceType`, `referenceId`, `expandPermission`, `branchId`, `actorDisplayName` |
| `customers.timeline` | `admin` | yes | `entryId`, `occurredAt`, `source`, `kind`, `title`, `detail`, `reasonPermission`, `referenceType`, `referenceId`, `expandPermission`, `branchId`, `actorDisplayName` |
| `customers.timeline` | `branch_manager` | yes | `entryId`, `occurredAt`, `source`, `kind`, `title`, `detail`, `reason`, `reasonPermission`, `referenceType`, `referenceId`, `expandPermission`, `branchId`, `actorDisplayName` |
| `customers.timeline` | `reception` | yes | `entryId`, `occurredAt`, `source`, `kind`, `title`, `detail`, `reasonPermission`, `referenceType`, `referenceId`, `expandPermission`, `branchId`, `actorDisplayName` |
| `customers.timeline` | `measurement_staff` | yes | `entryId`, `occurredAt`, `source`, `kind`, `title`, `detail`, `reasonPermission`, `referenceType`, `referenceId`, `expandPermission`, `branchId`, `actorDisplayName` |
| `customers.timeline` | `tailor_master` | no | — |
| `customers.timeline` | `tailor` | no | — |
| `customers.timeline` | `inventory_clerk` | no | — |
| `customers.timeline` | `cashier` | yes | `entryId`, `occurredAt`, `source`, `kind`, `title`, `detail`, `reasonPermission`, `referenceType`, `referenceId`, `expandPermission`, `branchId`, `actorDisplayName` |
| `customers.timeline` | `delivery_staff` | yes | `entryId`, `occurredAt`, `source`, `kind`, `title`, `detail`, `reasonPermission`, `referenceType`, `referenceId`, `expandPermission`, `branchId`, `actorDisplayName` |
| `customers.timeline` | `auditor` | yes | `entryId`, `occurredAt`, `source`, `kind`, `title`, `detail`, `reasonPermission`, `referenceType`, `referenceId`, `expandPermission`, `branchId`, `actorDisplayName` |
| `customers.timeline` | `hyfib_super_user` | no | — |
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

Deriving the table rather than writing it surfaces four things a hand-written one would have hidden. None is a
defect in this mechanism. The first three are questions about a grant; the fourth is a question about a value in
the code. Each is recorded here rather than quietly decided.

| # | What the derivation shows | Why it happens | What would close it |
| --- | --- | --- | --- |
| **FV-01** | Cashier, Delivery Staff, Inventory Clerk, Admin and Auditor all reach the job card | They hold `orders.read`, which [`permission-matrix.md`](permission-matrix.md) grants broadly and describes as "the order and its jobs" | Nothing here. Which *endpoints* exist is #32a's decision, and a role that never opens a job-card endpoint never sees one. The field sets above are correct for whoever does reach it |
| **FV-02** | Admin and Auditor see `assigneeThroughput` on the work queue | It is gated on `reports.read`, and decision **DC-11** names three roles — Tailor Master, Branch Manager, Owner — where `reports.read` is held by five | DC-11's concern is peers: no Tailor sees another Tailor's figures, and that holds. If the owner reads DC-11 strictly, #44 or #45 splits `reports.read` into a workload permission, and only the `Requires` cell of one row changes |
| **FV-03** | Cashier's job card carries no `referenceImages` | Cashier is the one branch role without `media.read` in the default grants | Nothing, if that is intended. It is listed because it is the kind of asymmetry that looks like a bug in six months and is not |
| **FV-04** | Admin and Measurement Staff are shown `maskedPhone` on a search card while being shown no contact field at all on the record | The card's number is masked in the projection — everything but its last four digits replaced — for every caller, so it carries no permission of its own. That is what makes a search that reaches across branches safe ([`../prd/workflows/branch-scenarios.md`](../prd/workflows/branch-scenarios.md) section 3.1), and it is the behaviour that shipped with the search; declaring the view is what made it visible here | Nothing, if four digits is the right amount to confirm a number somebody is reading out. If it is not, the fix is the mask, not this row: shortening the tail or gating the field changes what a duplicate check can do, and both are the owner's call rather than a change a reviewer should make in passing |

---

## 7. How a field is added

In one change, or the tests fail — which is the point of them:

| Step | Where |
| --- | --- |
| 1. Declare the field, its class, its permission and its rationale | The view's group in `src/Platform/Tailor360.Platform.Security/FieldVisibility/` |
| 2. Add its row to section 4 | This document |
| 3. Re-run the tests and paste the replacement role table the failure prints | This document, section 5 |
| 4a. Set it in the handler through `MaskedPayload.Set` | An endpoint that assembles its body field by field |
| 4b. Add the property to the payload record, and project it from the mask | An endpoint that returns a published schema — the customer record and the search card |
| 5. Regenerate the interface description and the client types | `TAILOR360_WRITE_OPENAPI=1 dotnet test --project tests/Tailor360.ContractTests/…`, then `pnpm --dir clients/pwa generate:api` |

A gated field must be one the payload can actually withhold — nullable, so that "withheld" has a value to send.
There is no way to leave a customer number out of a body that must contain one, so gating a non-nullable field
would approve an answer the code cannot give, and assertion 11 refuses it.

A field of a class the view withholds cannot be added at all. If it belongs there, the withheld set is what is
wrong, and changing it is a change to what an owner approved: it goes in the pull request with the reason, and
[`../nfr/data-classification.md`](../nfr/data-classification.md) is what the reason has to agree with.

---

## 8. How this document is enforced

| # | Assertion | The drift it catches |
| --- | --- | --- |
| 1 | Section 3 names every declared view and no others | A view added in code and never approved |
| 2 | Each view's module, surface, permission and withheld classes equal the code | An approval and an implementation saying different things |
| 3 | Section 4 has exactly one row per declared field, and no row without a field | A field shipped without approval, and an approved field nobody built |
| 4 | Each field's class and required permission equal the code | The dangerous direction: sensitive data approved as operational, or a permission quietly dropped |
| 5 | Every permission either document names is in the permission catalogue | A typo, which would hide a field from everybody for ever |
| 6 | Section 5 equals the derivation from section 4 and the seeded grants | A grant changed in the matrix and not reflected in what a role is shown |
| 7 | Every role named is a system role | A role invented in prose |
| 8 | Every **workshop** surface withholds all four workshop-forbidden classes, in the document as well as in the code | The sentence the owner approved becoming true only in the source |
| 8b | Every **counter** surface withholds measurements, pricing, payment state and staff performance | The other half of the same decision: a screen about a customer quietly acquiring a price or a body measurement |
| 9 | No role, on any view, is shown a field of a class that view withholds | The invariant checked end to end rather than at construction only |
| 10 | Every table is non-empty before any "every row…" assertion runs, and each surface has at least one view | The vacuous pass: a mangled table satisfying every rule about all of its rows, or a rescoped rule that stopped matching anything |
| 11 | Each payload type registered as a view's published schema carries exactly that view's declared fields, in order, and every gated field among them is nullable | A property added to a response record without an approved row, which `MaskedPayload` cannot catch because such a body is never built through it; and a field approved as gated that the projection has no way to withhold |
| 12 | Every view is declared on a surface, and withholds at least what that surface forbids | A view added with a quietly shorter withheld list, which the per-surface assertions above would then pass over |
