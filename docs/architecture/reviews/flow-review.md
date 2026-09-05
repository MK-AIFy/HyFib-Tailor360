# Architecture flow review — agenda and record

This is the record of the review in which the four representative flows of issue #18 are walked against the
architecture that claims to support them: [`../sequences/order-confirmation.md`](../sequences/order-confirmation.md),
[`../sequences/barcode-handoff.md`](../sequences/barcode-handoff.md),
[`../sequences/invoice-and-payment.md`](../sequences/invoice-and-payment.md) and
[`../sequences/stock-reservation.md`](../sequences/stock-reservation.md). It is both the agenda used to run the
session and the record signed at the end of it. Issue #18 names this review as required verification, and the W0
exit gate of plan [Section 6.2](../../IMPLEMENTATION_PLAN.md) is not met until it is complete and the corrections
it produces have been folded back into the documents.

> **Status: awaiting the review.** The date, the attendees and every sign-off cell below are to be filled in when
> the session runs. Nothing in this file may be cited as a decision until section 7 carries a dated entry.
> **Date of review: _to be filled in_** — the technical reviewer schedules it; the target is before the W0 exit
> gate.

The point of the session is not to admire the diagrams. It is to find the place where a diagram and a real
counter disagree, while changing it still costs a document edit rather than a migration.

---

## 1. Session details

| Field | Value |
| --- | --- |
| Date | _to be filled in_ |
| Start and end time | _to be filled in_ (180 minutes scheduled, one break, one flow per 40 minutes) |
| Location | _to be filled in_ — a room with a screen, and the branch counter reachable within a minute so a disputed step can be walked rather than argued |
| Facilitator | Technical reviewer |
| Note-taker | _to be filled in_ — not the facilitator |
| Chair for decisions | Technical reviewer for architecture; business owner for anything that changes what the shop does |
| Materials | The four sequence documents, [`../invariants.md`](../invariants.md), [`../module-ownership.md`](../module-ownership.md), [`../architecture-rules.md`](../architecture-rules.md), [`../failure-modes.md`](../failure-modes.md), [`../conventions.md`](../conventions.md), [`../../adr/README.md`](../../adr/README.md), [`../../prd/walkthroughs.md`](../../prd/walkthroughs.md) and [`../../prd/state-transitions.md`](../../prd/state-transitions.md) |
| Recording | Written notes only. No photograph, screen recording or audio recording that could capture a customer's name, phone number or measurements |

---

## 2. Attendees

An architecture review attended only by the people who wrote the architecture is a proofreading exercise. Each row
below must be filled by someone who can say "that is not what happens" and be believed.

| Perspective | Name | Why they are here | Present | Confirms the record | Date |
| --- | --- | --- | --- | --- | --- |
| Technical reviewer | _to be filled in_ | Owns the architecture rules and chairs the decisions | ☐ | ☐ | |
| Backend engineer | _to be filled in_ | Will implement the transaction boundaries being reviewed | ☐ | ☐ | |
| Client engineer | _to be filled in_ | Owns the offline queue and what the counter sees when a step fails | ☐ | ☐ | |
| Reception | _to be filled in_ | Performs order confirmation daily; the only person who knows what the counter actually does under queue pressure | ☐ | ☐ | |
| Tailor Master | _to be filled in_ | Performs the phase handoffs the barcode flow models | ☐ | ☐ | |
| Cashier | _to be filled in_ | Performs invoice posting and payment, and meets the dispatch gate | ☐ | ☐ | |
| Inventory Clerk | _to be filled in_ | Performs reservation, issue and stock-take, and receives the low-stock alert | ☐ | ☐ | |
| Business owner | _to be filled in_ | Decides anything that changes what the shop does | ☐ | ☐ | |

A flow whose operational role is absent is **not reviewed**. Mark it deferred in section 7 and reconvene for that
flow rather than recording a sign-off nobody qualified gave.

---

## 3. Agenda

| # | Minutes | Item |
| --- | --- | --- |
| 1 | 10 | What this session is for, and the rule that "the counter wins": where a document and daily practice disagree, the document is wrong until someone shows otherwise |
| 2 | 40 | Flow 1 — order confirmation and barcode allocation |
| 3 | 40 | Flow 2 — barcode handoff at a phase boundary |
| 4 | 10 | Break |
| 5 | 40 | Flow 3 — invoice posting, payment and the dispatch gate |
| 6 | 40 | Flow 4 — stock reservation, issue, consumption and the low-stock alert |
| 7 | 15 | The cross-cutting questions of section 5, taken once across all four flows |
| 8 | 15 | Decisions, actions and what changes before the W0 exit gate |

Each flow is walked in the same order: read the sequence aloud step by step, stop at every transaction boundary,
then take the five questions of section 4.1.

---

## 4. Per-flow sign-off

A flow is signed off only when all five questions of section 4.1 are answered and the operational role named in
section 2 agrees the flow is what they do.

| Flow | Document | Operational role that must agree | Walked | Failure modes walked | Role agrees | Defects raised | Signed |
| --- | --- | --- | --- | --- | --- | --- | --- |
| 1. Order confirmation and barcode allocation | [`../sequences/order-confirmation.md`](../sequences/order-confirmation.md) | Reception | ☐ | ☐ | ☐ | _to be filled in_ | ☐ |
| 2. Barcode handoff at a phase boundary | [`../sequences/barcode-handoff.md`](../sequences/barcode-handoff.md) | Tailor Master | ☐ | ☐ | ☐ | _to be filled in_ | ☐ |
| 3. Invoice posting, payment and the dispatch gate | [`../sequences/invoice-and-payment.md`](../sequences/invoice-and-payment.md) | Cashier | ☐ | ☐ | ☐ | _to be filled in_ | ☐ |
| 4. Stock reservation, issue, consumption and the low-stock alert | [`../sequences/stock-reservation.md`](../sequences/stock-reservation.md) | Inventory Clerk | ☐ | ☐ | ☐ | _to be filled in_ | ☐ |

### 4.1 The five questions asked of every flow

| # | Question | Why it is asked | Where the answer lives |
| --- | --- | --- | --- |
| Q1 | Does every step name exactly one owning module, and does no step reach into another module's tables? | A step with two owners is a boundary that will be crossed the first time somebody is in a hurry | [`../module-ownership.md`](../module-ownership.md), rule ARCH-001 in [`../architecture-rules.md`](../architecture-rules.md) |
| Q2 | Is every transaction boundary drawn where the invariant it protects actually lives, and is anything spanning two of them published through the outbox rather than called directly? | This is where a dual write hides, and a dual write is how the ledger and the invoice stop agreeing | Section 4 of each sequence document, [`../invariants.md`](../invariants.md) §6 |
| Q3 | For every failure listed, does the compensating action leave the system in a state the operational role can actually work from? | "Roll back and show an error" is not an answer when a customer is standing at the counter holding cloth | Section 6 or 7 of each sequence document, [`../failure-modes.md`](../failure-modes.md) |
| Q4 | Is the step idempotent where a person or a device will retry it, and does the second attempt produce the same result rather than a second effect? | A tapped button, a queued offline scan and a flaky connection all replay; a duplicated custody event or payment is not recoverable by apology | Section 7 of order-confirmation, section 4 of barcode-handoff, [`../conventions.md`](../conventions.md) §4 |
| Q5 | Is what is written to the audit trail enough to answer "who did this, when, on whose authority, and what did it look like before?" a year later? | An audit row that omits the before-state cannot settle a dispute, which is the only reason it exists | Section 5 or 6 of each sequence document, [`../invariants.md`](../invariants.md) |

### 4.2 Steps that must actually be performed, not described

Four claims in these flows are about the physical world, and a room full of people agreeing they sound right is
not evidence. Each must be performed during the session.

| # | What must be performed | The claim it tests | Flow |
| --- | --- | --- | --- |
| P1 | Print one label, stick it on a real bundle of cloth, put the bundle on the rack, and scan it from the rack | That the label survives handling and is scannable where the bundle actually sits, not where a photograph was taken | 1, 2 |
| P2 | Put the device in aeroplane mode, perform a handoff scan, restore the connection, and watch the queue drain | That the offline queue is real and that the person can tell whether their scan has been accepted | 2 |
| P3 | Attempt a dispatch scan on an unpaid order | That the dispatch gate refuses, and that the refusal tells the Delivery Staff what to do next rather than only that it failed | 3 |
| P4 | Issue material against a confirmed order, then complete the phase, then reconcile the balance against the ledger | That the balance is genuinely derived, and that a mismatch is visible rather than silently absorbed | 4 |

---

## 5. Cross-cutting questions, taken once

These are asked once across all four flows rather than four times.

| # | Question | Answer | Follow-up |
| --- | --- | --- | --- |
| C1 | Does any flow require a module to depend on another in a direction [`../module-ownership.md`](../module-ownership.md) forbids? | _to be filled in_ | |
| C2 | Does any flow require data that no module owns, or that two modules both claim? | _to be filled in_ | |
| C3 | Is there a step where the branch scope of the actor is assumed from a claim rather than evaluated against the resource? | _to be filled in_ | |
| C4 | Does any flow depend on a rule that is specified but not yet enforced — ARCH-013, ARCH-017, ARCH-018 or ARCH-019 — in a way that makes the deferral unsafe rather than merely untidy? | _to be filled in_ | |
| C5 | Does any flow contradict an accepted architecture decision record, and if so which is wrong? | _to be filled in_ | |
| C6 | Is there a step whose latency budget in [`../../nfr/capacity-and-performance.md`](../../nfr/capacity-and-performance.md) the flow cannot meet as drawn? | _to be filled in_ | |
| C7 | Which of the four flows would be hardest to extract into a separate service later, and does that agree with the extraction criteria of [`../../adr/0001-modular-monolith.md`](../../adr/0001-modular-monolith.md) §6? | _to be filled in_ | |

---

## 6. Failure-mode review

Issue #18 also requires a review of the failure modes for the database, object storage and background processing.
[`../failure-modes.md`](../failure-modes.md) is the analysis; this section is where it is reviewed rather than
merely written.

| # | Failure | Document section | Reviewed | The question the session must answer | Outcome |
| --- | --- | --- | --- | --- | --- |
| F1 | PostgreSQL is unavailable | [`../failure-modes.md`](../failure-modes.md) §4 | ☐ | What does Reception see, and can the shop keep taking orders on paper in a way the system can absorb afterwards? | _to be filled in_ |
| F2 | Object storage is unavailable | §5 | ☐ | Can an order be confirmed when the reference photograph cannot be stored, and is that the right answer? | _to be filled in_ |
| F3 | The worker host is stopped | §6 | ☐ | How long before anyone notices, and which of the four flows degrades first? | _to be filled in_ |
| F4 | The malware scanner is down | §7 | ☐ | Is refusing the upload the right trade, and for how long is it tolerable? | _to be filled in_ |
| F5 | A provider is failing | §8 | ☐ | Does a failed notification ever block a garment leaving, and should it? | _to be filled in_ |
| F6 | A device is offline | §9 | ☐ | What is the longest offline period the queue is designed to survive, and what happens beyond it? | _to be filled in_ |

---

## 7. Decisions log

Every decision taken at the session, with the person who took it. A decision not written here did not happen.

| # | Decision | Taken by | Date | Documents that must change |
| --- | --- | --- | --- | --- |
| _to be filled in_ | | | | |

### 7.1 Questions the session must answer

These leave the session decided or explicitly deferred with an owner and a date. None may simply lapse.

| # | Question | Why it cannot wait |
| --- | --- | --- |
| AQ-01 | When the barcode label prints but the print job is never confirmed, is the garment job blocked or does it proceed with a reprint? | The answer decides whether the print queue is on the critical path of order confirmation |
| AQ-02 | May an order be confirmed while object storage is unavailable, with the images uploaded later? | Decides whether Media is a hard or soft dependency of Orders, which is an architecture rule, not a preference |
| AQ-03 | When a scan contradicts the system's belief about the current holder, does the scan win, does the system win, or is a reconciliation case opened and the garment held? | Decides whether custody is advisory or authoritative, and therefore what the custody chain is worth in a dispute |
| AQ-04 | Is the low-stock alert evaluated on the reserved balance or the physical balance? | The two diverge exactly when it matters, on a day of heavy confirmation |
| AQ-05 | Does a refund reopen the dispatch gate on an already-delivered order? | Decides whether the gate reads a payment state or a delivery state |
| AQ-06 | Which of the four flows must keep working when the worker host is stopped? | Decides what may be published asynchronously and what must be synchronous |

---

## 8. Actions log

| # | Action | Owner | Due | Done |
| --- | --- | --- | --- | --- |
| _to be filled in_ | | | ☐ |

---

## 9. What happens after the session

1. The note-taker completes sections 4 to 8 within one working day, while the session is still recoverable from
   memory.
2. Every defect raised becomes an issue, linked to #18, or a correction to the document it contradicts. A defect
   recorded only in this file is a defect nobody will fix.
3. The corrections land through a pull request that references this record, so the diagrams and this document
   cannot drift apart.
4. Section 2's confirmation column is completed by each attendee **after** reading the written record, not at the
   table. Agreement in a room and agreement with a written record are different things.
5. The W0 exit gate is met for this item only when every row of section 4 is signed and every action in section 8
   is either done or has an owner and a date.

---

## 10. Maintenance

This record is written once and then frozen; it is the record of one session, not a living document. A later
review of the same flows — after a material architecture change, or when a fifth flow is added — gets its own file
alongside this one, named for its date. Corrections to the architecture itself belong in the architecture
documents, which this session exists to improve.

---

## 11. Related documents

| Document | Why it matters here |
| --- | --- |
| [`../sequences/order-confirmation.md`](../sequences/order-confirmation.md) | Flow 1 |
| [`../sequences/barcode-handoff.md`](../sequences/barcode-handoff.md) | Flow 2 |
| [`../sequences/invoice-and-payment.md`](../sequences/invoice-and-payment.md) | Flow 3 |
| [`../sequences/stock-reservation.md`](../sequences/stock-reservation.md) | Flow 4 |
| [`../failure-modes.md`](../failure-modes.md) | The analysis reviewed in section 6 |
| [`../module-ownership.md`](../module-ownership.md) | The ownership question Q1 asks |
| [`../invariants.md`](../invariants.md) | The invariants questions Q2 and Q5 test |
| [`../architecture-rules.md`](../architecture-rules.md) | The rules question C4 asks about |
| [`../conventions.md`](../conventions.md) | Concurrency and idempotency conventions behind Q4 |
| [`../../adr/README.md`](../../adr/README.md) | The decisions question C5 checks against |
| [`../../prd/reviews/exception-review.md`](../../prd/reviews/exception-review.md) | The sibling review, run with the same roles on the exception catalogue |
| [`../../nfr/reviews/stakeholder-review.md`](../../nfr/reviews/stakeholder-review.md) | The sibling review that puts numbers on the non-functional targets |
| [`../../IMPLEMENTATION_PLAN.md`](../../IMPLEMENTATION_PLAN.md) | Section 6.2, the W0 exit gate this record is evidence for |
