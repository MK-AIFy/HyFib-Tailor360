# Accountant's review of the rendered billing documents

This is the record of the review at which a qualified accountant looks at the documents this system actually prints
— a tax invoice, a credit note and a debit note — and says whether they are fit to give a customer and to put in
the practice's books. It is the last of the parent issue's acceptance criteria that engineering cannot answer for
itself, and the one the roadmap names against **#41**, **#42** and **#44** as *"GST configuration and sample output
approved by accountant"*.

It is written as a template, in the shape of [`../nfr/reviews/stakeholder-review.md`](../nfr/reviews/stakeholder-review.md):
the agenda, the questions the accountant must answer, and the signature blocks, so that the review is conducted
against it and completed in it.

> **Status: awaiting the review. The review has not been held.** Nothing in this document is a record of anything
> anyone has said. Every field marked _to be completed at the review_ is filled in the pull request that follows
> the meeting, together with the date.

**No statutory requirement is stated in this document.** Every question below is the accountant's to answer. Where
the system already does something — omitting a zero tax component, for instance — the question says what it does
today and asks whether that is right; it does not assert that it is. A number or a rule invented here would be a
change to the product wearing a reviewer's signature.

---

## 1. Review details

| Field | Value |
| --- | --- |
| Purpose | Confirm, change or reject the presentation of the rendered tax invoice, credit note and debit note before the first production posting |
| Status | **Not yet held** |
| Date | _To be completed at the review_ |
| Location | _To be completed at the review_ |
| Chaired by | Business owner, who books the accountant |
| Recorded by | Technical reviewer |
| Document drafted | Issue #326 (E09-F02-10), wave W4 |
| Gate this review unlocks | The go-live gate row in [`go-live-plan.md`](go-live-plan.md) section 2.7, and the release evidence checklist **RG-14** |

---

## 2. What is being reviewed, and where it came from

Three documents, attached to the pull request that added this record and regenerable at any time:

| Document | What it is | How it was produced |
| --- | --- | --- |
| `invoice.pdf` | A posted tax invoice for a two-garment order, supplied intra-state | Priced through `IPricingService`, drafted, posted, then rendered and stored by the worker exactly as a production posting is |
| `credit-note.pdf` | A credit note relieving one line's lining charge | Posted against that invoice through `POST /api/v1/billing/invoices/{id}/credit-notes` |
| `debit-note.pdf` | A debit note adding an agreed extra to the other line | Posted against the same invoice through the sibling route |

They are **not committed**: `artifacts/` is outside version control, and the copies a reviewer reads are the ones
attached to the pull request. Regenerate them with

```bash
dotnet test tests/Tailor360.IntegrationTests/Tailor360.IntegrationTests.csproj -- --filter-method "*WritesTheAccountantsSamplePack*"
```

which writes all three to `artifacts/billing-document-samples/`. The test needs a database, as every test in that
tier does.

**Every figure, name, address and amount in the pack is synthetic.** The customer's name is Tamil on purpose: the
template prints the display name, and a face that cannot draw Tamil fails by printing nothing rather than by
failing to render, so a pack reviewed only in English would hide the first defect a Tamil-speaking customer would
meet. No production data was used, and none may be.

### 2.1 What the figures were priced from

The rates come from a price list and a tax configuration published through the ordinary administration routes, and
the arithmetic is the engine's, pinned against the accountant's cases in
`tests/fixtures/billing/pricing-golden-master.json` (**#147**).

**Two different questions, and this review answers only the second.** Whether the stored arithmetic is *correct* is
the golden master's question and is still open under **OD-05**. Whether the printed page *says what the system
stored* is proved mechanically, by `PdfSnapshotAgreementTests` — every printed total and every line's tax
components are compared with the persisted calculation snapshot to the paisa, in both the intra-state and the
inter-state scheme. What is left for a person is whether a document that faithfully reports a correct calculation
is nevertheless **presented** in a way that satisfies the law and the practice. That is what section 3 asks.

---

## 3. The questions the accountant must answer

### 3.1 Statutory fields on a tax invoice

| # | Question | Answer | Effect |
| --- | --- | --- | --- |
| 3.1.1 | Is every field a tax invoice must carry present on the page? Name any that is missing | _To be completed_ | New fields are a change to the template and to `DocumentModels`, and possibly to what Billing stores |
| 3.1.2 | Is the supplier block — trade name, legal name, GSTIN, state code, branch — sufficient as printed, or must more appear (full registered address, PAN, a declaration)? | _To be completed_ | `BillingDocumentTemplate` header; the GST registration record |
| 3.1.3 | Is the customer block sufficient for a business-to-consumer supply as printed? What changes for a business-to-business supply where the customer is registered? | _To be completed_ | Whether a customer GSTIN field is needed at all, which is new scope |
| 3.1.4 | Must the document carry a signature, a digital signature, or a declaration in place of one? | _To be completed_ | New scope if so; it is neither built nor designed |
| 3.1.5 | Is the title of each document — TAX INVOICE, CREDIT NOTE, DEBIT NOTE — the wording required? | _To be completed_ | `TitleOf(templateKey)` in the template |

### 3.2 Tax presentation

| # | Question | Answer | Effect |
| --- | --- | --- | --- |
| 3.2.1 | **A zero tax component is omitted from the page today, not printed as zero.** An intra-state invoice therefore shows CGST and SGST and no IGST line at all. Is omission right, or must every component be printed with a zero? | _To be completed_ | The suppression rule in `BillingDocumentTemplate`, and the corresponding assertions in `PdfSnapshotAgreementTests`. **This is a presentation question only the accountant can settle** |
| 3.2.2 | Is showing each component per line *and* as a document total the right presentation, or is one of the two redundant or forbidden? | _To be completed_ | The line table's per-line breakdown, and the totals block |
| 3.2.3 | Is a rate-wise summary table required in addition to the per-line breakdown? | _To be completed_ | New scope: no such table is built |
| 3.2.4 | Is cess presented correctly where it applies? No sample carries cess — is a sample with cess needed before sign-off? | _To be completed_ | Whether a further scene is needed in the test and in the pack |
| 3.2.5 | Is the place-of-supply statement — `Place of supply <code> · <scheme>` — sufficient, and must the state be named in words rather than by code? | _To be completed_ | The template's document block |

### 3.3 Classification, rounding and the number series

| # | Question | Answer | Effect |
| --- | --- | --- | --- |
| 3.3.1 | Is the HSN/SAC shown per line in the right place and at the right level of detail? | _To be completed_ | The line description cell |
| 3.3.2 | Is the round-off line presented correctly, and is rounding to the nearest rupee at the document level the right rule? | _To be completed_ | The price-list version's round-off rule; **OD-05** for the rounding half |
| 3.3.3 | Is the human-readable number series `INV-<branch>-<FY>-000001` acceptable, and is `2627` the right financial-year token for 2026-27? | _To be completed_ | **COD-03**, [`../architecture/conventions.md`](../architecture/conventions.md). The go-live plan names this as needing confirmation **before the first production posting** |
| 3.3.4 | Must the series be strictly gapless for GST purposes, and if so, what must happen to a number drawn for a posting that then fails? | _To be completed_ | **COD-03**. The current design draws the number inside the posting transaction |
| 3.3.5 | Is a separate series required for credit and debit notes, or is the shared series acceptable? | _To be completed_ | The numbering service and its migrations |

### 3.4 Credit and debit notes

| # | Question | Answer | Effect |
| --- | --- | --- | --- |
| 3.4.1 | Does each note state its link to the invoice it corrects clearly enough? It prints `Against invoice <number>` where an invoice prints its order number | _To be completed_ | The template's document block |
| 3.4.2 | Must a note carry the original invoice's **date** as well as its number? | _To be completed_ | `DocumentModels`; the note payload |
| 3.4.3 | Must the reason for a note appear on the document? It does today, where one was given | _To be completed_ | Whether the reason is a statutory field or an internal one — it is **Personal** data under [`../nfr/data-classification.md`](../nfr/data-classification.md) section 5.10 |
| 3.4.4 | **A note prints no balance line today** — it states what it moves, never what remains owed. Is that right? | _To be completed_ | The `isNote` branch of the totals block |
| 3.4.5 | A cancelled invoice keeps its number, its lines and its totals, is stamped CANCELLED, and is relieved by a credit note for the whole amount. Is that the right treatment? | _To be completed_ | The cancellation design of **#154**; the `cancelled` flag in the template header |

### 3.5 The books

| # | Question | Answer | Effect |
| --- | --- | --- | --- |
| 3.5.1 | Can these three documents be entered into the practice's books as they stand, without asking the shop for anything further? | _To be completed_ | If not, what is missing is the answer that matters most in this review |
| 3.5.2 | Is anything on the page that should **not** be there — an internal code, a barcode payload, a reference a customer should not see? | _To be completed_ | The template; the barcode payload is printed in small type under the number |

**Accountant sign-off statement.** _I have examined the rendered tax invoice, credit note and debit note listed in
section 2. I confirm that their presentation is fit for issue to a customer and for entry in the practice's books,
subject to the changes recorded above, and I confirm the number series and financial-year token recorded in 3.3._

| Name | Practice | Membership number | Signature | Date |
| --- | --- | --- | --- | --- |
| _To be completed_ | _To be completed_ | _To be completed_ | _To be completed_ | _To be completed_ |

---

## 4. Why there is no waiver row

**RG-14, the release evidence checklist, admits no waiver.** It is marked "No waiver" in
[`../process/release-gates.md`](../process/release-gates.md) section 4 and is listed in
[`../process/waivers.md`](../process/waivers.md) section 3 among the gates that may never be waived, where the
accountant is named as a required approver for its financial evidence. A waiver row for this review would be
invalid on its face, so none is written and none should be added.

What is recorded instead is a **deferral**, on issue #326, with all six fields of
[`../process/definition-of-ready.md`](../process/definition-of-ready.md) section 5. A deferral says the criterion
still applies and names who will close it and when; a waiver would say it no longer applies. **#42 stays open until
the accountant signs.**

---

## 5. Accessibility of the rendered documents — `A11Y-DP-01` to `A11Y-DP-09`

The records of [`../nfr/a11y-checklist.md`](../nfr/a11y-checklist.md) section 5.6, answered here for the **rendered
artefact**. The on-screen preview's own records belong to E09-F02-5 and to the checklist's on-screen scope.

A record the renderer cannot satisfy is marked **Fail with the gap named and an issue to close it**, never Pass and
never Not applicable. **A gap recorded is not a gap waived.** Where a record is genuinely not about a rendered
document, it is marked Not applicable **with the reason**, as the checklist requires.

| Record | Verdict | Basis, gap and owner |
| --- | --- | --- |
| **A11Y-DP-01** — content readable as text rather than only inside an embedded rendering | **Pass** | The artefact's content is real text: `PdfSnapshotAgreementTests` reads every figure out of the PDF's own text layer, and `DocumentAdapterTests` does the same against the golden master. The in-app preview is HTML, not an embedded viewer (E09-F02-5) |
| **A11Y-DP-02** — read with the right phonetics; a Tamil name in Tamil | **Pass** | `BillingDocumentTemplate` now wraps the customer's display name and address lines in a `SemanticLanguage("ta-IN")` span whenever — and only whenever — they carry Tamil script, over QuestPDF's PDF/UA-1 structure tree (ADR-0014 section 6). Proved by `RenderedInvoiceCarriesATaggedPdfStructureTreeWithHeaderRolesAndATamilLanguageSpan`, and its negative case `ADocumentWithNoTamilTextCarriesNoStrayLanguageSpan` — an English-only customer carries no `ta-IN` span at all. The document-wide `/Lang` of `en-IN` still applies to everything else on the page. **Closed by #512** |
| **A11Y-DP-03** — real text, never an image of text | **Pass** | Same basis as A11Y-DP-01, asserted in two tiers. A picture of a page would extract nothing and every figure assertion would fail |
| **A11Y-DP-04** — reading order matches visual order | **Pass** | QuestPDF's PDF/UA-1 conformance gives the document its own structure tree — `Document`, `Header`, `Content` and `Footer` landmarks — so the order a reader announces is the order the document declares, not one inferred from geometry. Proved by the same structure-tree assertion in `DocumentAdapterTests`, alongside the extraction-order assertions already held by `RenderedDocumentIsAccessibleAsFarAsTheRendererAllows` and `PdfSnapshotAgreementTests`. **Closed by #512** |
| **A11Y-DP-05** — tables carry header cells | **Pass** | The line table is tagged `SemanticTable()` and its column headings `SemanticHorizontalHeader()`, so the structure tree carries a genuine table-header role (`/S /TH`) a reader can announce, alongside the printed-once-above-their-rows layout `DocumentAdapterTests` already pinned. Proved by `RenderedInvoiceCarriesATaggedPdfStructureTreeWithHeaderRolesAndATamilLanguageSpan`. **Closed by #512** |
| **A11Y-DP-06** — Print, Send to print station and Download PDF distinct and named | **Not applicable to the artefact** — reason: these are **screen controls**, not properties of a rendered document. Answered on the invoice detail screen, delivered by **E09-F02-5**, and recorded against that screen |
| **A11Y-DP-07** — "queued to the print station" announced with branch and job | **Not applicable to the artefact** — reason: as A11Y-DP-06, a screen announcement delivered by **E09-F02-5** |
| **A11Y-DP-08** — an amount in words is read as words | **Not applicable** — reason: **the billing templates print no amount in words.** Should the accountant require one (question 3.1.1), this record becomes live and must be answered before that change ships |
| **A11Y-DP-09** — the measurement sheet renders in the reader's display unit with the unit announced | **Not applicable to the billing documents** — reason: there is **no measurement-sheet template**; `Documents/` holds the billing document and the receipt only. The record belongs to whichever issue renders a measurement sheet |

The manual screen-reader pass that turns these desk verdicts into observed ones is section 2.3 of the checklist and
needs the NVDA pairing and a real device; it has **not** been run against these artefacts. The verdicts above are
recorded from the renderer's measured behaviour and the assertions named beside them, which is what can honestly be
claimed from a coding session.

---

## 6. What happens after the review

1. The answers in section 3 are filled in, the signature block completed and dated, and the banner at the top of
   this document replaced with the date the review was held.
2. Every change the accountant asks for becomes an issue linked to **#42**, and #42 stays open until they are
   merged and the accountant has seen the re-rendered pack.
3. The deferral record on #326 is closed against its stated condition.
4. The gate row in [`go-live-plan.md`](go-live-plan.md) section 2.7 is marked met, with this document as evidence.
5. **COD-03** is marked decided if 3.3.3 and 3.3.4 settle it, in
   [`../prd/assumptions-and-open-decisions.md`](../prd/assumptions-and-open-decisions.md).

---

## 7. Related documents

- [`go-live-plan.md`](go-live-plan.md) — the gate this review feeds, section 2.7
- [`../nfr/reviews/stakeholder-review.md`](../nfr/reviews/stakeholder-review.md) — the earlier review, whose
  section 9 obtained the accountant's undertaking to review the GST output of a release candidate before go-live
- [`../nfr/a11y-checklist.md`](../nfr/a11y-checklist.md) — section 5.6, the records answered in section 5 above
- [`../process/release-gates.md`](../process/release-gates.md) — RG-14, which admits no waiver
- [`../process/waivers.md`](../process/waivers.md) — section 3, what may never be waived
- [`../process/definition-of-ready.md`](../process/definition-of-ready.md) — section 5, the six fields of a deferral
- [`../architecture/conventions.md`](../architecture/conventions.md) — COD-03, the number series and the
  financial-year token
- [`../nfr/data-classification.md`](../nfr/data-classification.md) — section 5.10, how these documents are classed
- [`../prd/assumptions-and-open-decisions.md`](../prd/assumptions-and-open-decisions.md) — OD-05 and COD-03
