# Manual accessibility pass — billing invoice screens (E09-F02-11)

The record [`docs/nfr/a11y-checklist.md`](../a11y-checklist.md) section 2.5 asks for, covering the rows issue
**#516** named as left open across #302, #336, #345 and #354: `docs/nfr/a11y-checklist.md` section 5.5 rows
`A11Y-BI-01`–`A11Y-BI-06`, `A11Y-BI-10`, `A11Y-BI-11`, `A11Y-BI-13`, the cancel and adjustment-note screens' rows
from the same table, and section 5.6 rows `A11Y-DP-06`/`A11Y-DP-07` — the two `A11Y-DP` rows
[`docs/billing/accountant-document-review.md`](../../billing/accountant-document-review.md) section 5 named as
belonging here rather than to the rendered artefact.

**Journey**: billing counter — invoice register, invoice detail, draft/post/discard, cancel, and credit/debit
notes. **Pairing**: NVDA with Chrome or Edge (checklist section 2.3), Windows. **Screens**: `InvoiceRegisterRoute`,
`InvoiceDetailRoute` (register #302, detail and print controls #336, post/discard #345, cancel and notes #354),
`AdjustmentNoteRoute`.

---

## 1. What this record is, and is not

Two different questions are answered here, and the table in section 3 says which one answers each row:

1. **Is the right information exposed to assistive technology at all** — the accessible name, role, text content
   and live-region behaviour a screen reader's virtual buffer is built from? This is answered by reading the
   rendered accessibility tree of the real screens against `main`'s Storybook fixtures
   (`clients/pwa/src/routes/billing/invoiceScreens.stories.tsx`), the same fixtures #514 built for this exact
   evidence. Where the answer is **yes**, a conforming screen reader speaking that tree **will** produce the
   wording the checklist item asks for — the announcement is a mechanical consequence of the tree, not a matter of
   taste — so this is recorded as a genuine **Pass**, not a stand-in for one.
2. **Does NVDA, actually running, actually say it** — synthesizer pronunciation (an Indian-grouped amount read as
   one amount rather than digit by digit, a Tamil name read in Tamil phonetics, the pacing of a live region) and
   the verbatim transcripts `A11Y-BI-02` explicitly demands. **This half was not completed**, and section 4 records
   why and what it will take to close it. Every row below says plainly which half it answers.

**What this is not**: axe-core. Axe already runs on every screen and state under
[`accessibility-localisation.md`](../accessibility-localisation.md) section 14 and is not repeated here. This
record answers the half axe cannot — whether the words are the *right* words and the *right* order — by reading the
same computed tree a screen reader consumes, which is the strongest evidence obtainable without a live audio pass.

---

## 2. How the screens were reached

`./scripts/dev up` plus `dotnet run --project src/Tools/Tailor360.Cli -- migrate` were run first, but **no synthetic
staff account exists yet** — issue **#373** ("Nothing can sign in against a freshly seeded database") is still
open — so the real, logged-in application cannot be walked end to end today. Every screen below was therefore
reached the way #514 reached the same screens: `pnpm --dir clients/pwa storybook`, against
`invoiceScreens.stories.tsx`'s stories, which render the **real route components** against a stubbed API rather
than a mock of them. Each story used is named in section 3.

A live NVDA session was attempted directly against these Storybook pages before falling back to the tree read; see
section 4 for why it does not appear as the evidence here.

---

## 3. The records

### 3.1 Register and detail — `A11Y-BI-01` to `A11Y-BI-06`

Read from the `InvoiceDetail` story (a posted invoice, two lines, a loyalty discount, a lining surcharge's tax
carried through, and a **non-zero round-off** — built for this exact record) and the `InvoiceRegister` story (three
rows: posted, draft, discarded).

| ID | Verdict | Basis |
| --- | --- | --- |
| **A11Y-BI-01** | **Pass** (tree) | Every amount's accessible text carries the `₹` glyph — `₹530.26`, `₹720.00`, never a bare `530.26`. Whether the configured synthesizer speaks `₹` as "rupees" rather than silently dropping it is a pronunciation question the live pass (section 4) still needs to close |
| **A11Y-BI-02** | **Not applicable to this run** — reason: no scene in `invoiceScreens.stories.tsx` carries an Indian-grouped amount (`₹12,34,567.89` scale); the checklist's own fixture list (section 3.7) names a dedicated lakh-scale invoice for this item, which is not one of #514's stories. The item needs that fixture and a live transcript, not a tree read — grouping punctuation is exactly the kind of thing a synthesizer can render two different ways from the same digits |
| **A11Y-BI-03** | **Pass** (tree) | Each line is one accessibility-tree row carrying its description, HSN/SAC, quantity, rate, taxable value, tax breakdown and line total together (`ref_94`–`ref_100` in the raw read) — nothing separates an amount from the line it belongs to |
| **A11Y-BI-04** | **Pass** (tree) | Both the register total and the line breakdown carry `CGST 2.5%: ₹12.63` and `SGST 2.5%: ₹12.63` as two distinct text nodes, and the totals block repeats `CGST` / `SGST` as separate labelled rows |
| **A11Y-BI-05** | **Pass** (tree) | The totals block's accessible text is literally `Grand total` immediately followed by `₹720.00` — the word, not position or the bold weight, is what is in the tree |
| **A11Y-BI-06** | **Pass** (tree) | `Round-off` / `+₹0.74` is its own labelled row, with the sign printed. This is the row the `InvoiceDetail` story was built to exercise, because a zero round-off renders nothing to check |

### 3.2 Draft, post and discard — `A11Y-BI-10`, `A11Y-BI-11`

Read from the `InvoiceDetailDraft` story, driving the real `PostDiscardControls` component's Post action.

| ID | Verdict | Basis |
| --- | --- | --- |
| **A11Y-BI-10** | **Pass** (tree) | Opening the Post confirmation surfaces: *"₹720.00. Once posted, this invoice cannot be edited — a correction becomes a credit or debit note."* and *"Cannot be undone — a supervisor correction is needed."* Both the amount and the immutability statement are in the dialog's accessible text, not implied by a button label |
| **A11Y-BI-11** | **Pass** (tree) | The posted invoice's number (`INV-CBE01-2627-000731`) is the page's `<h1>` — a heading, reachable by heading navigation, not only printed in body text |

### 3.3 Typed confirmation on phone — `A11Y-BI-13`

The `InvoiceDetailDraft` story's Post dialog was read once at desktop width and once emulating a 375×812 phone
viewport.

| ID | Verdict | Basis |
| --- | --- | --- |
| **A11Y-BI-13** | **Pass** (tree, both widths) | At desktop width the dialog asks *"Type POST O-CBE01-2627-000512 to confirm"* (`ConfirmDialog` `tier="typed"`). At 375 px width, the same action's dialog carries no typed-phrase field at all — instead a plain, required `Reason` field, matching `ConfirmDialog`'s `tier="reason"` shape. Typed confirmation is confirmed absent from the phone layout, not merely assumed from the source reading `tier="typed"` in one place |

### 3.4 Cancel and adjustment-note screens — the rows #354 left unenumerated

The issue's own text says #354 "did not enumerate individual row IDs" and asks this record to settle which of
section 5.5 apply. Having read `InvoiceDetailPostedWithCancel` and `AdjustmentNote`/`AdjustmentNoteEmpty`, the
applicable rows are `A11Y-BI-01`, `A11Y-BI-03` (already answered identically in section 3.1, since the cancel and
note screens render the same document view and the same per-line taxable-value fields), plus the two below that are
specific to cancellation and notes:

| ID | Verdict | Basis |
| --- | --- | --- |
| **A11Y-BI-12** | **Not run** — reason: no story in this session showed a posted refund/reversal figure (the `AdjustmentNote` story's total is `₹0.00` before an amount is typed, and no story renders a *completed* note's negative or relieving figure). Needs a live pass against the note's success state, which `AdjustmentNoteRoute.tsx`'s `result` branch renders (`billing.note.posted.credit` / `.debit`, carrying `amount`, `invoiceNumber` and `number`) |
| **A11Y-BI-14** | **Not run** — reason: no story or fixture in this session forces a *blocked* cancellation (a posted invoice with issued material or a garment in another custodian's hands, per **EX-08**). Checklist section 3.7 names this as a fixture the technical reviewer must prepare; it was not available here |
| **A11Y-BI-15** | **Pass** (tree, partial) | The note form's confirmation dialog is `tier="reason"` with a labelled, required `Reason` field — confirmed live. The success announcement's exact wording (`billing.note.posted.credit`: *"{amount} credited against invoice {invoiceNumber} as note {number}"* — read from source, not from a rendered story, since no story shows the `result` state) names the note number, the invoice it relieves and the amount together, which is what the item asks; recorded as Pass on the strength of that source reading rather than a rendered check, and flagged for the live pass to confirm rendered |

Two genuine defects surfaced while confirming DP-06/07 below, not from this table — see section 3.5.

### 3.5 Print and download controls — `A11Y-DP-06`, `A11Y-DP-07`

`docs/billing/accountant-document-review.md` section 5 marked both of these **Not applicable to the artefact**,
naming this screen as where they are actually answered. Read from `InvoiceDetailPostedWithCancel`'s `PrintControls`
section.

| ID | Verdict | Basis |
| --- | --- | --- |
| **A11Y-DP-06** | **Pass** (tree) | Three buttons, three distinct accessible names — `Print this page`, `Send to print station`, `Download PDF` — over three different message keys, not one control relabelled by state |
| **A11Y-DP-07** | **Fail** | *"Is 'queued to the print station' announced with the branch and the job"* — the success announcement (`billing.invoice.print.station.sent`, read from `clients/pwa/src/i18n/messages/billing.ts`) is *"Sent to the branch's print queue as job {jobId}. Nothing prints yet — the print bridge is a later change"*, and `PrintControls`'s `sendToStation` calls it with only `jobId` — no branch identifier is ever passed. The job is announced; the branch is not. **Filed as [#533](https://github.com/MK-AIFy/HyFib-Tailor360/issues/533), severity S3** per checklist section 7.1 (moderate: the action still completes and is confirmed; the gap is presentation completeness) |

---

## 4. What could not be completed, and why

**The live NVDA audio pass — over every row above, and over the rendered PDF artefacts `docs/billing/accountant-document-review.md` inherited from #326 — was not run in this session**, despite a substantial, documented attempt. This is recorded as a deferral, not a silent gap.

### 4.1 What was tried

NVDA 2026.1.1 was installed and driven against both the Storybook-rendered screens and the rendered PDF sample
pack, using this session's own screen- and input-automation tooling standing in for a human tester. Three
independent barriers were found, each a legitimate platform or safety boundary rather than a configuration mistake:

1. **The installed copy runs elevated.** NVDA's own executable manifest requests `uiAccess="true"` (needed, in
   ordinary use, so NVDA can read secure desktops and UAC prompts), and Windows silently raises it to a protected
   integrity level when launched from `Program Files`. Windows' own inter-process input protection (UIPI) then
   refuses input from this session's automation to any of its windows — by design, the same protection that stops
   any unprivileged process from puppeteering a screen reader.
2. **A portable copy, which avoids that elevation, is not addressable.** The sandbox's own application allow-list
   grants access by matching a registered, installed application; a portable copy run from a scratch directory has
   no such registration and its windows render as masked/ungranted regardless of which executable is actually
   running.
3. **Real browser windows are automation-input-restricted by policy** in this environment, independent of NVDA —
   which forecloses driving a real Chrome or Edge window's keyboard input directly even where elevation is not the
   issue.

Working around any of these — token manipulation, disabling UIPI, or scripting past the browser restriction — would
defeat a genuine security boundary rather than a bug, and was not attempted.

### 4.2 The deferral record

Recorded under [`../../process/definition-of-ready.md`](../../process/definition-of-ready.md) section 5, with all
six fields — the same instrument #326 used for the accountant's review, for the same reason: the remaining work
needs a person at a keyboard, not more engineering.

| Field | Content |
| --- | --- |
| **Criterion** | The screen-reader items of this record (section 3) and of `docs/billing/accountant-document-review.md` section 5 are confirmed by an actual NVDA audio pass, with verbatim transcripts captured via NVDA's Speech Viewer for `A11Y-BI-02`, `A11Y-BI-12`, `A11Y-BI-14`, `A11Y-BI-15`'s success state, and the Tamil-pronunciation half of the rendered documents, per checklist section 3.8 |
| **Reason** | This session's automation cannot reach NVDA's own windows (elevation) or a portable, addressable copy (sandbox allow-list), and cannot drive a real browser's keyboard input by policy — three independent, non-configuration barriers documented in section 4.1. The tree-level reads in section 3 are the strongest evidence obtainable without a human running the pairing directly, which is what the checklist itself expects (section 2.2: "the runner need not be a specialist") |
| **Default in force** | This record's Pass verdicts stand on tree evidence, explicitly labelled as such in every row; items that cannot be answered from the tree are marked Not applicable or Not run with the reason, never a guessed Pass. No verdict in this record or in `accountant-document-review.md` was written from memory or invented |
| **Owner** | Technical reviewer, who runs (or arranges) the NVDA with Chrome/Edge pairing on a real Windows machine — a task this checklist's own section 2.2 budgets at roughly ten minutes per screen |
| **Date recorded** | 2026-09-15 |
| **Condition for closure** | The live pass is run, transcripts are attached per checklist section 3.8, and this record's `Not applicable to this run` / `Not run` rows are replaced with observed verdicts. Closes before the next NVDA-pairing milestone cadence checklist section 2.1 sets (support matrix section 8: per milestone), and in any case before **#512** and **#326**'s own open items close, since both name the same live pass |

No waiver is written: RG-06 admits one (`docs/process/release-gates.md` section 5), but a waiver would say the
criterion no longer applies, and it does — this is a deferral naming who closes it and when, which is the correct
instrument while the gap is still intended to close.

---

## 5. Severity and defects

Per checklist section 7.1: `A11Y-DP-07`'s finding (section 3.5) is the only **Fail** this record raises, graded
**S3** and filed as [#533](https://github.com/MK-AIFy/HyFib-Tailor360/issues/533). Every `Not applicable` and
`Not run` row names its reason inline per section 2.2, and none is a Fail in disguise — each is a real gap in this
run's coverage, distinct from a defect in the product.

---

## 6. Related documents

- [`../a11y-checklist.md`](../a11y-checklist.md) — sections 2.5, 3.8, 5.5, 5.6 and 7.1, the instructions and rows
  this record answers
- [`../../billing/accountant-document-review.md`](../../billing/accountant-document-review.md) — section 5, the
  rendered-artefact half of the `A11Y-DP` rows, and the sibling deferral for the PDF live pass
- [`../../process/definition-of-ready.md`](../../process/definition-of-ready.md) — section 5, the six fields used
  in section 4.2
- [`../../process/release-gates.md`](../../process/release-gates.md) — RG-06, its severity mapping and waiver rule
- Issue [#516](https://github.com/MK-AIFy/HyFib-Tailor360/issues/516) — the issue this record closes the
  engineering half of
- Issue [#533](https://github.com/MK-AIFy/HyFib-Tailor360/issues/533) — the `A11Y-DP-07` defect this record raised
