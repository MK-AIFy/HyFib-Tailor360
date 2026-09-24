# Production readiness audit — 2026-09-24

**Result: not ready for production use.** This is a source and environment audit of the
`40502b8` checkout, not a claim that every runtime journey passed. The API route inventory shows
operations in Identity, Customers and Catalog. The endpoint groups for Media, Orders, Custody,
Inventory, Billing, Reporting, Notifications and Integration map no operations. Those modules
cannot yet support a complete tailoring order through invoice, payment and delivery.

## What exists

| Area | Evidence in this checkout | Assessment |
| --- | --- | --- |
| Platform | Web, worker and CLI hosts; PostgreSQL migrations, outbox, audit, security and health infrastructure; architecture, contract and integration test projects | Foundation implemented; runtime checks still needed |
| Staff and reference data | Identity and administration, Customers and measurements, Catalog and PWA screens have API operations and tests | Functional slices exist; full journey still unverified here |
| Tailoring operations | Orders, Custody, Inventory, Billing, Media, Reporting, Notifications and Integration contain endpoint group stubs with no mapped operations | Product-critical work remains |
| Deployment | Development and staging Compose definitions; web, worker, PWA and CLI Dockerfiles; CI image-build gate | Image signing, publishing, promotion, deployment automation and a running staging environment remain |

The first Billing foundation now includes half-away-from-zero document rounding and a GST line
calculator that derives the tax scheme from supplier and place-of-supply state codes. It totals
already-rounded line components. This is unbuilt source, not an invoice-issuing workflow.

Before a bill can be posted, the Billing module must capture and validate the applicable invoice
particulars in [CBIC's tax-invoice rules](https://cbic-gst.gov.in/gst-invoice-rules.html), including
the supplier GSTIN, recipient details, HSN or SAC, taxable values, tax components, place of supply,
reverse-charge status and a serial number unique for the financial year. It must also determine
whether the business is subject to the [e-invoice mandate](https://einvoice6.gst.gov.in/content/einvoice-mandate/)
before issuing relevant B2B or export invoices. A draft calculation may be previewed, but posting,
printing and delivery must stay gated until those rules and accountant-approved examples are met.

## Completion sequence

1. **Establish a reproducible validation environment.** Install the SDK selected by `global.json`
   (.NET 10), provide PostgreSQL and object storage, then run formatting, .NET unit,
   architecture, contract and integration suites, PWA lint, typecheck, tests and build, and all
   four container builds. Resolve every failure before calling any slice ready.
2. **Complete the customer-to-order path.** Deliver secure media capture, design and estimate
   snapshots, pricing, multi-garment order confirmation, workflow and QC. Each command needs
   server-side permission and branch scope, audit, idempotency where retries occur, and
   concurrency handling. Add positive and negative integration cases and end-to-end PWA journeys.
3. **Complete the stock-to-delivery path.** Deliver inventory ledger and reservations, barcode
   custody, invoices and payments, the dispatch payment gate, delivery, notifications and
   reporting. Use integer money units, immutable financial and stock records, and accountant
   approved GST examples. Test retries, reversals, other-branch denial and reconciliation.
4. **Prove operational readiness.** Build, sign and publish images by digest; provision a gated
   staging environment; add the pull-based deploy and rollback procedure, least-privilege database
   roles, backup and restore drills, monitoring, accessibility and device-matrix evidence, a
   security assessment, and user acceptance testing. Release only after the documented gates in
   `docs/process/release-gates.md` have actual evidence.

Owner decisions in `docs/prd/assumptions-and-open-decisions.md` still govern hosting, providers,
dispatch payment policy, tax rounding, device and printer support, retention and operations.
These cannot be silently fixed by implementation defaults.

## Verification available during this audit

- `./scripts/dev doctor`: .NET 10 SDK unavailable (only 9.0.300 installed); Docker daemon and
  PostgreSQL/object-storage test endpoints unavailable. The script reports the integration tier
  would skip locally.
- `git diff --check` and YAML parsing passed for the edited workflow and staging Compose file.
- No build, automated suite, image build, integration test, deployed smoke check or kluster review
  result was obtained during the initial audit. Passing CI for these changes remains unverified.

## Subsequent local verification

The development Compose stack was started, its 22 migrations applied and reference data seeded.
The web, worker, Caddy, PostgreSQL, MinIO and Mailpit containers reported healthy. A later local
check returned HTTP 200 for `/sign-in` and the `/customers` client route, and the served index
referenced the newly built client bundle. TypeScript completed without errors and Vite emitted the
client assets. The Vite process did not exit after asset generation; Vitest workers timed out before
running tests, and the local .NET command could not start because SDK 10.0.100 is not installed.
The local UI was refreshed by layering the generated assets on the existing web image. This is a
development run, not a production image build or release gate. The requested kluster review tool was
not available in this task. Full authenticated journeys, staging checks and CI remain unverified.
