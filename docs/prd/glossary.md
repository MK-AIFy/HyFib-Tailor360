# HyFib Tailor 360 — Glossary

This is the authoritative glossary for the HyFib Tailor 360 documentation set, the API, the user interface strings
and the training material. Where a document, a screen label, an event name or a database column disagrees with this
file, this file wins and the other is a defect. Every term carries the module that **owns** it, so that a reader can
tell immediately which module's contracts define the term and which module may change its meaning (module ownership
is fixed by plan [Section 4.3](../IMPLEMENTATION_PLAN.md)). Read it alongside
[`00-overview.md`](00-overview.md), [`configurable-vs-fixed.md`](configurable-vs-fixed.md) and
[`assumptions-and-open-decisions.md`](assumptions-and-open-decisions.md).

## How to read the tables

- **Tamil** gives the word actually used on the shop floor, transliterated into Latin with the Tamil script in
  brackets. It is filled in only where a genuine shop-floor word exists; an em dash means the English term is used
  as-is in the shop, which is common for `order`, `delivery`, `stock` and `bill`.
- **†** marks a transliteration drafted by the authors of this document and **not yet reviewed by a native Tamil
  speaker**. Every Tamil entry currently carries this mark. Review is owed by the localisation workshop and lands
  in `../nfr/accessibility-localisation.md` (issue #19), which fixes the Tamil glossary for measurements and
  phases; the reviewed wording then flows into the `ta-IN` message catalogue.
- **Owning module** uses the module names of plan Section 4.3: Identity/Admin, Customers/Measurements,
  Catalog/Design, Media, Orders/Workflow, Custody/Barcode, Inventory, Billing/Payments, Reporting,
  Notifications/Feedback, Integration, Platform.

---

## 1. Roles and principals

Roles are default permission bundles; authorisation is evaluated on permission plus branch scope, never on the role
name alone (plan Section 4.4, issue #24).

| Term | Tamil | Definition | Owning module |
| --- | --- | --- | --- |
| Reception | — | The counter role: finds or creates the customer, records consent, captures images, builds the order draft, issues the estimate, confirms the order and prints labels | Identity/Admin |
| Tailor Master | mastar † (மாஸ்டர்) | The workshop lead: starts production, pins the workflow version, assigns garment jobs against capability and capacity, decides rework | Identity/Admin |
| Tailor | thaiyalkarar † (தையல்காரர்) | The stitching role: takes custody by scan, works phases, records material movements, hands the garment on | Identity/Admin |
| Inventory Clerk | — | Maintains items, suppliers, locations and reorder rules; records purchases, issues, returns and wastage; runs stocktakes | Identity/Admin |
| Cashier | — | Records advances and payments, allocates them, issues receipts, opens and closes the cashier session and reconciles | Identity/Admin |
| Delivery Staff | — | Works the delivery queue, performs the receive scan, dispatches and confirms the doorstep handover | Identity/Admin |
| Branch Manager | — | Supervises one or more branches: exception queues, holds, reconciliation cases, variance approvals and branch configuration within granted permissions | Identity/Admin |
| Owner | — | Approves catalogue, prices, tax, permissions and alert policies; approves dispatch exceptions; reads cross-branch reports | Identity/Admin |
| Admin | — | Administers users, branches, roles and configuration; a non-shop-floor superset of Branch Manager | Identity/Admin |
| Measurement Staff | — | The permission bundle `measurements.capture`; owns the "Measurements needed" queue. May be granted to Reception rather than held by dedicated staff — see OD-13 | Identity/Admin |
| Auditor | — | Read-only principal for audit events, financial records and deactivated customers; never state-changing | Identity/Admin |
| HyFib super-user | — | Vendor-side principal permitted to change feature flags with a mandatory reason and evaluation audit | Platform |
| System principal | — | The worker's own identity: the delivered type is `WorkerPrincipal`, and a *system* principal is one of those with no requester behind it (`IsSystem`). Constructible only through `IWorkerScopeFactory` from a declared `[WorkerJob]` scope with its permissions and branch scope — the constructor is private and the factories are internal, so the confinement is C# accessibility rather than a rule anything can waive | Platform |
| Branch | kilai † (கிளை) | An operating location with its own code, IANA timezone, working calendar, GST registration and document sequences | Identity/Admin |
| Organisation | — | The single legal entity that owns every branch; fixed at `organisation_id` on all operational aggregates | Identity/Admin |

---

## 2. Customer, consent and measurement

| Term | Tamil | Definition | Owning module |
| --- | --- | --- | --- |
| Customer | vaadikkaiyalar † (வாடிக்கையாளர்) | A person the branch serves. Identified by `customer_number` `C-<branch>-000001` and a UUIDv7 id; phone is validated but never unique; never hard-deleted | Customers/Measurements |
| Customer alias | — | A previous name, spelling or merged customer number kept searchable against the surviving customer | Customers/Measurements |
| Duplicate candidate | — | A scored, explained suspicion that two customer records are the same person, raised at create time from phone, name and address similarity | Customers/Measurements |
| Merge | — | The irreversible, authorised decision that one customer record survives and another is folded into it; re-points measurements and orders and keeps the merged history readable | Customers/Measurements |
| Correction | thirutham † (திருத்தம்) | An authorised edit to a customer record with a mandatory reason and before-and-after audit; `id` and `customer_number` never change | Customers/Measurements |
| Consent record | — | A versioned record of a customer's permission for one purpose — `measurement_storage`, `photo_capture`, `transactional_messages`, `marketing_messages`, `feedback_requests` — with wording version, source, actor and time | Customers/Measurements |
| Communication preference | — | The customer's allowed channels, language and quiet hours, queried server-side before any send | Customers/Measurements |
| Measurement template | alavu template † (அளவு) | The named set of fields to capture for a category or service type | Customers/Measurements |
| Measurement template version | — | A draft, in-review, published or retired version of a template. Published versions are immutable and carry field key, label, group, canonical unit, precision, required flag, ranges, conditional rules and a diagram reference | Customers/Measurements |
| Measurement field | alavu † (அளவு) | One captured value: stored canonically in millimetres with a display unit, precision and validation range | Customers/Measurements |
| Measurement draft | — | Work in progress on a device or shared within the branch; consumed exactly once when confirmed, never a source of truth for an order | Customers/Measurements |
| Measurement version | — | A confirmed, immutable set of values for a customer against one template version, with reason, taken-by and taken-at; may be reused by later orders | Customers/Measurements |
| Measurement snapshot | — | The **copy** of a measurement version's values written onto a garment job at order confirmation, with the measurement version id kept only as provenance | Orders/Workflow |
| Measurement sheet | alavu sheet † | The printable or view-only A4 rendering of a measurement version; access is a sensitive read and is audited | Customers/Measurements |
| Customer timeline | — | The merged, permission-filtered chronological view of a customer's consents, measurements, orders, invoices, payments, custody events, notifications and feedback, composed by the BFF from each module's `ITimelineSource` | Platform (composition) |
| Native name | — | The optional Tamil-script form of a customer's name, stored and searched directly alongside the transliterated normalised name | Customers/Measurements |

---

## 3. Catalogue, category and design

| Term | Tamil | Definition | Owning module |
| --- | --- | --- | --- |
| Category | — | A stitching category such as Blouse, Salwar, Lehenga, Gown or Kids, with a code, optional parent, active dates, branch availability and a feature flag. Administrators add categories without a deployment | Catalog/Design |
| Sub-category | — | A child category, for example Blouse to Pattern and Aari work; the same rules as a category | Catalog/Design |
| Service type | — | What is actually sold within a category, carrying the links that make it orderable: measurement template, workflow definition, design option groups, price-list item and QC checklist | Catalog/Design |
| Blouse | ravikkai † (ரவிக்கை) | Seed category; sub-categories Pattern and Aari work | Catalog/Design |
| Salwar | sudidar † (சுடிதார்) | Seed category | Catalog/Design |
| Lehenga | langa † (லங்கா) | Seed category | Catalog/Design |
| Gown | — | Seed category | Catalog/Design |
| Kids | kuzhandhai † (குழந்தை) | Seed category with simplified option subsets and age-band notes | Catalog/Design |
| Aari work | aari velai † (ஆரி வேலை) | Hand embroidery performed as a specialist, conditional phase; its design options appear only when the service type is Aari work | Catalog/Design |
| Catalog version | — | A draft, published or retired snapshot of the whole hierarchy — categories, service types, design groups, options and rules — that an order is confirmed against | Catalog/Design |
| Design option group | — | A named group of choices for a category, with single or multiple selection, a required flag, branch availability and active dates, for example front neck or sleeve type | Catalog/Design |
| Design option | — | One choice inside a group, with a label, illustration reference, alternative text, help text, price impact and time impact | Catalog/Design |
| Design rule | — | A configurable constraint between options: requires, excludes or conditional note. Example: padding requires lining | Catalog/Design |
| Design snapshot | — | The immutable value object frozen onto a garment job that embeds labels, illustration references and option versions, so a job card renders identically after the catalogue changes | Catalog/Design (built), Orders/Workflow (stored) |
| Design revision | — | A post-confirmation, authorised change to a garment's design snapshot with reason, price and due-date delta shown before approval; blocked once the workflow marks the design frozen | Orders/Workflow |
| QC checklist template | — | The configurable list of criteria for a category or service type; criteria are typed pass/fail, measurement tolerance or fit check, with defect codes, evidence requirement and responsible role | Catalog/Design |
| Catalogue dependency validator | — | A rule registered by another module that must pass before a catalog version may be published, for example "every service type references a published measurement template" | Catalog/Design |
| Illustration and diagram | — | Line drawings shown in the design picker and measurement wizard; stored as media objects with mandatory alternative text | Media |

---

## 4. Estimate, order and garment job

| Term | Tamil | Definition | Owning module |
| --- | --- | --- | --- |
| Order draft | — | A server-side, branch-shared, resumable work-in-progress order, locked per garment section and expiring after a configured window, default 72 hours | Orders/Workflow |
| Estimate | mathippeedu † (மதிப்பீடு) | A priced, shareable snapshot of a draft, numbered `E-<branch>-<FY>-000001`, with a validity date and a status of issued, superseded or converted. Marked "Estimate — not a tax invoice". Never posted, never numbered in the invoice sequence | Orders/Workflow |
| Order | — | The confirmed commitment to a customer, numbered `O-<branch>-<FY>-000001`, carrying one or more garment jobs, the customer snapshot, due date, priority and totals | Orders/Workflow |
| Garment | — | The physical article being made or altered | Orders/Workflow |
| Garment job | — | The unit of production and tracking: one garment, numbered `J-<branch>-<FY>-000001-01`, with its own category and service version, measurement, design and price snapshots, due date, priority, workflow, phases, assignments, QC results and custody state. Also called a **job card** | Orders/Workflow |
| Job card | job card † | The printed or on-screen rendering of a garment job for the workshop: job number, category, design snapshot, measurements, due cue and barcode label. Shows the customer name and job number only — never contact details | Orders/Workflow |
| Job dependency | — | A declared relationship between two garment jobs of the same order: `finish_before` blocks the dependent job's first phase, `deliver_together` binds them at the ready gate and in the delivery queue | Orders/Workflow |
| Order revision | — | An authorised re-pricing and re-validation of a confirmed order, allowed only while every job is still `confirmed` and none has entered production; supersedes the estimate | Orders/Workflow |
| Price snapshot | — | The `PricingResult` copied onto the order and each garment job at confirmation, including the price-list and tax configuration versions used | Orders/Workflow (stored), Billing/Payments (produced) |
| Display number | — | A human-readable reference such as `O-…`, `J-…`, `E-…`, an invoice or a receipt number, allocated from a per-branch and per-financial-year sequence. Searchable by authenticated staff only, never a lookup key on a customer-facing surface | Platform (sequences) |
| Resource id | — | The UUIDv7 primary key; the only identifier used in API paths, deep links and customer links | Platform |

---

## 5. Workflow, phase, QC and exceptions

| Term | Tamil | Definition | Owning module |
| --- | --- | --- | --- |
| Workflow definition | — | The named production process for a category or service type; administrators create and edit definitions without a deployment | Orders/Workflow |
| Workflow version | — | A draft, published or retired version of a definition, containing phases with code, name, permitted roles, required evidence, duration and SLA, optional or skippable flags, the transition graph and the category mapping. Published versions are immutable | Orders/Workflow |
| Phase | — | One step of a workflow instantiated on a garment job, with server-timestamped start, pause, resume and completion. Seed default: Intake, Cutting, Specialist work, Stitching, Finishing, QC, Ready for delivery | Orders/Workflow |
| Cutting | vettu † (வெட்டு) | The seed phase in which material is cut | Orders/Workflow |
| Stitching | thaiyal † (தையல்) | The seed phase in which the garment is sewn | Orders/Workflow |
| Finishing | — | The seed phase covering pressing, trimming and final presentation | Orders/Workflow |
| Start production | — | The authorised command that resolves and **pins** the published workflow version onto the garment job, creates its phases and refuses any later order revision | Orders/Workflow |
| Assignment | — | The record that a garment job phase is allotted to a user or team, with reason. Reassignment is a new row; earlier completions stay attributed to the previous assignee | Orders/Workflow |
| Assignee capability | — | A grant that a user or team may work a given category and phase, with validity dates and optional daily capacity; used to validate assignment | Orders/Workflow |
| Workboard | — | The Tailor Master's queue view of open jobs and phases, filtered by assigned, unassigned, overdue, priority, blocked and due-soon | Orders/Workflow |
| SLA | — | The per-phase duration target that raises `PhaseSlaBreached`; evaluated in the branch timezone against the branch working calendar, so non-working days are skipped where configured | Orders/Workflow |
| Due date | delivery thedhi † (தேதி) | The promised date for an order and for each garment job, evaluated in the branch timezone; raises `JobDueSoon` and `JobOverdue` exactly once per condition | Orders/Workflow |
| QC — quality control | sari paarthal † (சரி பார்த்தல்) | The recorded evaluation of a garment job against a published QC checklist version. Results are immutable and copy the criteria evaluated so they render identically after a checklist change | Orders/Workflow |
| Defect code | — | A configurable code recorded against a failed QC criterion | Catalog/Design |
| Rework | meendum thaiyal † (மீண்டும் தையல்) | The task opened when QC fails; the garment returns to production without losing history, and the ready gate stays closed while it is open | Orders/Workflow |
| Alteration | alteration † | A requested change to a delivered or in-progress garment — before or after delivery — with its own reason, changed measurement or design versions, price and due-date decisions and communication status | Orders/Workflow |
| Hold | thadai † (தடை) | A recorded suspension of a garment job with a reason and approval; the ready gate stays closed while any hold is open and an overdue hold is alerted | Orders/Workflow |
| Cancellation | — | The authorised termination of a garment job or an order, blocked in prohibited financial, stock and custody states; compensating flows are required rather than deletion | Orders/Workflow |
| Ready-for-delivery gate | — | The single evaluator of `ready_state`, combining workflow complete, QC passed with no open rework, documentation complete, no open hold, dependencies met and custody reconciled. Each predicate returns a reason code | Orders/Workflow |
| Ready state | readi † | The materialised outcome of the gate on a garment job; written by the gate alone and recomputed on every workflow, QC, hold, dependency and custody event | Orders/Workflow |

---

## 6. Barcode identity, scanning and custody

| Term | Tamil | Definition | Owning module |
| --- | --- | --- | --- |
| Barcode identity | — | An opaque, namespaced payload bound to exactly one entity: namespace letter plus an 11-character random body and one check character from the Crockford base32 alphabet, for example `G-7K3M9QW2XZ4B`. Namespaces: `G-` garment job, `S-` stock item, `I-` invoice, `R-` receipt. **Contains no PII and no display number.** Exactly one active identity per garment job; a payload is never re-issued | Custody/Barcode |
| Check character | — | The final character of a payload, computed with a Damm-style checksum over the 32-symbol alphabet so a mis-keyed or mis-read payload is rejected before any lookup | Custody/Barcode |
| Label | sticker † | The printed carrier of a barcode identity — thermal or A4 — showing job number, category cue, due cue, branch code and print version. Reprints are permission-controlled, reasoned and recorded | Custody/Barcode |
| Label print | — | The audit record of one label being printed: identity, template version, printer and format, actor, time, reason, batch and verification flag | Custody/Barcode |
| Scan | scan † | The act of reading a barcode. Sources are camera, keyboard-wedge hardware scanner and manual entry; manual entry demands a reason and is audited | Custody/Barcode |
| Scan event | — | The immutable, append-only record of a scan: job, action, from and to custodian, location, actor, device, branch, client and server time, correlation, source and any exception reason | Custody/Barcode |
| Custody | — | Who physically holds a garment right now: a user, a team or a location | Custody/Barcode |
| Custody transfer | — | The explicit two-sided hand-off — transfer out, then receive — moving custody between custodians or branches. Pending, accepted, rejected or expired; append-only | Custody/Barcode |
| Custodian | — | The user, team or location currently holding the garment | Custody/Barcode |
| Reconciliation case | — | The record opened for a custody exception — mismatch, duplicate, stale, unknown location, lost or disputed — with evidence, resolution and, above a threshold, approval by a different user | Custody/Barcode |
| Correction event | — | A new scan event with action `CORRECTION`, linked to the event it corrects and to its case; history is never edited | Custody/Barcode |
| Delivery queue | — | The list of garment jobs past the ready gate, grouped by order with due age, balance and blocking reasons | Custody/Barcode |
| Dispatch | anuppudhal † (அனுப்புதல்) | Releasing a garment from the branch into the custody of Delivery Staff, permitted only against a recorded dispatch authorisation | Custody/Barcode |
| Dispatch authorisation | — | The record created by the receive scan when the dispatch gate passes: policy version, amount, approver and an expiry of the branch end of day. The doorstep confirmation references it | Custody/Barcode |
| Dispatch eligibility | — | Billing's answer to "may this be dispatched": `Paid`, `PartialAboveThreshold`, `PartialBelowThreshold`, `Unpaid`, `ApprovedException` or `NotEvaluated`. **Fails closed** — an unevaluated result blocks | Billing/Payments |
| Dispatch exception | — | The single, Billing-owned override of the payment rule: single-use, reason-coded, bound to order, job set, maximum outstanding amount, policy version and an expiry of at most 72 hours, approved with step-up by someone other than the dispatcher | Billing/Payments |
| Delivery confirmation | — | The doorstep record of handover: recipient name plus a one-time password or a signature stroke, optional photo, referencing the dispatch authorisation and re-validated on replay | Custody/Barcode |
| Handoff dispute | — | A disagreement about a hand-off, which opens a reconciliation case | Custody/Barcode |

---

## 7. Inventory

| Term | Tamil | Definition | Owning module |
| --- | --- | --- | --- |
| Stock item | sarakku † (சரக்கு) | Anything held in stock — cloth, lining, thread, hooks, zips, packaging — with SKU, barcode or supplier EAN, base unit, purchase and issue units, tax metadata and branch or location availability | Inventory |
| Material | thuni † (துணி) | Cloth and trims consumed by a garment job. Customer-supplied material is tracked separately and never valued | Inventory |
| Unit and unit conversion | — | The base unit an item is held in and the invertible, acyclic conversion factors to its purchase and issue units | Inventory |
| Supplier | — | A vendor with contacts, GSTIN, approval status, lead time and payment terms; suspended and retired suppliers stay in history but cannot be chosen on new purchase orders | Inventory |
| Location | — | A warehouse, store or bin belonging to a branch, with allowed transfer destinations | Inventory |
| Reorder rule | — | The per item and per location minimum, reorder point, target quantity, lead time and responsible role that drive low-stock evaluation | Inventory |
| Stock ledger | — | The immutable, append-only, trigger-protected record of every stock movement with a signed quantity in the base unit, its type, its references and its actor. **The only authoritative source of stock** | Inventory |
| Ledger entry | — | One row of the stock ledger. Types: opening, purchase receipt, reservation, release, issue, consumption, return, transfer out, transfer in, wastage, adjustment | Inventory |
| Balance | — | The derived on-hand, reserved, available and in-transit quantity per item and location, updated in the same transaction as the ledger insert, rebuildable from the ledger and reconciled by a scheduled job | Inventory |
| Reservation | — | Stock earmarked for a garment job, serialised by a row lock on the balance so it cannot be oversubscribed; released or converted to consumption | Inventory |
| Consumption | — | The recorded use of stock by a garment job and phase, reducing on hand | Inventory |
| Wastage | kazhivu † (கழிவு) | Stock lost or spoiled, recorded with a reason against a job or a location | Inventory |
| Purchase order and purchase receipt | — | The intent to buy and the recorded arrival of goods with supplier, lines, cost, tax reference, lot, receiving location and evidence | Inventory |
| Stocktake | stock kanakkeduppu † (கணக்கெடுப்பு) | A counting session per branch and location with a freeze policy, count sheets, recount, variance explanation, approval by a different user above a threshold, and posting as ledger adjustments | Inventory |
| Variance | — | The difference between counted and expected quantity in a stocktake, explained and approved before it is posted | Inventory |
| Low-stock alert | — | The de-duplicated alert state machine — raised, acknowledged, snoozed, escalated, cleared — driven by the branch alert policy and routed through Notifications | Inventory |
| Valuation | — | The value of stock per item and location at a cut-off using the configured method — weighted average by default, FIFO optional — recorded as an immutable valuation run | Inventory |
| Customer material custody | — | The record that a customer's own cloth is held against an order or job; received and returned, never valued | Inventory |

---

## 8. Billing, payment and tax

| Term | Tamil | Definition | Owning module |
| --- | --- | --- | --- |
| Price list and price-list version | vilai † (விலை) | The configurable, effective-dated set of base rates, inclusive or exclusive flags, allowed discounts, surcharges and approval thresholds. Published versions are immutable | Billing/Payments |
| Pricing result | — | The deterministic output of the pricing engine: line components, document totals, rounding allocation and the exact price-list and tax configuration versions used | Billing/Payments |
| Invoice | bill † | The tax document for an order. Draft until posted; posting allocates the next number under a row lock and freezes the row. A database trigger blocks every update and delete on posted rows | Billing/Payments |
| Invoice line | — | One charged line of an invoice, carrying the garment job it relates to | Billing/Payments |
| Invoice cancellation | — | An appended record — actor, reason, time, approval — from which the displayed status is derived. The original PDF still renders with its original hash | Billing/Payments |
| Credit note | — | The document that reduces what a customer owes, for example after a cancellation where value was already recognised. Posted and immutable like an invoice | Billing/Payments |
| Debit note | — | The document that increases what a customer owes after posting | Billing/Payments |
| Estimate | mathippeedu † (மதிப்பீடு) | See section 4. An estimate is **not** a tax invoice, is never posted and never consumes an invoice number | Orders/Workflow |
| Receipt | rasidhu † (ரசீது) | The numbered acknowledgement of money received, carrying an `R-…` barcode; append-only | Billing/Payments |
| Advance | munpanam † (முன்பணம்) | Money taken before the work is invoiced, held unapplied against the customer or order until allocated | Billing/Payments |
| Payment | panam † (பணம்) | Money received in a payment mode — cash, card, UPI, bank transfer or other — with reference, payer, branch, cashier and idempotency key; append-only, status changes only by new rows | Billing/Payments |
| Allocation | — | The application of a payment or advance to a specific invoice; deterministic oldest-invoice-first by default, with authorised manual allocation | Billing/Payments |
| Balance | baaki † (பாக்கி) | Posted charges minus allocations minus credits plus refunds. Computed only by Billing; Custody and Delivery never compute it | Billing/Payments |
| Refund and reversal | — | Compensating, approved records that undo money movement; never an edit of the original row | Billing/Payments |
| Cashier session | — | An open-to-close shift with expected against counted totals by mode, a denomination count sheet, variance reason and approval | Billing/Payments |
| Reconciliation batch | — | A cashier-session or provider-settlement comparison of expected against recorded totals by mode, with variance, reason and approver | Billing/Payments |
| Payment mode | — | Configurable tender type with flags for required reference, required provider, refund eligibility and branch availability | Billing/Payments |
| GST | — | India's Goods and Services Tax, applied per line from the tax configuration version in force | Billing/Payments |
| CGST | — | Central GST: the central component charged on an intra-state supply, alongside SGST | Billing/Payments |
| SGST | — | State GST: the state component charged on an intra-state supply, alongside CGST | Billing/Payments |
| IGST | — | Integrated GST: the single component charged instead of CGST and SGST when the supply is inter-state | Billing/Payments |
| Cess | — | An additional component configurable per tax code where it applies | Billing/Payments |
| HSN code | — | Harmonised System of Nomenclature code classifying **goods** for GST, held on tax configuration and stock items | Billing/Payments |
| SAC code | — | Services Accounting Code classifying **services**, such as stitching charges, for GST | Billing/Payments |
| Place of supply | — | The state that decides whether a supply is intra-state — CGST plus SGST — or inter-state — IGST. Recorded on the invoice | Billing/Payments |
| GST registration | — | The branch's GSTIN and state code, used on documents and to derive place of supply | Billing/Payments |
| Tax configuration version | — | The immutable, effective-dated set of tax codes, HSN and SAC mappings, component rates and place-of-supply rules used by a calculation | Billing/Payments |
| Rounding and round-off | — | Line-level half-up rounding to paise and document round-off to the nearest rupee, configurable. Money is `decimal`, never floating point | Billing/Payments |
| Financial year | — | April to March; part of every document sequence key | Platform (sequences) |
| Document artefact | — | The rendered PDF of an invoice, estimate, receipt or credit note, stored under the `documents/` prefix with its version and checksum | Billing/Payments |

---

## 9. Notifications, links and feedback

| Term | Tamil | Definition | Owning module |
| --- | --- | --- | --- |
| Notification intent | — | The decision to notify: event, audience, priority, template version, locale, channel preference, consent requirement, quiet hours, fallback policy and de-duplication key | Notifications/Feedback |
| Notification template version | — | A published, immutable message body for one channel and language, rendered by a logic-less engine in safe mode from a declared variable allowlist | Notifications/Feedback |
| Delivery | — | One attempt to send: queued, accepted, delivered, failed, bounced, suppressed or acknowledged. The rendered body is classified personal and retained by policy | Notifications/Feedback |
| Suppression | — | A send refused for a recorded reason — consent, quiet hours, de-duplication, rate limit or preference — and audited as such | Notifications/Feedback |
| Quiet hours | — | The customer's or branch's configured window during which non-urgent messages are held | Notifications/Feedback |
| Customer link | — | A 128-bit random, purpose-bound, expiring, revocable and rate-limited URL, of which only the SHA-256 hash is stored. Purposes: `estimate`, `status`, `feedback`. Served outside the PWA shell with its own strict policy and redacted from logs | Notifications/Feedback |
| In-app notification | — | The staff-facing notification centre entry; the fallback for push, which iOS delivers only to installed clients | Notifications/Feedback |
| Feedback | karutthu † (கருத்து) | The customer's rating and comments on a delivered order — overall, fit, stitching quality, design match, timeliness — captured through a one-time feedback link with a single edit window | Notifications/Feedback |
| Service-recovery case | — | The follow-up opened automatically when feedback is at or below the configured rating threshold or when an alteration is requested; owned, due-dated, escalated and closed with a confirmation to the customer | Notifications/Feedback |
| Webhook subscription | — | An outbound, signed, replay-protected subscription to versioned integration events for a trusted third-party system | Integration |

---

## 10. Platform, security and operations

| Term | Tamil | Definition | Owning module |
| --- | --- | --- | --- |
| Module | — | A vertically sliced part of the monolith owning exactly one PostgreSQL schema, exposing `Contracts` and events; no module reads another module's tables | Platform |
| Contract | — | The published interface or integration event through which another module may consume a module's data | Owning module of each contract |
| Outbox | — | The per-module table written in the same transaction as the aggregate change, from which a worker dispatches events at least once with leases, retries, back-off and a dead-letter queue | Platform |
| Inbox and idempotency record | — | The de-duplication record that lets an at-least-once handler or a retried command run exactly once in effect. Keyed on principal, route template and `Idempotency-Key` | Platform |
| Dead letter | — | An outbox message that exhausted its retries; replayed only by an authorised, audited operator action | Platform |
| Domain event | — | A past-tense fact inside one module | Owning module |
| Integration event | — | The versioned, published form of a domain event, for example `orders.order-confirmed.v1`, carrying identifiers, codes, statuses, timestamps, amounts and branch codes only | Owning module |
| Audit event | — | The append-only, hash-chained record of who did what, to which resource, why and with which correlation. Written in the same transaction as the mutation, partitioned by month, verified hourly and anchored externally | Platform |
| Correlation id | — | The identifier that ties a request, its events, its logs and its traces together across hosts | Platform |
| Feature flag | — | Organisation- or branch-scoped runtime switch with safe-off defaults, mandatory change reason, evaluation audit and a documented propagation bound | Platform |
| Branch scope | — | The set of branches a principal may act in. Evaluated on every request alongside the permission; a pending cross-branch custody transfer grants the destination branch a narrow, time-limited exception | Platform (security) |
| Permission | — | The atomic right checked by an endpoint, for example `orders.confirm` or `custody.dispatch`; carries `RequiresMfa` and `RequiresStepUp` flags | Platform (security) |
| Step-up | — | Re-authentication with a second factor within the last five minutes, required by sensitive endpoints such as dispatch-exception approval | Identity/Admin |
| Session | — | The server-side ticket referenced by an opaque `HttpOnly` cookie; rotated on login, MFA, step-up and role change, and revocable within the revocation SLO | Identity/Admin |
| BFF — backend for frontend | — | The same-origin host that serves the PWA and `/api/v1`, holds the session and forbids bearer tokens in browser storage | Platform |
| Snapshot | — | A copy of data taken at a decision point so later configuration changes cannot alter settled work | Owning module of each snapshot |
| Projection and read model | — | A derived, rebuildable reporting table with a checkpoint and a visible freshness indicator. **Never authoritative** for financial, stock, workflow or custody state | Reporting |
| Metric dictionary | — | The published definition, source, filter semantics and limitations of every reported figure | Reporting |
| Retention policy | — | The configured lifetime of a class of data, applied by an idempotent worker job that honours legal and business holds and audits each deletion | Platform |
| Data classification | — | The sensitivity class of a data element, which fixes its access, logging, backup and retention treatment | Platform |
| Quarantine | — | The state of an uploaded object before its signature check, malware scan, metadata strip and re-encode succeed; a quarantined object is never served | Media |
| Media object and derivative | — | The stored original and its generated thumbnail and preview, served only by an endpoint that re-authorises every request and streams the bytes | Media |
| Print queue and print station | — | The queued print job and the printer-connected screen that drains its branch's queue, so a phone never drives a thermal printer directly | Platform |
| Health probe | — | `live`, `startup`, `ready` and `detail`; non-essential dependencies report degraded on `detail` and never remove a host from rotation | Platform |
| RPO and RTO | — | Recovery point objective, the tolerable data loss, and recovery time objective, the tolerable outage. Targets are set by issue #19 and are **proposed, to be confirmed** | Platform |
| Synthetic data | — | Generated, non-real data used in development and tests; production refuses synthetic seeding unconditionally | Platform |

---

## 11. Terms deliberately not used

To keep the vocabulary unambiguous, the following words are avoided in this documentation set, the API and the user
interface.

| Avoid | Use instead | Why |
| --- | --- | --- |
| Ticket, work order | Garment job | "Job" is the tracked unit; "order" is the customer commitment |
| Quotation, proforma | Estimate | Only one pre-invoice document exists and it is never a tax document |
| Bill, when a tax document is meant | Invoice | "Bill" is acceptable shop-floor speech but ambiguous in writing |
| Delete | Deactivate, retire, cancel or, for approved classes only, retention deletion | Business records are never soft-deleted or hard-deleted |
| Status, when the ready gate is meant | Ready state | `ready_state` is computed by the gate alone |
| Barcode number | Barcode payload, or display number | The payload is opaque; the display number is a different thing |
| Tenant | Branch | The platform is branch-aware within one organisation; multi-legal-entity tenancy is out of scope |
| Stock count, when a value is meant | Balance, or valuation | Quantity and value are distinct, separately governed figures |

---

## 12. Maintenance

This glossary is amended by pull request only, in the same pull request that introduces or changes the term. When a
term changes meaning, the pull request must also update the documents that use it, the `en-IN` message catalogue
and, where it is a shop-floor word, the `ta-IN` catalogue. The Tamil column is reviewed as a whole by a native
speaker before any `†` is removed; until then no Tamil string in this file may be copied into a customer-facing
message. Terms that are still undecided are not listed here — they are registered in
[`assumptions-and-open-decisions.md`](assumptions-and-open-decisions.md) against plan Section 11.
