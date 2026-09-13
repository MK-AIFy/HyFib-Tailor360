# ADR-0014 — Document rendering and object storage: QuestPDF, ZXing.Net, MinIO's client, and an interim print queue

| Field | Value |
| --- | --- |
| Status | **Proposed** — 2026-09-12 |
| Decision-makers | Technical reviewer; the business owner for the licence and the print bridge |
| Plan decision | D15 (barcode and PDF rendering), D4 (object-storage keys) |
| Issues affected | #155 (this record), #35 (label printing), #43 (receipts), #31 (media pipeline), #55 (print bridge) |
| Depends on open decision | OD-03 (providers) for the print bridge; none for rendering or storage |
| Supersedes | — |

---

## 1. Context

Issue #42 requires every posted invoice, credit note and debit note to exist as a rendered PDF with a checksum, stored
under an opaque key and streamed only through an endpoint that re-authorises the caller. Until #155 nothing in the
repository rendered a document or stored an object: `IPdfRenderer`, `IBarcodeRenderer` and `IPrintQueue` were ports
without adapters, and there was no object-storage port. ADR-0012 fixes where an adapter may live —
`Integration.Infrastructure`, and nowhere else (ARCH-009) — and that the module behind the port never sees the
library. This record chooses the libraries and the shape of the storage port.

Constraints that shaped the choice:

- The documents are statutory. A rendering must be **deterministic for its model**, so that a stored checksum means
  something and a re-rendering can be compared with it (`INV-INV-06`).
- The customer's name may be in Tamil script; the document must embed a font that has the glyphs
  (`docs/IMPLEMENTATION_PLAN.md`, the localisation rules).
- The barcode on an invoice and the one on a garment-job label must be produced by the same code, so the scanner's
  fallbacks behave the same (`docs/architecture/conventions.md` section 3.3).
- No native dependency the container image cannot carry; no library whose licence the business cannot meet.

## 2. Decision

| Port | Adapter | Library | Licence |
| --- | --- | --- | --- |
| `IPdfRenderer` | `QuestPdfRenderer` | QuestPDF 2026.8 | QuestPDF Community, which the licence grants to organisations under its annual revenue threshold (USD 1M at the time of writing). HyFib Tailor 360's operator is a tailoring business well under it; the threshold is re-checked at each licence change, and the fallback is the Professional licence, a purchase rather than a rewrite |
| `IBarcodeRenderer` | `Code128BarcodeRenderer` | ZXing.Net 0.16 (core package only) | Apache 2.0 |
| `IObjectStorage` (new, `Platform.Abstractions`) | `MinioObjectStorage`; `InMemoryObjectStorage` for tests and a developer without the compose stack | Minio 6.0 | Apache 2.0 |
| `IPrintQueue` | `LoggingPrintQueue` — **interim** | — | — |
| Fonts | Noto Sans and Noto Sans Tamil, embedded resources | — | SIL Open Font License 1.1, beside the files |

Specifics the code relies on:

1. **Determinism.** The PDF's creation and modification dates are taken from the model (`renderedAt`, the posting
   time), never from the clock, so two renderings of one posted document are byte-for-byte identical. The contract
   tier asserts it.
2. **The barcode is drawn, not rasterised into the PDF.** ZXing.Net computes the Code 128 bar pattern; the bars are
   written as an SVG that QuestPDF places on the page, and rasterised by QuestPDF when a PNG is asked for. No image
   library is therefore referenced, which is why the core ZXing.Net package is enough.
3. **One bucket per kind of object, three in all.** The compose stack creates `tailor360-documents`,
   `tailor360-media` and `tailor360-exports`; `ObjectStorageOptions.BucketFor(key)` routes a key by its first path
   segment (`documents/`, `exports/`, anything else to media). That is a blast-radius split — a retention or
   replication rule per bucket — not a module boundary: Billing's documents and Reporting's exports share nothing,
   but two modules writing `documents/…` share a bucket, and MO-4 (a module never reaches another's objects) is
   held by the opaque keys each module owns and by the endpoint that re-authorises every read, not by the store.
4. **Keys are opaque**: `documents/<uuidv7>` with no number and no name (conventions section 3.5). The port has
   **no delete**: a statutory document is never removed by the application.
5. **The real adapter needs an endpoint and two mounted secrets** (`ObjectStorage__AccessKey`,
   `ObjectStorage__SecretKey`, `docs/platform/secrets.md`). `ObjectStorageOptionsValidator` refuses the start of any
   host outside Development that lacks the endpoint or either key, so a deployment cannot silently fall back to a
   store that forgets on restart; in Development the in-memory adapter serves in their absence, with a warning.
6. **The print queue logs and acknowledges.** The routes that print are built, permissioned and audited now against
   the port they keep; the job reaches a printer when #55's print bridge replaces the adapter. Nothing else changes.

## 3. Options considered

| Option | For | Against | Outcome |
| --- | --- | --- | --- |
| **QuestPDF** | Fluent layout in C#, deterministic output, embedded-font support, images and SVG, active maintenance; the plan's default (D15) | Community licence is revenue-gated; no PDF/UA tagging today, so accessibility tagging is a waiver (`docs/process/waivers.md`) | **Chosen** |
| PdfSharp / MigraDoc | MIT, mature | Layout by hand at the drawing level; complex-script shaping for Tamil is weak; no SVG | Rejected: Tamil rendering and the layout cost |
| Headless Chromium (HTML to PDF) | Familiar templating | A browser in the worker image, non-deterministic output, a large attack surface | Rejected: determinism and image size |
| **ZXing.Net core, bars drawn by QuestPDF** | One encoder for every symbology, no image dependency, Apache 2.0 | Only Code 128 is drawn today; QR needs a matrix drawer | **Chosen**; a QR drawer is a small addition when a port asks for one |
| ZXing.Net with SkiaSharp bindings | PNG directly | A second native dependency for what QuestPDF already does | Rejected |
| **Minio client** | Built for the store the stack runs, S3-compatible, path-style by default | An SDK to keep current | **Chosen** |
| AWSSDK.S3 | The canonical S3 client | Heavier, signing quirks against MinIO, no gain for the deployment in hand | Rejected; the port makes a swap a one-class change |
| Print bridge now | Complete | Depends on OD-03 and #55's hardware decisions | Deferred; the logging adapter keeps the routes honest |

## 4. Consequences

- `Platform.Abstractions` gains `IObjectStorage`; Media and Reporting will use it through the same adapter and their
  own prefixes.
- Three packages join `Directory.Packages.props`, referenced by `Integration.Infrastructure` only; the architecture
  tier's ARCH-009 scan holds them there.
- The worker image carries the fonts (about 2.4 MB) as embedded resources.
- **Accessibility tagging of the PDF is not delivered** by this library today; the document carries its title,
  language and subject metadata, and a waiver records the gap and its expiry.
- **The accountant's review of a rendered invoice and credit note** is a release gate this record does not close;
  it is recorded beside the waiver until it is done.

## 5. Compliance

Held by the contract tier (`tests/Tailor360.ContractTests/Adapters`): determinism of the renderer, the Code 128
drawing read back through the library's own reader, the in-memory store's contract, the start-up guard, and the
MinIO adapter's round trip against the compose stack when `TAILOR360_TEST_S3_ENDPOINT` names one — a variable the
continuous-integration workflow deliberately does not set (`.github/workflows/ci.yml`), so that round trip is run
by a developer against `./scripts/dev up` and is not a merge gate; by the architecture tier: ARCH-009 for the
packages; by the integration tier: the worker's pass, a renderer that throws, the bounded retry, the download's
authorisation and audit, the barcode lookup's negative cases, the checksum after a cancellation.
