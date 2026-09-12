# Billing — go-live plan

The plan for taking the Billing module from where it stands today to a billing tool a counter can go live on. It
is written against the three feature issues of epic #13 — #41 (pricing and GST engine), #42 (invoices, numbering,
PDFs, immutability) and #43 (payments, receipts, cashier, dispatch gate) — and against the module's own rules in
[`../../src/Modules/CLAUDE.md`](../../src/Modules/CLAUDE.md) and the ownership in
[`../architecture/module-ownership.md`](../architecture/module-ownership.md) section 5.8. Where this document and
an issue disagree, the issue wins and this document is corrected.

It is deliberately a sequence, not a wish list: each slice is one pull request, each pull request is under the
1,500-line target and lands behind the previous one, and the tool is usable at the counter from the end of slice
3 — not from the end of the epic.

---

## 1. Where the module stands

| Piece | Issue | State on `main` | What exists |
| --- | --- | --- | --- |
| Tax configuration versions, tax codes and components, GST registrations | #145 (of #41) | **Merged** | Aggregates, immutability triggers, admin routes, publication checks, unit and integration tests |
| Price lists, versions, items, discount rules, catalogue link validator | #146 (of #41) | **Merged** (#150, review fixes #151) | Aggregates, one-published-per-branch constraint, admin routes, publication checks, tests |
| The pricing engine: `IPricingService`, calculation snapshots, overrides, golden master | #147 (of #41) | **Merged** (#152) | The contract, the pure engine, append-only snapshots keyed by the caller's reference, the preview route, the golden master the accountant signs |
| Invoices, numbering, immutability, credit notes, PDF, barcode lookup | #42 | Not started | The permissions and the section 5 matrix rows are reserved |
| Payments, allocations, advances, receipts, cashier, dispatch gate | #43 | Not started | The permissions and the section 5 matrix rows are reserved |
| Client screens for billing | (an issue under #41 once the API is stable) | Not started | `clients/pwa/src/stories/fixtures/billing.ts` only |

With #147 merged, **#41 is complete in code** and closes when its parent epic's owner accepts the outcome: the
accountant-signed golden master (section 2.7) is the one artefact still outstanding.

Three facts shape everything below:

- **Billing never references Orders** (ARCH-010). Order facts arrive as command input — the priced lines and the
  identifiers the caller sends — and as versioned integration events. `IPricingService` takes no order entity,
  which is why #42 drafts an invoice from a calculation snapshot and the caller's identifiers, and why Orders
  adopting the contract at estimate and confirmation is an Orders change under #32/#33, not a Billing one.
- **A calculation is pinned to its configuration versions** (INV-INV-03). Every figure a document shows
  reproduces from its `calculation_snapshots` row with the price-list, tax configuration and registration it
  names — the row is append-only, keyed by the caller's reference, unique per organisation, and carries a schema
  version. An invoice never recalculates; it reads the snapshot.
- **No statutory number lives in code.** Rates, classifications and whether a cess applies are what the
  accountant enters into a tax configuration version; the engine encodes only what would make a calculation
  contradict itself.

---

## 2. The slices, in the order they land

| # | Slice | Issue | Branch | Ships |
| --- | --- | --- | --- | --- |
| 1 | Invoice aggregate: draft from a calculation snapshot, post with a gap-free number, immutable at the database, cancellation record and credit note, list and read routes, `InvoicePosted` and `InvoiceCancelled` events | #42 (part a) | `feat/e09-f02a-invoices-posting` | Next |
| 2 | Invoice PDF and print view, the `I-…` barcode and its authorised lookup, the `ITimelineSource`, the `invoice.issued` notification intent | #42 (part b) | `feat/e09-f02b-invoice-documents` | After 1 |
| 3 | Payment modes, payments, allocations, advances, receipts with `R-…` numbers, the balance (`IFinancialTotalsQuery`), `PaymentRecorded`/`Allocated`/`AdvanceReceived`/`InvoicePaidStatusChanged` events | #43 (part a) | `feat/e09-f03a-payments-receipts` | After 1 |
| 4 | Refunds and reversals, cashier sessions with the denomination sheet and reconciliation, `IDispatchEligibilityQuery` and the single-use dispatch exception | #43 (part b) | `feat/e09-f03b-cashier-dispatch-gate` | After 3 |
| 5 | Client: price and tax administration with the preview, the invoice screen, take payment, cashier open and close | client issue under #41 | `feat/e09-f04-billing-screens` | Alongside 3 and 4 |
| 6 | Go-live: reference data, accountant sign-off of the golden master, staging migration, UAT walkthrough, release-gate evidence | release | — | Last |

**The counter can bill from the end of slice 3.** An administrator publishes a tax configuration and a price list
(#145, #146), the engine prices the lines and stores the snapshot (#147), the counter drafts an invoice from that
snapshot and posts it (slice 1), records the advance and the balance and hands over a numbered receipt (slice 3).
The PDF (slice 2) and the cashier close and dispatch gate (slice 4) follow; until slice 4 merges, dispatch stays
on the manual rule the shop runs today, and Custody's gate keeps failing closed on `NotEvaluated`.

### 2.1 What #147 gives the slices below

The contract and its behaviour, as merged, so the next branch is cut against what is there rather than what the
issue asked for:

- `IPricingService.PriceAsync(PricingRequest)` prices lines keyed by the caller (`LineKey`) against the published
  price-list version covering the branch on `On`, the published tax configuration and the registration in force;
  a request carrying a `Reference` is stored once under it and answered again unchanged, the same reference with a
  different body is `billing.snapshot-conflict`, and `FindSnapshotAsync(organisationId, reference)` reads it back.
  A request without a reference is priced and forgotten.
- `PricingResult` carries the three version identifiers, the `SupplyScheme`, whether the rates were inclusive,
  every line's components (base, surcharges, discount, gross, taxable value, tax code and classification, tax
  components, line total, variance from the catalogue, whether an approval was exercised) and the nine document
  totals Orders already stores as `PricedTotals`.
- Rounding is [`../architecture/conventions.md`](../architecture/conventions.md) section 1.2: half away from zero
  to paise once per printed component, sums never re-rounded, the document round-off explicit; an inclusive rate
  backs the rounded components out of the quoted amount so the customer pays the quoted price to the paisa.
- A surcharge is a surcharge-kind item carrying the line item's tax code; anything else on a line's surcharge
  list is refused. A discount above its rule's counter maximum, or an override beyond the version's threshold,
  needs `billing.override_price` with a fresh step-up and a reason, and the exercised approval is audited; a
  discount above the rule's maximum is refused outright.
- Missing configuration is `billing.configuration-missing` naming what — including a version published ahead of
  its first day, which is the **OD-21** default (section 4).
- `POST /api/v1/billing/pricing/preview` runs the engine against a named version, draft or published, on
  `billing.manage_price_lists`, and stores nothing.

### 2.2 Slice 1 — invoices and posting

- Tables: `invoices`, `invoice_lines`, `invoice_tax_components`, `invoice_cancellations`, `credit_notes`,
  `document_sequences` (allocated through Platform's `ISequenceAllocator`, scope branch × financial year).
- `POST /api/v1/billing/invoices` (`billing.create_invoice`, `Idempotency-Key`): a draft from a calculation
  snapshot named by its reference, plus the caller's order and garment-job identifiers, with the bill-to party
  and place of supply from `ICustomerSnapshotQuery` and the registration the snapshot names. The lines and totals
  are copied from the snapshot, never recalculated: `billing.snapshot-mismatch` is what a draft answers when the
  snapshot's versions are no longer the ones in force and the caller has not re-priced. The order-driven
  conversion (`/from-order/{orderId}`) waits for Orders to adopt the contract; until then the counter sends the
  reference it priced under.
- `POST /api/v1/billing/invoices/{invoiceId}/post` (`billing.post_invoice`): allocates the number under the
  sequence row lock in the same transaction, freezes the row, writes `billing.invoice-posted.v1` to the outbox
  carrying the persisted tax components so no consumer recalculates.
- A database trigger refuses every `UPDATE` and `DELETE` on a posted row, in the shape of
  `calculation_snapshots_are_append_only`; cancellation is an appended `invoice_cancellations` row plus a credit
  note where value was recognised, both under `billing.cancel_invoice` with step-up and a reason.
- Number format `INV-<branch>-<FY>-000001`; the financial-year token is `2627` as
  [`../architecture/conventions.md`](../architecture/conventions.md) section 2.3 proposes, **to be confirmed with
  the accountant (COD-03)** before the first production posting.
- Routes: list by branch and status, read one, draft, post, cancel, credit note; each with its section 5 row, and
  every one with a route parameter declaring `ScopedToResource("billing.invoice", …)` (ARCH-023).

### 2.3 Slice 2 — documents

- `IPdfRenderer` template for the invoice (A4, Tamil-capable font, tagged where the renderer supports it),
  `document_artifacts` with checksum under `documents/`, the in-app print view, `IPrintQueue` hand-off.
- `GET /api/v1/billing/barcodes/{payload}` resolving an `I-…` payload under authorisation only.
- `ITimelineSource` for invoices and notes; `invoice.issued` and `credit_note.issued` intents behind the
  `notifications.email` flag.
- `PdfTotalsMatchSnapshot` over the golden fixtures; a cancelled invoice still renders its original PDF.

### 2.4 Slice 3 — payments and receipts

- Tables: `payment_modes` (seeded cash, card, UPI, bank transfer, other), `payments`, `payment_allocations`,
  `advances`, `receipts`; `payments`, `payment_allocations` and `receipts` append-only by trigger.
- `POST /api/v1/billing/payments` (`payments.record`, `Idempotency-Key`), deterministic allocation oldest
  invoice first, `payments.allocate_manual` for a hand allocation with step-up and a reason.
- Receipts numbered `RCPT-<branch>-<FY>-000001` with an `R-…` barcode; `billing.print_receipt`.
- `IFinancialTotalsQuery` for the balance: posted charges − allocations − credits + refunds.
- Card data: only the masked last four, network, provider reference and authorisation code; a validator refuses a
  13–19 digit numeric value in any reference field.

### 2.5 Slice 4 — cashier and the dispatch gate

- `refunds`, `reversals` (compensating, approved), `cashier_sessions`, `cashier_session_counts`,
  `reconciliation_batches`, `dispatch_exceptions`.
- `IDispatchEligibilityQuery` from Billing's own records only, fails closed; the single-use exception bound to
  order, job set, maximum outstanding amount, policy version and expiry, approver ≠ dispatcher.

### 2.6 Slice 5 — screens

Price and tax administration (versions, the preview against a draft, publish with step-up), the invoice screen
(draft, post, print), take payment (amount with `inputmode="decimal"`, quick-fill Balance and Advance, cash
tendered → change, mode as large segmented buttons, receipt share), cashier open and close with the denomination
sheet.

### 2.7 Slice 6 — go-live

| Gate | Evidence |
| --- | --- |
| Golden master signed off | The accountant's cases in `tests/fixtures/billing/pricing-golden-master.json` pass, and the accountant has initialled the fixture file (OD-05) |
| Tax configuration published | A version with the shop's codes and rates, entered by the accountant; no rate in a seed |
| Price list published per branch | Every service the catalogue offers resolves to an item; the catalogue publish check passes; the preview against the version shows the walkthrough figures |
| Registrations in force | One per branch on the go-live date |
| Migrations | `migrate --dry-run` then `migrate` on staging, output in the release record; every `Down` executed once |
| UAT | The walkthrough in `docs/prd/walkthroughs.md` section 2 run end to end on staging with synthetic data |
| Release gates | `docs/process/release-gates.md`, with any waiver recorded |

---

## 3. Decisions the slices take, and decisions they do not

Taken under the conventions document, reversible, and already recorded with #147:

| Decision | Position | Why it is safe |
| --- | --- | --- |
| Rounding mode | Half away from zero, once per printed component | It is what `conventions.md` section 1.2 states; the golden master asserts it, so a change is a fixture change |
| Inclusive lines | The quoted amount is the line total; the rounded components are backed out and the taxable value is the remainder | Recorded under OD-05 with #147; the customer pays the quoted amount to the paisa |
| Invoice figures | Copied from the snapshot, never recalculated | INV-INV-03: a document reproduces from the row it was drafted from |

Not taken here, because they are product decisions with an owner in
[`../prd/assumptions-and-open-decisions.md`](../prd/assumptions-and-open-decisions.md):

| Decision | Blocks | Default shipped until decided |
| --- | --- | --- |
| **OD-05** the accountant's sign-off of the golden master | Slice 6 | The fixture as merged with #147 |
| **OD-21** a version published ahead of its first day | Nothing today; slice 1 inherits it | The engine refuses with the date named, and the predecessor is retired on publication. Choosing "the predecessor stays in force until the successor's day" later is a change to the read, not to any stored figure |
| **COD-03** financial-year token | Slice 1's first production number | `2627` for 2026-27 |
| **OD-04** whether an advance is refundable on cancellation | Slice 4 | Refund is a compensating record under `payments.refund` with step-up; the policy is who may |
| **OD-06** one price list or one per branch | Nothing: both are supported | — |
| **XQ-02** who may approve a dispatch exception | Slice 4 | Owner only |

---

## 4. Evidence per slice

Every slice's pull request carries the tier output for unit, architecture, contract and integration, run twice;
the migration `--dry-run` and apply output and a register row in `docs/dev/migrations.md` for a schema change;
the regenerated `docs/api/openapi.v1.json` and `clients/pwa/src/api/schema.d.ts`; and a section 5 row in
[`../security/permission-matrix.md`](../security/permission-matrix.md) for every route. A screen also carries
the phone, tablet and desktop screenshots and the Storybook states the pull-request template lists.
