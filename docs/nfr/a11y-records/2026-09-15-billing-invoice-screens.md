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

**The screen-reader pairing was not run in this session.** Section 4 says why in full: this session's automation
cannot reach NVDA's own windows once elevated, cannot address a non-elevated copy through the sandbox's application
allow-list, and cannot drive a real browser's keyboard input by policy. Per
[`../a11y-checklist.md`](../a11y-checklist.md) section 2.2's own rule for exactly this situation — *"When a piece of
kit is missing: the affected items are recorded `N/A — kit unavailable: <item>` and the run does not count as
covering them"* — every `SR`-marked row below is recorded **Not applicable — kit unavailable**, not Pass, however
strong the surrounding evidence.

What *was* done, and is recorded as its own kind of evidence rather than folded into a verdict: the real screens'
computed accessibility tree was read (Storybook, against #514's fixtures — see section 2), and where the source
code itself settles a question with no audio needed — a live region exists or it does not, a dialog's text contains
a given sentence or it does not, a control fires a heading or it does not — that is recorded as a genuine
engineering finding, including two outright defects (section 3.5). None of this substitutes for a human hearing
NVDA say the words; it only says what a correctly speaking screen reader would have to work with, and where it
would have nothing to say at all.

**`K`-marked rows are unaffected** by the missing pairing — the checklist itself answers `A11Y-BI-13` from the
keyboard pass alone, screen reader off, and it is recorded as an observed Pass or Fail below on that basis.

**What this is not**: axe-core. Axe already runs on every screen and state under
[`accessibility-localisation.md`](../accessibility-localisation.md) section 14 and is not repeated here.

---

## 2. How the screens were reached

`./scripts/dev up` plus `dotnet run --project src/Tools/Tailor360.Cli -- migrate` were run first, but **no synthetic
staff account exists yet** — issue **#373** ("Nothing can sign in against a freshly seeded database") is still
open — so the real, logged-in application cannot be walked end to end today. Every screen below was therefore
reached the way #514 reached the same screens: `pnpm --dir clients/pwa storybook`, against
`invoiceScreens.stories.tsx`'s stories, which render the **real route components** against a stubbed API rather
than a mock of them. Each story used is named in section 3.

---

## 3. The records

### 3.1 Register and detail — `A11Y-BI-01` to `A11Y-BI-06`

Read from the `InvoiceDetail` story (a posted invoice, two lines, a loyalty discount, a lining surcharge's tax
carried through, and a **non-zero round-off** — built for this exact record) and the `InvoiceRegister` story (three
rows: posted, draft, discarded). All six rows are `SR` in the checklist.

| ID | Verdict | Engineering observation (not a substitute for the verdict) |
| --- | --- | --- |
| **A11Y-BI-01** | **N/A — kit unavailable: NVDA pairing** | Every amount's accessible text carries the `₹` glyph — `₹530.26`, `₹720.00`, never a bare `530.26`. Whether the configured synthesizer speaks it as "rupees" is exactly the kind of thing the live pass exists to confirm |
| **A11Y-BI-02** | **N/A — kit unavailable: NVDA pairing**, compounded by **no fixture**: no scene in `invoiceScreens.stories.tsx` carries an Indian-grouped amount at lakh scale. Checklist section 3.7 names a dedicated fixture for this item; it is not one of #514's stories | — |
| **A11Y-BI-03** | **N/A — kit unavailable: NVDA pairing** | Each line is one accessibility-tree node grouping description, HSN/SAC, quantity, rate, taxable value, tax breakdown and line total together — nothing in the tree separates an amount from its line |
| **A11Y-BI-04** | **N/A — kit unavailable: NVDA pairing** | Both the line breakdown and the totals block carry `CGST 2.5%: ₹12.63` and `SGST 2.5%: ₹12.63` (or the totals' `CGST`/`SGST` rows) as two distinct text nodes |
| **A11Y-BI-05** | **N/A — kit unavailable: NVDA pairing** | The totals block's accessible text is literally `Grand total` immediately followed by `₹720.00` |
| **A11Y-BI-06** | **N/A — kit unavailable: NVDA pairing** | `Round-off` / `+₹0.74` is its own labelled row with the sign printed — the row the `InvoiceDetail` story exists to exercise, since a zero round-off renders nothing |

### 3.2 Draft, post and discard — `A11Y-BI-10`, `A11Y-BI-11`

Read from the `InvoiceDetailDraft` story, driving the real `PostDiscardControls` component's Post action. Both rows
are `SR`.

| ID | Verdict | Engineering observation |
| --- | --- | --- |
| **A11Y-BI-10** | **N/A — kit unavailable: NVDA pairing** | The Post confirmation's accessible text includes *"₹720.00. Once posted, this invoice cannot be edited — a correction becomes a credit or debit note"* and *"Cannot be undone — a supervisor correction is needed."* Both sentences are present in the tree for a screen reader to reach |
| **A11Y-BI-11** | **N/A — kit unavailable: NVDA pairing** | The posted invoice's number (`INV-CBE01-2627-000731`) is the page's `<h1>`, reachable by heading navigation, not only printed in body text |

### 3.3 Typed confirmation on phone — `A11Y-BI-13`

`K` only — screen reader off, keyboard and viewport alone answer it, so the missing pairing does not apply. Read
from the `InvoiceDetailDraft` story's Post dialog, once at desktop width and once emulating a 375×812 phone
viewport.

| ID | Verdict | Basis |
| --- | --- | --- |
| **A11Y-BI-13** | **Pass** (observed, keyboard pass) | At desktop width the dialog asks *"Type POST O-CBE01-2627-000512 to confirm"* (`ConfirmDialog` `tier="typed"`). At 375 px width, the same action's dialog carries no typed-phrase field — a plain, required `Reason` field instead (`tier="reason"`). Typed confirmation is confirmed absent from the phone layout by direct comparison, not assumed from reading `tier="typed"` in one place |

### 3.4 Cancel and adjustment-note screens — the rows #354 left unenumerated

The issue's own text says #354 "did not enumerate individual row IDs" and asks this record to settle which of
section 5.5 apply. Having read `InvoiceDetailPostedWithCancel` and `AdjustmentNote`/`AdjustmentNoteEmpty`, the
applicable rows are `A11Y-BI-01` and `A11Y-BI-03` (already recorded identically in section 3.1, since these screens
render the same document view and the same per-line taxable-value fields), plus three specific to cancellation and
notes — all `SR`:

| ID | Verdict | Engineering observation |
| --- | --- | --- |
| **A11Y-BI-12** | **N/A — kit unavailable: NVDA pairing**, compounded by **no fixture**: no story renders a *completed* note's negative or relieving figure (`AdjustmentNote`'s total is `₹0.00` before an amount is typed) | — |
| **A11Y-BI-14** | **N/A — kit unavailable: NVDA pairing**, compounded by **no fixture**: no story or fixture forces a *blocked* cancellation (**EX-08**: a posted invoice with issued material or a garment in another custodian's hands). Checklist section 3.7 names this as a fixture the technical reviewer must prepare | — |
| **A11Y-BI-15** | **N/A — kit unavailable: NVDA pairing** | The note form's confirmation dialog is `tier="reason"` with a labelled, required `Reason` field, confirmed live. The success message's wording — `billing.note.posted.credit`/`.debit`, read from `AdjustmentNoteRoute.tsx` source rather than a rendered story, since no story reaches the `result` state — names the amount, the invoice relieved and the note number together in one sentence, which is what the item asks; whether NVDA actually speaks it that way is unconfirmed |

### 3.5 Print and download controls — `A11Y-DP-06`, `A11Y-DP-07`

`docs/billing/accountant-document-review.md` section 5 marked both of these **Not applicable to the artefact**,
naming this screen as where they are actually answered. Read from `InvoiceDetailPostedWithCancel`'s `PrintControls`
section **and** from `InvoiceDetailRoute.tsx` source, since both rows turn on whether an announcement mechanism
exists in the code at all — a fact no amount of audio evidence changes.

| ID | Verdict | Basis |
| --- | --- | --- |
| **A11Y-DP-06** | **Fail** | Two-part question — distinct names **and** an announced outcome for each of Print, Send to print station and Download PDF; per checklist section 4, any failing part fails the item. The three names are distinct (`Print this page`, `Send to print station`, `Download PDF`, three message keys). The outcome half fails for **Download**: `PrintControls`'s `download` function calls `saveBlob` and renders only `<BillingProblemAlert failure={downloadFailure} />` — there is no success announcement at all in the source, so no screen reader, however it is run, has anything to say when a download succeeds. `window.print()` ("Print this page") delegates to the browser's own native print dialog; whether that counts as an announced outcome is left for the live pass to judge rather than guessed at here. **Filed as [#535](https://github.com/MK-AIFy/HyFib-Tailor360/issues/535), severity S3** |
| **A11Y-DP-07** | **Fail** | *"Is 'queued to the print station' announced with the branch and the job"* — the success announcement (`billing.invoice.print.station.sent`, read from `clients/pwa/src/i18n/messages/billing.ts`) is *"Sent to the branch's print queue as job {jobId}. Nothing prints yet — the print bridge is a later change"*, and `sendToStation` calls it with only `jobId` — no branch identifier is ever passed. **Filed as [#533](https://github.com/MK-AIFy/HyFib-Tailor360/issues/533), severity S3** |

---

## 4. Why the pairing itself is recorded as unavailable kit

NVDA 2026.1.1 was installed and an actual pass was attempted against both the Storybook-rendered screens and the
rendered PDF sample pack, using this session's own screen- and input-automation tooling standing in for a human
tester, before falling back to the tree read and source inspection in section 3. Three independent barriers were
found, each a legitimate platform or safety boundary rather than a configuration mistake, and none was worked
around:

1. **The installed copy runs elevated.** NVDA's own executable manifest requests `uiAccess="true"` (needed, in
   ordinary use, so NVDA can read secure desktops and UAC prompts), and Windows silently raises it to a protected
   integrity level when launched from `Program Files`. Windows' own inter-process input protection (UIPI) then
   refuses input from this session's automation to any of its windows — the same protection that stops any
   unprivileged process from puppeteering a screen reader, working as intended.
2. **A portable copy, which avoids that elevation, is not addressable.** The sandbox's own application allow-list
   grants access by matching a registered, installed application; a portable copy run from a scratch directory has
   no such registration, and its windows render as masked regardless of which executable is actually running.
3. **Real browser windows are automation-input-restricted by policy** in this environment, independent of NVDA,
   which forecloses driving a real Chrome or Edge window's keyboard input directly even where elevation is not the
   issue.

Per checklist section 2.2, this is recorded as missing kit, not absorbed into a Pass: every `SR` row above is
`N/A — kit unavailable: NVDA pairing`, and the run does not count as covering them. No waiver or deferral
instrument is invented for it — the checklist's own convention already says exactly what to do with a run that
cannot reach its pairing, and this record follows it.

### What happens to the gap from here

- These `N/A — kit unavailable` rows carry into the release evidence exactly as any other coverage gap does
  (checklist section 2.5 item 5): gathered into the accessibility report at the release train, where the Owner
  confirms release-evidence item 7 (`release-gates.md`), rather than silently disappearing.
- **#512** and **#326** both already track "the manual NVDA pass has not been run" as their own open item (#326's
  closing comment, 2026-09-14, pulled forward onto this issue); this record is additional evidence for that same
  open item, not a new one.
- The two genuine defects in section 3.5 (#533, #535) are filed and severity-graded independently of the missing
  pairing — they were found by reading source, not by hearing NVDA, and do not need it to be true.

---

## 5. Severity and defects

Per checklist section 7.1: `A11Y-DP-06` and `A11Y-DP-07` are the only **Fail** verdicts this record raises, both
graded **S3** and filed as [#535](https://github.com/MK-AIFy/HyFib-Tailor360/issues/535) and
[#533](https://github.com/MK-AIFy/HyFib-Tailor360/issues/533). Every `N/A` row names its reason inline per section
2.2, and none is a Fail in disguise — each is a real gap in this run's coverage, distinct from a defect in the
product.

---

## 6. Related documents

- [`../a11y-checklist.md`](../a11y-checklist.md) — sections 2.2, 2.5, 5.5, 5.6 and 7.1, the instructions, the
  kit-unavailable convention and the rows this record answers
- [`../../billing/accountant-document-review.md`](../../billing/accountant-document-review.md) — section 5, the
  rendered-artefact half of the `A11Y-DP` rows
- [`../../process/release-gates.md`](../../process/release-gates.md) — RG-06, its severity mapping and how the
  release evidence gathers a coverage gap
- Issue [#516](https://github.com/MK-AIFy/HyFib-Tailor360/issues/516) — the issue this record closes the
  engineering half of
- Issue [#533](https://github.com/MK-AIFy/HyFib-Tailor360/issues/533) — the `A11Y-DP-07` defect this record raised
- Issue [#535](https://github.com/MK-AIFy/HyFib-Tailor360/issues/535) — the `A11Y-DP-06` defect this record raised
