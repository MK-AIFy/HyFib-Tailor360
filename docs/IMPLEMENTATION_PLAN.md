# HyFib Tailor 360 — Implementation Plan

Status: **Proposed** (for review by the product owner and technical reviewer)
Source of truth for scope: GitHub issues [#1](https://github.com/MK-AIFy/HyFib-Tailor360/issues/1) (roadmap), #2–#16 (epics) and #17–#61 (feature issues).
Last reviewed against issues: 2026-09-03 (all 61 open issues read in full; every acceptance criterion and implementation step audited against this plan).

> This plan turns the roadmap into an executable, dependency-ordered sequence of pull requests. It records the
> architecture, conventions and quality gates every implementation issue must follow, and gives each feature
> issue a concrete blueprint (modules, data, contracts, endpoints, screens, workers, tests, evidence). Items that
> need a human decision are collected in [Section 11](#11-decisions-required-from-the-business-owner).

---

## Table of contents

1. [Executive summary](#1-executive-summary)
2. [What the issues require (requirements digest)](#2-what-the-issues-require-requirements-digest)
3. [Key decisions and assumptions](#3-key-decisions-and-assumptions)
4. [Target architecture](#4-target-architecture)
5. [Engineering standards and Definition of Done](#5-engineering-standards-and-definition-of-done)
6. [Delivery plan: milestones, waves, epic closure](#6-delivery-plan-milestones-waves-epic-closure)
7. [Traceability matrix (issue → wave → branch → evidence)](#7-traceability-matrix)
8. [Issue blueprints: E01–E07 (#17–#37)](#8-issue-blueprints-e01e07)
9. [Issue blueprints: E08–E15 (#38–#61)](#9-issue-blueprints-e08e15)
10. [Risks and mitigations](#10-risks-and-mitigations)
11. [Decisions required from the business owner](#11-decisions-required-from-the-business-owner)
12. [Executing this plan with Claude Code](#12-executing-this-plan-with-claude-code)
13. [Immediate next steps (first ten pull requests)](#13-immediate-next-steps-first-ten-pull-requests)

---

## 1. Executive summary

HyFib Tailor 360 is a tailoring operations platform for Android/iOS phones and tablets and desktop browsers. The
roadmap (#1) fixes the architecture direction: a **React + TypeScript installable PWA**, an **ASP.NET Core LTS
modular monolith**, **PostgreSQL**, **private object storage**, an **API-first design with a transactional outbox
and background workers**, **secure server-side sessions behind a BFF**, and **containerised, portable
deployment**. Microservices are explicitly out of scope without an approved ADR.

The 45 feature issues decompose into 15 epics. The dependency graph declared in the issues ("Depends on")
produces six milestones that match the roadmap's target milestones:

| Milestone | Theme | Issues | Outcome |
| --- | --- | --- | --- |
| M1 | Product and architecture baseline | #17, #18, #19 | Approved glossary, workflow maps, ADRs, NFRs, Definition of Done |
| M2 | Platform foundation and access control | #20, #21, #22, #53, #23, #24, #25, #50 | Buildable repo, CI gates, outbox, auth/BFF, RBAC, admin, design system |
| M3 | Customer, measurements, catalog, design, media | #26, #29, #56a, #27, #28, #30, #31 | Customers, versioned templates, measurement capture, design snapshots, secure images, threat models before the flows they cover |
| M4 | Orders, workflow, barcode custody, QC | #41, #32, #33, #34, #35, #36, #37 | Multi-garment orders, job cards, workflow engine, labels, scanning, custody chain |
| M5 | Inventory, billing, payments, reporting, delivery, feedback | #38, #42, #54, #39, #43, #47, #44, #40, #48, #45, #46, #49, #55 | Stock ledger, GST invoices, payments and dispatch gate, notifications, delivery, feedback, reports, adapters |
| M6 | Hardening, integrations, security, operations, launch | #51, #56b, #57, #52, #58, #59, #60, #61 | PWA resilience, WCAG/cross-browser, ASVS baseline and pen test, privacy/audit, observability, CI/CD, backups/DR, UAT and go-live |

Delivery is one focused branch and pull request per implementation issue (a roadmap delivery principle); three
extra-large issues (#32, #56, #61) are split into GitHub sub-issues with their own branches. The plan identifies three parallel
lanes (backend platform, frontend/PWA, governance/security/operations) so that two or three sessions can run
concurrently without touching the same module.

**Two things need attention before implementation starts** (details in Section 3 and Section 11):

1. The roadmap's backend is ASP.NET Core. The Claude Code cloud environment used for this session has **no .NET
   SDK** and its egress policy blocks the Microsoft download hosts, so backend issues need an environment with the
   .NET 10 SDK pre-installed (or an allowed egress rule). A Node.js/TypeScript backend would work in the current
   environment but contradicts the roadmap; that choice is the owner's, not this plan's.
2. Several M1 items are business approvals (workshops, accountant sign-off, device matrix, hosting model and
   budget). Claude can draft every artefact, but approval gates are human. The plan schedules drafting first so
   approvals are never on the critical path for longer than one review cycle.

---

## 2. What the issues require (requirements digest)

### 2.1 Product outcomes (#1)

- Customer identity, contact details, consent, measurements, material images and reference images.
- Configurable stitching categories: Blouse (Pattern, Aari work), Salwar, Lehenga, Gown, Kids, plus future
  administrator-defined categories **without code changes**.
- Shape/design selections, multi-garment orders, job cards, assignment, QC, alterations, delivery.
- Barcode tracking of every garment from intake through Tailor Master, production, QC, delivery team, payment
  clearance, dispatch and feedback. **No PII in barcodes.**
- Inventory with an immutable stock ledger, low-stock alerts, purchasing, consumption, wastage, reports.
- Configurable GST billing, payments, receipts, sales reports and a payment-dependent dispatch gate.
- Complete auditability, security, observability, backups, disaster recovery, automated testing, controlled
  releases.

### 2.2 Non-negotiable delivery principles (#1, repeated in every issue)

- No direct commits to `main`; every change arrives through a reviewed pull request linked to one issue.
- Every state-changing endpoint enforces authentication, authorisation, validation, idempotency where required
  and audit logging.
- Categories, measurements, workflow phases, QC checklists, taxes, prices, alerts, retention and feature
  availability are **configuration, not code**.
- Posted invoices, payments and stock-ledger entries are immutable; corrections are compensating transactions.
- Reporting projections are never the authoritative source of financial, stock, workflow or custody state.
- Module ownership is preserved: no cross-module table access unless an ADR permits it; modules talk through
  `Contracts` projects and events.
- Synthetic data only in tests and local development (production refuses synthetic seeding unconditionally);
  no production secrets in the repository.
- Release gates: security, accessibility (WCAG 2.2 AA), cross-browser, performance, backup/restore and DR.

### 2.3 Release-level acceptance criteria (#1)

End-to-end lifecycle on real labels and devices; dispatch blocked until QC and payment rules pass; role and branch
isolation; consent/retention/access controls on photos and measurements; browser matrix (latest Chrome, Edge,
Firefox, Safari, iOS/iPadOS Safari, Android Chrome); WCAG 2.2 AA; no unresolved critical/high security findings;
load/SLO targets; backup restore, PITR, rollback and DR exercises; accountant approval of GST output; UAT sign-off,
training, runbooks, monitoring and hypercare.

### 2.4 Roles named across the issues

Owner, Admin (and a "HyFib super-user" for feature flags), Reception, Measurement Staff, Tailor Master, Tailor,
Inventory, Cashier, Delivery, Auditor. Roles are default permission bundles; authorisation is permission- and
branch-based (#24).

---

## 3. Key decisions and assumptions

Decisions marked **(ADR)** are formalised by #18. Decisions marked **(owner)** need explicit owner confirmation
(Section 11). Everything else is an engineering default that a reviewer can override in the first PR that
touches it.

| # | Decision | Rationale |
| --- | --- | --- |
| D1 **(ADR, owner)** | Backend: **.NET 10 LTS**, ASP.NET Core Minimal APIs in a **modular monolith** (one deployable web host + one worker host sharing module assemblies). | Mandated by #1/#18. .NET 10 is the current LTS (support to Nov 2028). Minimal APIs keep endpoint definitions close to each module's slice. |
| D2 **(ADR)** | Frontend: **React 19 + TypeScript + Vite** PWA with TanStack Query, React Router, Tailwind CSS 4 plus headless accessible primitives, react-hook-form + zod, `@zxing/browser` for camera decoding with the native `BarcodeDetector` API when present, Workbox via `vite-plugin-pwa`. | Mandated PWA (#1, #12). Libraries chosen for accessibility, small bundles and cross-browser decoding (#36 forbids depending solely on `BarcodeDetector`). |
| D3 **(ADR)** | **PostgreSQL 16+**, one database, **one schema per module**, EF Core 10 with one `DbContext` per module, migrations per module, `xmin` concurrency tokens. | Module ownership at the schema level makes forbidden cross-module access testable (#18, #20, #21). |
| D4 **(ADR)** | **Private S3-compatible object storage** (MinIO locally; S3, R2, or Azure Blob via S3 API in production — #20 verifies the current MinIO community distribution and evaluates Garage/SeaweedFS or a managed bucket before pinning), server-side encryption on every bucket, random object keys, per-module bucket prefixes, versioning with non-current-version retention ≥ the database backup retention. The PWA never receives a storage URL: media is served only by an API endpoint that re-authorises every request and **streams** the object; the storage endpoint is not internet-reachable. A redirect-to-presigned-URL mode is out of scope until an ADR accepts exposing the storage endpoint (presigned URLs are time-bound, never single-use). | #31 requires private storage and no stable URLs; #24 requires re-authorisation of every media request. |
| D5 **(ADR)** | **BFF pattern**: the ASP.NET Core host serves the PWA and the `/api/v1` surface on the same origin; authentication is an `HttpOnly; Secure; SameSite=Lax` session cookie backed by server-side session storage; anti-forgery via header token; no bearer tokens in browser storage. Third-party/trusted clients authenticate separately (API keys or OAuth client credentials) and never share the cookie scheme. | #1, #23, #53. |
| D6 **(ADR)** | **Transactional outbox** table per module schema written in the same transaction as the aggregate; a worker dispatches to in-process handlers, notification channels and webhooks with at-least-once delivery, inbox/idempotency records and dead-letter queue. Each module maps its own domain events to versioned integration events; the Integration module never reads another module's tables. | #21, #47, #54. |
| D7 **(ADR)** | **Single organisation, branch-aware from day one**: `organisation_id` (fixed) and `branch_id` on all operational aggregates; policy-based authorisation evaluates branch scope; tenancy can be added later without schema rewrites. | #18 acceptance criteria. |
| D8 **(ADR)** | **Configurable taxonomy stored as versioned data**: categories, service types, measurement templates, design option groups, QC checklists, workflow definitions, price lists, tax configuration, payment modes, alert policies, retention policies and costing assumptions are draft → published (immutable) → retired records with seed data for the initial scope. | #1, #27, #29, #30, #33, #34, #40, #41, #43, #57. |
| D9 | **Identifiers**: UUIDv7 primary keys (`Guid.CreateVersion7()`) are the only identifiers used in API paths, deep links and customer links; human-readable display numbers (`O-<branch>-<FY>-000001`, `J-…-01`, `E-…`, invoice numbers) are allocated from per-branch/financial-year sequences and are never lookup keys on unauthenticated surfaces. Barcode payloads = namespace letter + a 12-character body from the Crockford base32 alphabet: 11 random characters (55 bits of entropy) followed by 1 check character computed with a Damm-style checksum over the 32-symbol alphabet (so the check character is itself one of the 32 alphanumeric symbols and keyboard-wedge safe), e.g. `G-7K3M9QW2XZ4B` (garment job; `B` is the check character), `S-…` (stock), `I-…` (invoice), `R-…` (receipt). Decoding accepts lowercase and the I/L→1, O→0 confusables. | #35 (opaque, no PII, separate namespaces, checksum), #42 (atomic numbering), #32 (non-guessable identifiers). |
| D10 | **Money and tax**: `decimal(18,2)` amounts, `decimal(18,4)` unit rates, `decimal(6,3)` tax rates; line-level half-up rounding to paise, document round-off to the nearest rupee (configurable), CGST/SGST vs IGST decided by place of supply; financial year April–March; every calculation stores the pricing/tax configuration version used. | #41, #42. |
| D11 | **Time**: `timestamptz` in UTC; branch IANA timezone (default `Asia/Kolkata`) for display, due dates and report cut-offs; optional branch working calendar (holidays) for due/SLA clocks; server timestamps are authoritative for scans and transitions. | #33, #37, #44. |
| D12 | **Background processing**: a .NET Worker Service container running outbox dispatch, notification delivery, webhook delivery, due-date/SLA evaluation, low-stock evaluation, retention/cleanup, export generation, report projections and reconciliation, scheduled reports and backup-freshness checks (read from `pg_stat_archiver` and backup-scheduler metrics — the worker holds no backup credentials). Outbox claims and scheduled jobs use database leases so a second worker replica (`docker compose up --scale worker=2`) needs no code change; the worker exposes its own health endpoints and per-instance heartbeat. | #21, #33, #40, #44, #46, #47, #57. |
| D13 **(owner)** | **Observability**: OpenTelemetry traces/metrics/logs; Serilog structured logs with a redaction policy; `AspNetCore.HealthChecks` with the probe semantics in Section 4.4 (readiness never depends on non-essential dependencies). Production default is **collector-only on the application VM** (OpenTelemetry Collector or Grafana Alloy) shipping to a hosted backend (Grafana Cloud free tier or the Section 11 item 14 choice); the self-hosted Prometheus/Loki/Tempo/Grafana/Alertmanager stack stays in `infra/observability/` as a compose profile for development, air-gapped on-prem installs and game days, and runs on a separate VM if chosen for production. An external dead-man's switch and uptime check (healthchecks.io/Better Stack class) are mandatory so "VM down" and "backup job dead" are never silent. Client telemetry (web vitals, errors, scanner metrics) is posted to a same-origin endpoint and exported through the same collector. | #20, #36, #52, #58; small-business operability. |
| D14 | **Testing**: xUnit + FluentAssertions, Testcontainers (PostgreSQL, MinIO, ClamAV) for integration, NetArchTest/ArchUnitNET for module boundaries, FsCheck for property tests, Verify for snapshots (PDF/JSON), Playwright (Chromium, Firefox, WebKit) + axe-core for E2E/accessibility, k6 for load, Lighthouse CI for performance budgets. | #20, #22, #52, #58, #61. |
| D15 | **Barcode/PDF rendering and printing**: ZXing.Net for Code 128/QR bitmaps behind `IBarcodeRenderer`; PDF via `IPdfRenderer` with QuestPDF as default adapter (verify the Community licence fits HyFib's revenue) and PDFsharp as the MIT alternative, embedding a Tamil-capable font; `IPrintQueue` (`platform.print_jobs`) plus a print-station screen so phones send labels/receipts to a printer-connected device instead of driving thermal printers through a mobile browser. Image processing via SkiaSharp (default; Magick.NET only with a restrictive `policy.xml`) for decode-validate, EXIF strip, re-encode and thumbnails, always in the worker under a bulkhead. Ports are introduced by the first issue that needs them in `Platform.Abstractions`: `IPdfRenderer` by #32a (estimate PDF), `IBarcodeRenderer` and `IPrintQueue` by #35; later issues extend them by non-breaking change only. | #31, #32, #35, #42, #43, #55. |
| D16 | **Malware scanning**: ClamAV (`clamd`) behind an `IMalwareScanner` port, feature-flagged; uploads are quarantined until the scan passes. | #31, #56. |
| D17 **(owner)** | **Deployment baseline**: Docker Compose (reverse proxy with automatic TLS, web host, worker, PostgreSQL, MinIO, ClamAV, observability stack, backup sidecar) for single-VM/on-prem; the same images run under Kubernetes/Helm later. Terraform (cloud) or Ansible (on-prem) for environment provisioning. An interim single-VM staging environment exists from the end of W1 so real-device UAT has a place to run. | #1 portability, #22, #59. |
| D18 | **Backups** (final design decided with the hosting model, Section 11 item 2; table in #60): self-hosted PostgreSQL runs pgBackRest **inside the PostgreSQL image** (`archive_command` cannot live in a sidecar) with base + continuous WAL to an encrypted, versioned, object-locked bucket whose retention is enforced by bucket lifecycle (never `pgbackrest expire`) and whose archiver identity cannot delete; managed PostgreSQL uses provider PITR plus a nightly logical dump to the same locked bucket. The backup cipher key and the Data Protection key-encryption key are escrowed separately (password manager/KMS), never only on the VM and never in the same backup set as the data. Media buckets keep non-current versions ≥ 35 days and an off-site copy. Weekly automated restore on the staging VM (never production, never GitHub-hosted runners); retention 35 daily + 12 monthly. | #57, #60. |
| D19 | **Repository layout**: single repository (`src/`, `clients/pwa/`, `tests/`, `docs/`, `infra/`, `.github/`) so one PR can carry API, UI, migrations and docs for an issue. | #20, #22. |
| D20 **(owner)** | **Providers**: default adapters are fakes; first real adapters are SMTP email, an Indian SMS provider (e.g. MSG91), WhatsApp via Meta Cloud API or an aggregator, UPI/card via Razorpay or PhonePe, accounting export in Tally XML. Enabled per branch by feature flag only after contract tests pass and a support-ownership document exists. | #47, #55. |
| D21 **(ADR)** | **Caching**: no application cache is ever authoritative. Permitted caches: version-keyed read caches for catalog, price lists, tax configuration and workflow definitions (invalidated by the corresponding `…VersionPublished` event); feature-flag evaluation cache with a documented propagation bound (≤ 30 s via `LISTEN/NOTIFY`); session-revocation cache backed by `identity.sessions`, revalidated per request when more than one web replica runs. A distributed cache (Redis/Valkey) is optional and introduced only with more than one replica. | #18, #21, #23, #29, #59. |

**Assumptions**

- A1. One legal entity (organisation) with one or more branches, all in India; INR only; GST-registered.
- A2. Staff-only application with local accounts (password + TOTP/passkeys); customers interact through expiring
  links (estimate, status, feedback), not accounts. Federation with an external identity provider is an owner
  decision (Section 11).
- A3. Concurrent users ≈ 20–50 per branch, orders ≈ 100–500/month per branch, images ≈ 5 per garment. The NFR
  issue (#19) will replace these with measured targets.
- A4. Hardware: Android phones/tablets, iPhone/iPad, desktop browsers, USB/Bluetooth keyboard-wedge scanners,
  thermal label printers (via a print station or a print bridge) and A4 printers. Counter and workshop devices may
  be shared between staff (Section 11 item 12).
- A5. Baseline sizing (starting point for #19 and Section 11 item 2): 3 branches × 500 orders/month × 2 garments ×
  5 images × ~1.5 MB after re-encode ≈ 22 GB/year of originals plus ~30% derivatives; database growth < 5 GB/year;
  scans < 5,000/day. Resident memory: PostgreSQL 2 GB, web 0.5 GB, worker 0.5 GB, MinIO 0.5–1 GB, ClamAV
  1.5–2 GB, Caddy 0.1 GB, collector 0.3 GB (self-hosted observability adds 3–4 GB). Baseline production VM
  4 vCPU / 16 GB RAM / 200 GB SSD; object storage 100 GB with growth alerts; staging 2 vCPU / 8 GB. On-prem egress
  list: ClamAV signature mirror, backup bucket endpoint, ACME DNS provider, observability backend, container
  registry.

---

## 4. Target architecture

### 4.1 Context

```
Reception / Measurement / Tailor Master / Tailor / Inventory / Cashier / Delivery / Owner / Auditor
        │  (phones, tablets, desktops — installable PWA, same-origin cookie session)
        ▼
┌─────────────────────────────── Web host (ASP.NET Core) ───────────────────────────────┐
│  Static PWA  │  BFF: session cookie, anti-forgery, /api/v1 (Minimal APIs, OpenAPI)   │
│  Modules (in-process): Identity/Admin · Customers/Measurements · Catalog/Design ·     │
│  Media · Orders/Workflow · Custody/Barcode · Inventory · Billing/Payments ·           │
│  Reporting · Notifications/Feedback · Integration · Platform (outbox, audit, flags)   │
└───────────────┬───────────────────────────────┬───────────────────────────────────────┘
                │ EF Core (schema per module)    │ uploads / authorised media streaming
                ▼                               ▼
        PostgreSQL 16 (one DB)          Private object storage (S3 API, SSE)
                ▲
                │ outbox / inbox / leases / heartbeats
┌───────────────┴───────────────── Worker host (.NET Worker Service) ───────────────────┐
│  Outbox dispatcher · Notification sender · Webhook sender · Due-date/SLA evaluator ·   │
│  Low-stock evaluator · Retention/cleanup · Export generator · Projections and         │
│  reconciliation · Scheduled reports · Backup-age monitor                              │
└───────────────┬───────────────────────────────────────────────────────────────────────┘
                ▼ ports/adapters (fakes by default)
   Email · SMS · WhatsApp · Payment gateway · Accounting export · Print bridge · ClamAV
```

### 4.2 Solution layout (D19)

```
HyFib.Tailor360.slnx
├── src/
│   ├── Platform/                      # shared kernel: no business rules
│   │   ├── Tailor360.Platform.Abstractions   # Result, DomainEvent, IClock, IIdGenerator, Money, ports
│   │   │                                     # (IEmailSender, IPdfRenderer, IBarcodeRenderer, IPrintQueue, IOutboundHttp, ITimelineSource, …)
│   │   ├── Tailor360.Platform.Persistence    # EF conventions, outbox/inbox, sequences, idempotency, audit writer
│   │   ├── Tailor360.Platform.Security       # permission catalogue, policies, branch scope, step-up
│   │   └── Tailor360.Platform.Observability  # OTel, Serilog redaction, health checks, correlation
│   ├── Modules/
│   │   ├── Identity/      (Domain, Application, Infrastructure, Api, Contracts)
│   │   ├── Customers/     (customers, consent, preferences, measurement templates, measurement versions/drafts)
│   │   ├── Catalog/       (categories, service types, design option groups/rules, QC checklist templates)
│   │   ├── Media/         (upload pipeline, storage, authorised delivery, retention)
│   │   ├── Orders/        (estimates, orders, garment jobs, snapshots, workflow engine, QC results, alterations, holds)
│   │   ├── Custody/       (barcode identities, labels, scan/custody events, reconciliation, delivery queue)
│   │   ├── Inventory/     (items, units, suppliers, locations, ledger, reservations, stocktake, alerts, valuation)
│   │   ├── Billing/       (pricing/tax engine, price lists, invoices, credit/debit notes, payments, receipts, cashier)
│   │   ├── Reporting/     (read models, projections, reconciliation, scheduled reports, governed exports)
│   │   ├── Notifications/ (templates, intents, deliveries, in-app centre, customer links, feedback, service recovery)
│   │   └── Integration/   (integration event relay, webhooks, provider adapters, accounting export, callbacks)
│   ├── Hosts/
│   │   ├── Tailor360.Web              # BFF + API + static PWA hosting, module registration, timeline composition
│   │   └── Tailor360.Worker           # background processing host with its own health endpoints
│   └── Tools/
│       └── Tailor360.Cli              # migrate, init-reference-data, seed-synthetic (non-production), replay-outbox,
│                                      # flags set, rebuild-projection, create-owner
├── clients/pwa/                       # React + TypeScript PWA (Vite), generated API client, Storybook
├── tests/
│   ├── Tailor360.UnitTests            # per-module domain/application tests, property tests
│   ├── Tailor360.IntegrationTests     # Testcontainers (PostgreSQL, MinIO, ClamAV), API tests, authorisation matrix
│   ├── Tailor360.ArchitectureTests    # module dependency rules (ARCH-001…), vendor SDK isolation, endpoint policy
│   ├── Tailor360.ContractTests        # OpenAPI lint/diff, endpoint inventory, consumer contracts, adapter suites
│   ├── e2e/                           # Playwright (Chromium/Firefox/WebKit; phone/tablet/desktop; portrait/landscape), axe
│   ├── load/                          # k6 scenarios
│   └── fixtures/                      # golden masters (billing, valuation, reporting), db snapshots, synthetic datasets
├── docs/                              # prd, architecture, adr, nfr, process, security, privacy, api, reports, runbooks, uat
├── infra/                             # compose (dev, staging), Dockerfiles, reverse proxy, observability, backup, terraform/ansible
└── .github/                           # workflows, issue/PR templates, CODEOWNERS, dependabot
```

Module internals follow one shape: `Domain` (aggregates, invariants, domain events — no framework references),
`Application` (commands/queries, validators, authorisation requirements, ports, integration-event mappers),
`Infrastructure` (EF `DbContext`, repositories, adapters, outbox handlers), `Api` (Minimal API endpoint groups,
DTOs), `Contracts` (integration events, queries and read contracts other modules may reference). Only `Contracts`
and `Platform.*` may be referenced across modules; enforced by architecture tests with stable identifiers
(`ARCH-001`…) published in `docs/architecture/architecture-rules.md` (#18) and implemented in #20.

### 4.3 Module responsibilities, owned data and published contracts

| Module | Owns (schema / storage prefix) | Publishes (events and read contracts) | Consumes |
| --- | --- | --- | --- |
| Identity/Admin | users, roles, permissions, branch assignments, sessions, MFA/passkeys, recovery, branches (with timezone and working calendar), admin audit views; the feature-flag administration UI/API calls Platform through its contract | `UserDeactivated`, `BranchCreated`, `BranchCalendarChanged`; `IUserDirectory` (names, roles, capabilities) | Platform (flag contract) |
| Customers/Measurements | customers, aliases, consent records, communication preferences, duplicate candidates, merges, measurement templates/versions/fields, measurement drafts and versions | `CustomerCreated/Merged/Corrected/Deactivated`, `ConsentRecorded/Withdrawn`, `PreferencesChanged`, `MeasurementVersionConfirmed`; `IConsentQuery`, `ICommunicationPreferenceQuery`, `ICustomerSnapshotQuery`, timeline source | Identity (branch scope) |
| Catalog/Design | categories, service types, catalog versions, design option groups/options/rules, QC checklist templates/versions | `CatalogVersionPublished`; `ICatalogAvailabilityQuery`, `ICatalogDependencyValidator` registration, `IDesignSelectionValidator`, `GarmentDesignSnapshot` builder | — |
| Media (`media/` prefixes) | media objects, derivatives, quarantine, retention holds, access log; bundled static diagrams/illustrations until upload exists | `MediaReady`, `MediaQuarantined`, `MediaDeleted`; `IMediaReference` | Identity, Customers (consent record id), Orders (job authorisation) |
| Orders/Workflow | estimates (priced draft-order snapshots), orders, garment jobs and job dependencies, snapshots (measurement, design, price), order revisions, workflow definitions/versions, job phases, assignments, assignee capabilities, QC results, rework, alterations, holds, cancellations, ready-state | `OrderConfirmed`, `OrderRevised`, `EstimateIssued`, `GarmentJobCreated`, `JobEnteredProduction`, `JobPhaseChanged`, `JobAssigned/Reassigned/Unassigned`, `JobHeld/Resumed/Rescheduled`, `QcRecorded`, `ReworkOpened/Completed`, `AlterationRequested/Decided/Completed`, `JobReadyForDelivery`, `JobDueSoon/Overdue`, `PhaseSlaBreached`, `JobCancelled`, `OrderCancelled`, `JobClosed`, `DesignRevised`; `IAlterationRequests`, `IOrderSnapshotQuery`, timeline source | Customers, Catalog, Billing (pricing contract), Custody (custody state), Billing (dispatch eligibility) |
| Custody/Barcode | barcode identities, label prints, scan/custody events, custody transfers, reconciliation cases, delivery queue entries and dispatch authorisations | `CustodyTransferRequested/Transferred/Overdue`, `ScanRecorded`, `DispatchRecorded`, `DeliveryConfirmed/Failed/Returned`, `HandoffDisputed`; `ICustodyStateQuery`, `IBarcodeIdentityAllocator`, timeline source | Orders (job state), Billing (dispatch eligibility), Identity |
| Inventory | items, units/conversions, suppliers, locations, reorder rules, alert policies, ledger entries, balances (derived), reservations, purchase orders/receipts, stocktakes, low-stock alerts, valuation runs, customer-material custody records | `StockReserved/Released/Consumed`, `PurchaseReceived`, `LowStockRaised/Cleared`, `StocktakePosted`; `IStockBalanceQuery`, `IValuationQuery` | Orders (job references), Notifications (alert routing) |
| Billing/Payments (`documents/` prefix) | price lists/versions, GST registrations, tax configuration versions, calculation snapshots, invoices, invoice lines/tax components, document sequences, document artefacts, credit/debit notes, invoice cancellations, payment modes, payments, allocations, advances, refunds/reversals, receipts, cashier sessions, reconciliation batches, dispatch exceptions | `InvoicePosted`, `InvoiceCancelled`, `CreditNotePosted`, `DebitNotePosted`, `PaymentRecorded/Allocated/Reversed`, `RefundRecorded`, `AdvanceReceived/Applied`, `InvoicePaidStatusChanged`, `CashierSessionClosed`, `DispatchExceptionApproved/Consumed/Expired`; `IPricingService`, `IDispatchEligibilityQuery`, `IFinancialTotalsQuery`, timeline source | Customers, Orders, Catalog |
| Reporting (`exports/` prefix) | read models/projections, projection checkpoints, metric dictionary, reconciliation runs, report schedules, export jobs, costing assumption versions, GST summary layouts | `ReportReconciliationMismatch`, `ReportFreshnessBreached` | all modules' events and read contracts only |
| Notifications/Feedback | templates/versions, intents, deliveries, in-app notifications, customer links (estimate/status/feedback purposes), feedback tokens/responses, service-recovery cases and policies | `NotificationDelivered/Failed`, `FeedbackReceived`, `ServiceRecoveryOpened/Closed`, timeline source | Customers (consent), Orders, Billing, Custody, Inventory events |
| Integration | integration event relay copy, webhook subscriptions/deliveries, provider configurations, payment intents/callbacks, accounting export batches, print-bridge adapters | `WebhookDelivered/DeadLettered`, `PaymentCallbackReconciled` | outbox events from all modules (relay), ports |
| Platform | outbox/inbox, idempotency keys, sequences, audit events (append-only, trigger-protected), configuration, feature flags (`platform.feature_flags`: store, evaluation, evaluation audit), retention policies, print jobs (`IPrintQueue`), Data Protection key ring, job leases, worker heartbeats, correlation | `FeatureFlagChanged`, `PrintJobQueued`; `IAuditWriter`, `IIdempotencyStore`, `ISequenceAllocator`, `IPrintQueue`, `IOutboundHttp` | — |

Object-storage ownership: Media owns the material/reference/diagram/QC-evidence/delivery-evidence prefixes,
Billing owns `documents/` (invoice, estimate, receipt and credit-note PDFs referenced by `document_artifacts`),
Reporting owns `exports/`; no module writes to another module's prefix (enforced by per-module storage
credentials or bucket policies).

### 4.4 Cross-cutting mechanisms

- **Authentication and session (D5, #23)**: ASP.NET Core Identity with an Argon2id password hasher, TOTP MFA and
  passkeys (WebAuthn, supported natively by Identity in .NET 10), server-side session tickets referenced by an
  opaque 256-bit id in a `__Host-t360.session` cookie (`Secure; HttpOnly; SameSite=Lax; Path=/`, no `Domain`),
  rotated on login, MFA, step-up, password change, MFA reset and role change; sliding inactivity and absolute
  timeouts with a two-minute warning dialog (WCAG 2.2.1) and in-place re-authentication that retries the pending
  request with the same `Idempotency-Key`; device and session inventory, logout-all and immediate revocation
  checked per request via the revocation cache (D21); optional revocable trusted-device cookie for shared counter
  devices (Section 11 item 12). v1 registers exactly two authentication paths: the cookie scheme for `/api/v1/**`
  and justified `[AllowAnonymous("reason")]` endpoints (customer links, payment callbacks, health, telemetry
  ingest); API keys/OAuth clients are a separate later issue on `/api/ext/v1/**` and an architecture test asserts
  no endpoint accepts more than one scheme. Anti-forgery is required on every non-safe request for cookie
  principals **and** on login, MFA challenge, passkey and recovery endpoints (login CSRF); a middleware also
  rejects non-safe requests whose `Sec-Fetch-Site` is `cross-site` or whose `Origin` is not the host origin.
  Security headers and an **enforcing** nonce-based CSP ship with #53 at the end of W1 (new directives enter as
  report-only and are promoted in the PR that needs them); #56b performs the hardening review.
- **Authorisation (#24)**: a permission catalogue (`customers.read`, `customers.read_contact`, `orders.confirm`,
  `custody.scan`, `custody.dispatch`, `billing.post_invoice`, `billing.approve_dispatch_exception`,
  `payments.record`, `inventory.approve_variance`, `reports.export`, `admin.users`, …) mapped to default roles in
  an owner-approved `docs/security/permission-matrix.md`; every permission carries `RequiresMfa` and
  `RequiresStepUp` flags — MFA enrolment is mandatory for any principal whose *effective* permission set (custom
  roles included) contains a `RequiresMfa` permission, and endpoints using a `RequiresStepUp` permission declare
  `.RequireStepUp()` (MFA re-authentication within 5 minutes recorded in `identity.sessions.last_strong_auth_at`),
  which an architecture test enforces and the matrix test exercises with a fresh/stale dimension.
  `IAuthorizationRequirement` handlers evaluate permission + branch scope + resource ownership; a
  `TransferScopeRequirement` grants users of the destination branch exactly the receive/reject/resolve actions on
  jobs in a pending cross-branch transfer (#37); endpoints declare `.RequirePermission("orders.confirm")`;
  deny-by-default is enforced by an architecture test; field-level minimisation policies project DTOs per role;
  A system principal — the delivered type is `WorkerPrincipal`, with no requester — is constructible only through `IWorkerScopeFactory` in the worker/CLI hosts from a
  `[WorkerJob]` attribute that declares the job's permissions and branch scope, jobs acting for a user run under
  an impersonation principal rebuilt from the requester's current permissions, and CLI commands require
  `--operator <user>` and `--reason` outside Development; media and download endpoints re-check authorisation on
  every request. Every denied request to a state-changing or step-up endpoint is audited (`authz.denied`,
  coalesced per actor/endpoint/minute — never sampled); read-only capability probes are not audited. Every new or
  changed endpoint adds its expectations to the authorisation matrix fixtures, and the matrix test fails on any
  endpoint without an entry.
- **Validation and errors (#53)**: FluentValidation in the application layer; RFC 9457 problem details with
  field errors, correlation ID and no stack traces; request size/time limits; a **rate-limit policy catalogue**
  (`auth-anon`, `mfa-challenge`, `link-anon`, `callback`, `telemetry-ingest`, `scan-burst` sized for the #51
  queue replay, `export-heavy`, `default-user`, `default-ip`; numbers confirmed by #19) where every endpoint
  declares one policy (architecture test) and the limiter keys on the client IP resolved through
  `ForwardedHeadersOptions.KnownNetworks` limited to the reverse-proxy network (fail-fast when empty outside
  Development); `X-Client-Version` checked against the minimum supported client (426 when too old).
- **Idempotency (#21, #53)**: `Idempotency-Key` (client-generated UUID) required on confirm/scan/post/pay/callback/
  webhook commands. Record key = `(principal_id, route template, key)`; stored: request SHA-256, status, response
  body, `created_at`, `in_flight_until`. Rules: authentication and authorisation run **before** the lookup (a
  replay by a revoked or unauthorised principal is refused, never served from the store); same key + different
  request hash → `422 idempotency.key-reused`; a duplicate arriving while the first is in flight waits up to 5 s
  then returns `409 idempotency.in-progress`; records are retained 7 days, which must exceed the #51 offline-queue
  maximum age plus worker retry horizons (a configuration test asserts `Idempotency:Retention ≥ 2 ×
  OfflineQueue:MaxAge`); scan events additionally deduplicate on `(actor_id, client_event_uuid)` — a UUID seen
  under a different actor is `409 custody.event-conflict`, never a replay.
- **Concurrency (#21, #53)**: `xmin` (or explicit `version`) as concurrency token; `ETag`/`If-Match` on editable
  aggregates; 409 problem details with current version.
- **Audit (mechanism and tamper evidence in #21, verification and viewer in #57)**: `Platform.Persistence` ships
  `IAuditWriter`, a `SaveChanges` interceptor that appends `platform.audit_events` in the same transaction as the
  mutation, and an `[Audited("module.action")]` endpoint filter recording actor, action, resource, reason and
  correlation for every state-changing endpoint (an architecture test fails when a command endpoint lacks it)
  plus explicit calls for sensitive reads (measurement sheet, media, exports). The table carries `seq`,
  `prev_hash` and `row_hash = SHA-256(seq ‖ prev_hash ‖ canonical row)` computed by a `BEFORE INSERT` trigger
  owned by the migrator role (the application role has INSERT only); it is range-partitioned by month so
  retention is a partition detach under the retention role, never a DELETE. #57 adds the hourly verification job,
  external anchoring of the chain head to the object-locked backup bucket with an alert on disagreement, gap
  detection, the restricted viewer/export and retention independent of logs.
- **Outbox/inbox (D6)**: `outbox_messages` per module schema with `locked_by`, `locked_until`, `attempts`,
  `next_attempt_at`, `processed_at`, `dead_lettered_at`; a claim is one short transaction (`UPDATE … WHERE id IN
  (SELECT … FOR UPDATE SKIP LOCKED) RETURNING *`) that excludes messages whose aggregate has an older unprocessed
  message, so two dispatchers never reorder one aggregate's stream; handlers and provider calls run after the
  claim commits, a heartbeat extends the lease, an expired lease is redelivered (hence every handler is
  inbox-deduplicated); wake-up by `LISTEN outbox_<module>` with a 5 s poll fallback; retry/backoff/jitter, dead
  letter with operator replay (CLI in W1, authenticated endpoint from #25). Scheduled jobs acquire a row lease in
  `platform.job_leases` before running; heartbeats are per instance and the "worker down" alert fires when no
  instance is younger than twice the interval.
- **Feature flags (#21, #25)**: `platform.feature_flags` with organisation/branch scope, mutation restricted to
  `admin.feature_flags` (Owner and the HyFib super-user role), mandatory reason, evaluation audit,
  `Microsoft.FeatureManagement` filters, documented propagation bound (D21), safe defaults (off).
- **Configuration, secrets and keys (#21)**: `IOptions<T>` bound from `appsettings` + environment + secret files
  (`AddKeyPerFile("/run/secrets")` for Docker/Kubernetes secrets; `.env` files carry only non-secret settings and
  compose `environment:` blocks never contain secret values), validated on startup (`ValidateOnStart`), failing
  fast if required values are missing; secrets never appear in logs, problem details, health payloads or
  telemetry (tested with sentinel values). The ASP.NET Core Data Protection key ring is persisted in
  `platform.data_protection_keys`, protected with a certificate/KMS key supplied as a secret, rotated every 90 days,
  loaded as part of `/health/startup`, and startup fails if the ring is on the local file system outside
  Development. **Database roles** (created by `infra/`, verified at startup — the runtime connection must not
  hold DDL): `t360_migrator` (DDL, owns schemas and triggers; used only by `migrate` and `init-reference-data`),
  `t360_app` (DML; INSERT-only on append-only tables; no TRUNCATE), `t360_reporting` (SELECT on `reporting.*` and
  contract views, `default_transaction_read_only`), `t360_retention` (partition detach/drop for audit and expired
  drafts), `t360_backup`. A **connection budget** (web transactional 30, web reporting 10, worker transactional
  20, worker reporting 10, migrator 2, tooling 5, reserve 10 against `max_connections = 100`) is validated at
  startup against `SHOW max_connections` and exported with an 80% alert; a second web replica or a read replica
  requires recomputing it or PgBouncer in transaction mode.
- **Media pipeline (#31)**: the web host accepts uploads only as bytes (size cap, default 15 MB) into the
  quarantine bucket; **all decoding happens in the worker** under a bounded `MediaProcessing` bulkhead
  (concurrency 2, 30 s timeout) — header parsed for dimensions and rejected above 40 megapixels or 12,000 px on a
  side, decoded signature validated, ClamAV scan, metadata stripped and re-encoded, derivatives (thumb/preview)
  produced, object promoted to the ready bucket — then served by the authorised streaming endpoint with access log
  (D4).
- **Barcode/scan abstraction (#33 interface, #36 implementation)**: PWA `ScannerSource` interface with
  `CameraSource` (ZXing / BarcodeDetector), `KeyboardWedgeSource` (buffer + terminator detection, ignored while
  typing in unrelated fields) and `ManualEntrySource` (reason required, audited); all produce
  `{ raw, normalised, namespace, id, checksumValid, source, timestamp }`. The server re-validates namespace,
  check character, identity status and branch on every resolve and command.
- **Customer timeline (#26)**: `Platform.Abstractions.ITimelineSource` implemented by Customers, Orders, Billing,
  Custody and Notifications as those modules land; a BFF composition endpoint in the web host merges entries and
  filters them by the caller's permissions and branch scope. Customers never references other modules.
- **Provider calls (#55)**: never inside a database transaction; record intent → call with the intent id as the
  provider idempotency key → apply verified outcome; timeouts are `unknown`, resolved by status polling, never
  assumed successful; payment callbacks never post financial state themselves.
- **Outbound HTTP policy (`Platform.Abstractions.IOutboundHttp`, #54/#55/#59)**: every outbound call from web or
  worker (webhooks and their verification challenges, payment/messaging providers, print bridge, accounting
  target, SMTP host validation) goes through one client factory that requires `https` (a print bridge may use
  `http` only on an explicitly allowlisted RFC 1918 address per branch), resolves DNS once and rejects loopback,
  RFC 1918, CGNAT, link-local, multicast, IPv6 ULA/link-local/IPv4-mapped and cloud-metadata addresses, connects
  to the validated IP with the original host for TLS/SNI, disables automatic redirects (a 3xx is a delivery
  failure), enforces per-call timeouts and response-size caps and logs destination host and IP; provider
  adapters use fixed base URLs from configuration, never from request data; worker egress goes through a
  dedicated proxy/NAT and network policy (#59).
- **Health probe semantics (#20, #21, #59; Compose and Kubernetes)**: `/health/live` = process responsive and
  dispatcher loop ticking (in-process flags, no network dependency); `/health/startup` = configuration validated,
  no unapplied migration, Data Protection ring loaded (worker: plus one heartbeat write); `/health/ready` =
  database reachable (`SELECT 1`, 2 s timeout) only; `/health/detail` (internal network or `admin.health.read`) =
  every registered check as Healthy/Degraded/Unhealthy. Object storage, ClamAV, providers, outbox/projection lag
  and backup age report **Degraded** on the detail endpoint, drive alerts and feature gates (uploads answer
  `503 media.unavailable` while storage is down) and never remove a host from rotation. Compose does not restart
  `unhealthy` containers, so each host runs an in-process watchdog (three consecutive liveness failures → exit
  code 70 → `restart: unless-stopped`); the reverse proxy exposes only `/health/live` externally.
- **Customer links (#32a skeleton, #47–#49)**: 128 random bits, Base64url; only `SHA-256(token)` is stored with
  purpose, subject ids, expiry, revocation, use count and rate-limit counters; served under `/c/{purpose}/{token}`
  by a minimal server-rendered page outside the PWA shell and service-worker scope with its own strict CSP,
  `Referrer-Policy: no-referrer` and `Cache-Control: no-store`, rendered in the customer's language; the reverse
  proxy, request logging and OpenTelemetry redact `/c/**` paths.
- **Print queue (#35)**: `Platform.Abstractions.IPrintQueue` + `platform.print_jobs` (document type label |
  receipt | invoice | estimate | measurement_sheet, format, artefact key, branch, requested_by, target station,
  status queued → printed | failed, audited); a print-station screen on a printer-connected tablet/desktop drains
  its branch's queue through the browser dialog; every print action on phone layouts offers **Send to print
  station** first and **Download PDF** as fallback; #55 adds the optional network/local bridge adapter.

### 4.5 Data model overview (aggregate roots and invariants)

- **Customer** (branch visibility, normalised name/phone, aliases, consent records, preferences, status).
  Invariant: phone is validated, not unique; duplicates are detected and merged only by authorised decision;
  corrections keep `id` and `customer_number`; no delete endpoint (pseudonymisation via #57 only).
- **MeasurementTemplateVersion** (category link, fields with unit/precision/ranges/conditions, diagram
  reference, lifecycle draft → in_review → published → retired). Invariant: published versions immutable
  (database trigger).
- **MeasurementDraft / MeasurementVersion** (customer, template version, canonical millimetre values with display
  unit, reason, taken-by, reused-from). Invariant: versions are never edited; a draft is consumed exactly once.
- **CatalogVersion** (categories, service types with references to template, workflow, option groups, price item
  and QC checklist; design option groups/options/rules; QC checklist versions). Invariant: one coherent published
  version per order; dependency validators registered by later modules.
- **Estimate / Order → GarmentJob[]** (category/service version, measurement snapshot copy with a provenance
  reference to the measurement version, design snapshot, media
  links, price snapshot, due date, priority, job dependencies, workflow definition reference at confirmation,
  pinned workflow version at start-production, phases, assignments, QC results, rework, alteration links, holds,
  ready-state, custody state). Invariant: job snapshots immutable after confirmation; revision only before
  production; ready-for-delivery computed by the gate alone.
- **BarcodeIdentity** (namespace, opaque payload, entity ref, status active/superseded/invalidated, label prints).
  Invariant: exactly one active identity per garment job (partial unique index) and payload unique across all
  statuses; allocated inside the confirmation transaction through the confirmation-participant hook.
- **ScanEvent / CustodyTransfer / ReconciliationCase** (immutable, append-only; idempotency key and client event
  UUID; from/to custodian and location; source camera/wedge/manual; corrections are new events linked to the
  corrected one). Invariant: server validates expected custodian and prerequisites; history is never edited.
- **StockItem**, **LedgerEntry** (immutable, signed quantity in base unit, type, references, `corrects_entry_id`),
  **Balance** (derived, updated in the same transaction, rebuilt and reconciled by a job), **Reservation**,
  **PurchaseReceipt**, **Stocktake**, **ValuationRun**. Invariant: balances rebuildable from ledger; no
  oversubscription (row lock on the balance).
- **PriceListVersion**, **TaxConfigurationVersion**, **PaymentMode** (effective-dated, immutable when published).
- **Invoice** (draft → posted; posted rows never updated; cancellation is an appended record; lines and tax
  components snapshot; sequence per branch/FY), **CreditNote/DebitNote**, **Payment** (append-only, allocations,
  advances, reversal/refund as new rows), **Receipt**, **CashierSession**, **ReconciliationBatch**,
  **DispatchException** (single-use, bound to order, job set, maximum outstanding amount, policy version and
  expiry). Invariant: balance = posted charges − allocations − credits + refunds; numbers never reused; card
  credentials never stored.
- **NotificationIntent → Delivery**, **CustomerLink** (random 128-bit token, purpose-bound, expiring, revocable,
  rate-limited), **Feedback**, **ServiceRecoveryCase**.
- **IntegrationEvent**, **WebhookSubscription/Delivery**, **PaymentIntent**, **AccountingExportBatch**, **PrintJob**.
- **DataSubjectRequest**, **RetentionPolicy** (configuration), **AuditEvent** (append-only, hash-chained).

### 4.6 PWA architecture (#50, #51, #52)

- Routing by role-optimised shells: phone (bottom navigation + scanner-first "Scan" action), tablet
  (master-detail), desktop (side navigation + dense tables). Layouts chosen by container queries, not user agent.
- State: TanStack Query for server state with `Idempotency-Key`, `X-Correlation-Id`, `X-Client-Version` and
  anti-forgery header injection and conflict handling; server-side shared drafts for measurement capture and order
  intake (#28, #32b); encrypted local drafts and the persisted offline queue arrive in #51.
- Network and permission states from W1 (#50): `useNetworkState()`, a persistent non-dismissable
  `NetworkStatusBanner`, `OfflineBlockedAction` ("Needs connection — this will not be queued") for billing,
  payment and any non-allowlisted command, `RetryableError` (plain-language problem details with a Retry that
  reuses the same `Idempotency-Key` and never discards typed input) and `Forbidden`; toasts are never used for
  scan results, sync state or actionable errors.
- Bounded offline queue only for approved idempotent operations (scan submissions and the doorstep
  delivery-confirmed scan that references an online dispatch authorisation); billing, payment and inventory
  reconciliation are online-only with the explicit blocked-action state.
- Service worker: precache versioned assets; network-first for API; stale-while-revalidate only for an explicit
  allowlist of non-sensitive reference endpoints; never cache protected responses; update prompt driven by
  `GET /api/version` and the server's 426 response for outdated clients.
- Design system: tokens (colour, type scale, spacing, motion, focus, density, breakpoints, light/dark/high-contrast
  for sunlight), accessible components with one `FieldProps` form contract and a shared step-aware
  `FormErrorSummary`, numeric measurement inputs (`FractionInput` for inch fractions, `NumericStepper`), scanner,
  camera capture, tables and cards, filters, drawers/dialogs (bottom sheet on phone), timeline, status badges (icon
  + text, never colour alone), alerts, empty/error/loading/offline/forbidden states, `ConfirmDialog` in three tiers
  (confirm; confirm with reason; typed confirmation on desktop/tablet admin actions only — never on phone
  layouts) plus a client-side 3-second undo for non-sensitive field actions. Components emit no inline
  scripts/styles (CSP-compatible), carry WCAG 2.2 specifics (button alternatives to every drag, focus never
  obscured by bottom bars or the virtual keyboard, route and status announcements, `autocomplete`/`inputmode`
  hints, consistent Help placement) and are documented in Storybook with axe checks, a pseudo-locale story and
  visual regression baselines; a Playwright helper asserts no horizontal overflow and no obscured focus at
  320/360/768/1024/1280 px and 200% zoom.
- Localisation from W1: FormatJS/`react-intl` with ICU MessageFormat, `en-IN` default and a `ta-IN` catalogue
  skeleton; `<html lang>` follows the user's locale (stored on the user, editable on the profile and admin
  screens); Tamil-capable font subsets shipped with the Latin subsets; layouts tolerate 40% text growth; one
  `formatters` module for INR (lakh/crore grouping), `dd-MM-yyyy` and 12-hour time; PDFs embed a Tamil-capable
  font; customer-facing pages follow the customer's language.
- Installable from W1: `manifest.webmanifest` (maskable icons, `display: standalone`, screenshots), iOS meta tags
  and an Install page ship with #50 so device evidence from W2 onward is recorded in installed mode as well as in
  a browser tab; the service worker, update flow and offline queue arrive in #51.
- Generated API client from OpenAPI (`openapi-typescript` + `openapi-fetch`), so contract changes fail the
  frontend type check.
- Client telemetry module (web vitals, unhandled errors with stack hashes, service-worker failures, capability
  detection, scanner metrics) posted batched, sampled and redacted to a same-origin endpoint (#52, #58).

### 4.7 Deployment topology (#22 interim, #59 hardened)

- Images: `tailor360-web` (host + PWA), `tailor360-worker`, `tailor360-cli`; non-root, read-only filesystem,
  pinned base images, SBOM + signature + provenance.
- Compose stack: reverse proxy (Caddy, ACME DNS-01 so certificates work behind NAT), web, worker, one-shot
  `migrate` service that web and worker depend on (`condition: service_completed_successfully`), PostgreSQL image
  with pgBackRest, MinIO, ClamAV, OpenTelemetry collector (self-hosted observability as an optional profile),
  Mailpit (non-production); every service caps container logs (`json-file`, 50 MB × 5). Environments: dev
  (compose), test (CI ephemeral), interim staging (single VM from W1 behind a network gate, synthetic data),
  staging and production (IaC-managed, #59).
- Release: build once, promote the identical digest after signature and provenance verification; deployment to a
  VM is **pull-based** (`tailor360-deploy` on the VM verifies the cosign signature, runs `migrate`, then
  `docker compose up -d --wait --pull always`; no GitHub-held SSH key to production); Compose has no rolling
  update, so a web container swap costs a few seconds of 502s (accepted against the availability target,
  minimised with `--wait` and proxy retry; blue-green arrives with Kubernetes); expand-migrate-contract with the
  startup check tolerating applied-but-unknown migrations so release N runs against a database at N+1; rollback
  = redeploy tag N, rehearsed with the database at N+1 **and** PWA N+1 cached in a browser (`minimumClient` is
  raised only in the release after the change that requires it).

---

## 5. Engineering standards and Definition of Done

These apply to every implementation issue and are the content of `CLAUDE.md` (#22).

### 5.1 Definition of Done (per pull request)

1. Linked to exactly one issue (or sub-issue); only scoped changes; branch `feat/eXX-fYY-<slug>`; enforced by the
   PR policy check (#22).
2. Server-side authorisation, validation, idempotency (where the endpoint is retried) and audit events on every
   state-changing endpoint; every new or changed endpoint adds its role × own-branch/other-branch expectations
   (and field mask where applicable) to the authorisation matrix fixtures.
3. Unit tests for domain rules, integration tests for persistence/API, architecture tests still green,
   E2E coverage for any new critical journey; synthetic data only.
4. Migrations forward-only and backward compatible with the previous release (expand/contract); rollback or
   restore note in the PR (`docs/dev/migrations.md`).
5. OpenAPI updated (or the endpoint marked internal); generated client regenerated; no undocumented breaking
   change (`oasdiff` gate).
6. No secrets, no PII in logs, telemetry names and redaction reviewed; the threat model covering the flow is
   referenced and its mapped controls closed (from #56a onward).
7. For UI changes: accessibility check (axe and the `docs/nfr/a11y-checklist.md` screen-reader items for a new
   journey), responsive check (phone/tablet/desktop profiles, overflow and obscured-focus helper), pseudo-locale
   story, CSP clean, Storybook stories for the loading, empty, error, offline and forbidden state of every new
   screen, and one network-failure step in the journey's E2E (request aborted → retry succeeds with exactly one
   effect).
8. Documentation updated (module README, ADR if a decision changed, runbook if operations changed, metric
   dictionary if a report changed).
9. PR evidence checklist completed (tests run, screenshots for UI, migration output, security notes) — a
   required status check verifies the linked issue, branch name and checklist.

### 5.2 Conventions

- Money, time, identifiers and rounding per D9–D11; no floating point for money.
- Naming: schemas `identity`, `customers`, `catalog`, `media`, `orders`, `custody`, `inventory`, `billing`,
  `reporting`, `notifications`, `integration`, `platform`; tables snake_case; all tables carry `id`,
  `organisation_id`, `branch_id` (where scoped), `created_at`, `created_by`, `updated_at`, `updated_by`, `xmin`.
- Append-only tables (audit events, ledger entries, scan events, custody transfers, posted invoices, payments,
  receipts, QC results) are protected by database triggers that reject UPDATE/DELETE from the application role.
- No soft-delete of business records; deactivation flags instead. Hard delete only for retention-policy jobs on
  approved classes (media derivatives, expired drafts, expired links, expired exports).
- REST: `/api/v1/{module}/{resource}`; commands as POST sub-resources (`/orders/{id}/confirm`); cursor
  pagination; `filter[...]`, `sort`, `fields`; problem details errors; `X-Correlation-Id`; `X-Client-Version`.
- Domain events named in past tense; integration events versioned `orders.order-confirmed.v1` with JSON Schema
  and examples under `docs/integration/events/`; payloads carry identifiers, codes, statuses, timestamps, amounts
  and branch codes only unless the event is classified personal and the subscriber is approved for it.
- Logging: no request bodies, tokens, measurements, image bytes, rendered message bodies, recipient addresses or
  card data; correlation and causation IDs everywhere.
- Tabular exports/imports: every cell starting with `=`, `+`, `-`, `@`, tab or CR is prefixed with `'` and quoted;
  UTF-8 with BOM; rows per file capped (default 50,000, larger exports paginate into a zip); XLSX cells are strings
  unless the metric dictionary types the column; CSV imports (#38) are limited to 10 MB / 20,000 rows, parsed with
  a strict RFC 4180 parser, rejected on control characters and previewed before commit; a unit test covers the
  injection corpus for every export path.
- Frontend: TypeScript strict, ESLint + Prettier, no `any`, components documented in Storybook, all user-facing
  text through `react-intl` message ids (ICU plurals, no string concatenation), `en-IN` first with a `ta-IN`
  catalogue, formats through the shared `formatters` module.

### 5.3 Test pyramid and gates

| Layer | Tooling | Gate |
| --- | --- | --- |
| Unit and property | xUnit, FsCheck, Vitest | every PR |
| Architecture | NetArchTest/ArchUnitNET rules `ARCH-001`… (module boundaries, endpoint policy, audit filter, vendor SDK isolation) | every PR |
| Integration | Testcontainers PostgreSQL/MinIO started once per run (template database cloned per class, `Respawn` resets), WebApplicationFactory, authorisation matrix; `FakeMalwareScanner` by default, the real ClamAV contract test in a separate job | every PR (PR pipeline budget ≤ 15 min) |
| Contract | OpenAPI lint (Spectral) + diff (oasdiff) + endpoint inventory, adapter contract suites | every PR |
| E2E + accessibility | Playwright (Chromium/Firefox/WebKit; phone/tablet/desktop; portrait/landscape), axe, overflow helper | every PR for touched journeys; full nightly |
| Security | CodeQL, dependency review with licence allowlist, gitleaks, Trivy (images/IaC), SBOM, expired-exception check | every PR / release |
| Performance | Lighthouse CI budgets, k6 smoke; full load and mixed-load test per release | release |
| Operations | migration dry-run, restore test, DR exercise | release / scheduled |

---

## 6. Delivery plan: milestones, waves, epic closure

### 6.1 Ordering rules

- The order below is derived from the "Depends on" field of every feature issue. An issue starts only when its
  dependencies are merged to `main` (or the owner explicitly accepts a stub defined in this plan).
- One issue = one branch = one Claude Code session = one pull request. Extra-large issues are split into GitHub
  sub-issues (linked under the parent) before work starts; each sub-issue has its own branch, session and PR,
  and the parent closes when all sub-issues are merged. Target < 1,500 changed lines per PR excluding generated
  code and tests.
- Three lanes can run concurrently: **Lane A** backend platform and modules, **Lane B** PWA and design system,
  **Lane C** governance, security and operations documents. Issues in the same parallel group touch disjoint
  modules; where two issues must touch one module (Reporting in W4), they are sequenced.
- Business approvals (workshops, accountant, device matrix) are scheduled as review gates at the end of the
  wave that produces the draft, never as blockers for drafting.

### 6.2 Waves

| Wave | Milestone | Issues (parallel groups shown with `∥`) | Exit gate |
| --- | --- | --- | --- |
| W0 | M1 | #17 → #18 → #19 | Owner approves glossary, workflow maps, category hierarchy, ADRs and NFRs; owner confirms D1 (backend platform and SDK-capable environment) and D17 (hosting model and indicative budget) in writing so ADR-0002/ADR-0010 are final and no critical architecture decision blocks W1 (Section 11 items 1–2 closed); repo has `docs/prd`, `docs/adr`, `docs/nfr`, `docs/process` |
| W1 | M2 | #20 → #21 → (#22 ∥ #23 ∥ #50) → #24 → (#25 ∥ #53) | Clean clone builds; CI gates and branch protection block bad changes; interim staging reachable over TLS with synthetic data; login with MFA behind BFF; permission matrix approved and regression suite green; admin UI; design system in Storybook |
| W2 | M3 | (#26 ∥ #29 ∥ #56a) → #27 → (#28 ∥ #30) → #31 | Customer search/dedup with consent contract; threat models for every flow; catalog and measurement templates seeded and published; measurement wizard on phone/tablet; design catalog with immutable snapshots; secure image pipeline |
| W3 | M4 | #41 → #32a → #32b → (#33 ∥ #35) → (#34 ∥ #36) → #37 | Multi-garment order confirmed with snapshots and estimate PDF; job cards; workflow engine, workboards and due-date alerts; labels printed and scanned on real devices; custody chain end-to-end with fail-closed dispatch gate |
| W4 | M5 | (#38 ∥ #42 ∥ #54) → (#39 ∥ #43 ∥ #47) → (#44 ∥ #40 ∥ #48) → #45 → (#46 ∥ #49 ∥ #55) | Ledger-backed stock; posted GST invoices and receipts; payment-gated dispatch with partial-delivery policy; notifications; feedback; reconciled reports with visible freshness; adapters behind flags |
| W5 | M6 | (#51 ∥ #56b ∥ #57) → (#52 ∥ #58) → (#59 ∥ #61a) → #60 → #61b → #61c | Installable PWA with safe updates and offline scan queue; ASVS baseline and pen test; privacy/audit; observability and load tests; CI/CD promotion; backups/DR rehearsed; UAT and go-live |

Dependency notes (each is a deliberate deviation from, or completion of, the issues' own "Depends on" lines):

1. **#41 ↔ #32 cycle.** Issue #41 (pricing engine) lists #32 (orders) as a dependency while #32 needs the pricing
   service for order totals. The plan replaces #41's dependency on E06-F01 (#32) with a dependency on the
   pricing contract `Billing.Contracts.IPricingService` (`PricingRequest` = catalog service/product references,
   quantities, discounts, place of supply, effective date; `PricingResult` = line components, document totals,
   configuration versions). The contract is the first deliverable of #41 and never references an order entity
   (an architecture test rejects any `Billing → Orders` reference). #41 therefore starts W3 with dependencies
   #19 and #29 only; #32 depends on #41 in addition to its own list. Until the owner amends issue #41
   (Section 11, item 11), the implementing session records the supersession in its PR description. If the owner
   declines, #41 moves after #32 and #32 ships with a feature-flagged stub `IPricingService` (catalogue base rates,
   no tax, not usable for invoicing) replaced when #41 merges.
2. **#37 dispatch gate before #43.** #37's dispatch scan must enforce the payment rule, but Billing's payment
   module (#43) is delivered later and itself depends on #37. #37 introduces
   `Billing.Contracts.IDispatchEligibilityQuery.GetDispatchEligibility(orderId, jobIds[])` with a default
   implementation that **fails closed** (`Eligible = false, Reason = NotEvaluated`); dispatch is rejected unless the
   query returns eligible or an approved dispatch exception exists (the single Billing-owned mechanism defined in
   #43; #37 never reads `dispatch_exceptions` directly). #43 registers the balance-based rule; #48 adds the
   partial-delivery policy and the two-stage dispatch (online receive scan records a dispatch authorisation; the
   doorstep confirmation references it). The W3 rehearsal records dispatch through the exception path; paid
   dispatch is rehearsed again in #43 and #48.
3. **#34 ready-for-delivery gate before #37.** The gate's `CustodyReconciled` predicate resolves through
   `Custody.Contracts.ICustodyStateQuery`; #34 registers a placeholder returning `Unknown` and the predicate is
   disabled by configuration until #37 supplies the real query and enables it.
4. **Reporting module sequencing in W4.** #44 creates the Reporting module foundations; #45 and then #46 add
   only their own projections, one migration each (`<issue>_<slug>`) and screens, merged sequentially. #40 serves
   its operational inventory views from the Inventory module and does not touch the `reporting` schema.
   Notifications in the same W4 group: #40 adds only routing rules and templates under
   `Notifications/Routing/Inventory*` and `Templates/inventory.*`; #48 adds the `status` link purpose, delivery
   templates and `Templates/delivery.*`; neither modifies the #47 service, adapters or existing templates, and the
   second PR to merge rebases and re-runs the #47 routing tests.
5. **#56 split.** Threat models must precede the flows they cover, so #56 is split into #56a (W2, Lane C: threat
   models, abuse cases, ASVS traceability skeleton, CI severity gates) and #56b (W5: security regression suite,
   enforcing CSP, penetration test and remediation).
6. **Media references before #31.** #27 diagrams and #30 illustrations are stored as media references; until #31
   merges they resolve to bundled static line drawings by asset key; #31 adds the admin upload path and a
   migration registering the bundled assets as media objects.
7. **Email before #47.** #23 introduces `IEmailSender` (SMTP adapter, fake for tests) in `Platform.Abstractions`
   for recovery, MFA enrolment and invitations; #47 moves adapters under Integration and adds templates,
   consent and tracking without changing the port.
8. **Client drafts and offline queue.** #28 and #32 use server-side drafts only; #37 retries scan submissions in
   memory; #51 is the only implementation of encrypted local drafts and the persisted offline scan queue.
9. **Customer links before #47/#48.** The estimate share link required by #32 uses the Notifications-owned
   customer-link mechanism (Section 4.4). #32a creates the Notifications module skeleton with only
   `notifications.customer_links` (token hash, purpose, subject reference, branch, expiry, revocation, rate-limit
   counters), `Notifications.Contracts.ICustomerLinkIssuer.Issue(purpose, subjectRef, ttl)` /
   `ICustomerLinkResolver.Resolve(token)` and the justified anonymous route `/c/estimate/{token}` (rate-limited,
   audited). #47 leaves the table and port unchanged; #48 adds the `status` purpose and page, #49 the `feedback`
   purpose, without changing the port.
10. **Confirmation participants before #35.** #32a defines `Platform.Abstractions.IConfirmationParticipant<TEvent>`
    (invoked inside the confirmation transaction, in registration order, sharing its connection; any exception
    rolls the confirmation back) and invokes all registered participants for `GarmentJobCreated`. With none
    registered, confirmation succeeds and the job card shows "label not yet available". #35 registers
    `IBarcodeIdentityAllocator` as the first participant; the W3 exit gate "labels printed" is verified after #35.
11. **Sub-issue splits and how dependents resolve.** #32 → #32a (backend) and #32b (intake UI); #56 → #56a/#56b
    (note 5); #61 → #61a (strategy and fixtures), #61b (regression suite), #61c (UAT, pilot, go-live). An issue
    that depends on a split parent depends on the sub-issue that delivers what it consumes: #33 needs #32a and
    #32b; #35 and #42 need #32a only; #58 needs #56a only (#56b is nonetheless sequenced before it); #59 needs
    #56b; #61's own dependencies are carried by #61a (#52), #61b (#59, which requires #56b and #58) and #61c (#60).
12. **`IPdfRenderer` before #35/#42.** #32a introduces `Platform.Abstractions.IPdfRenderer` with the QuestPDF
    adapter (licence check recorded in the PR), the Tamil-capable font and a `FakePdfRenderer`; #35 (labels) and
    #42 (invoices) reuse it.
13. **Read contracts consumed later.** #26 delivers `Customers.Contracts.ICustomerSnapshotQuery` (used by #32a and
    #42), #32a delivers `Orders.Contracts.IOrderSnapshotQuery` (used by #42, #43, #45) and #25 delivers
    `Identity.Contracts.IUserDirectory` (used by #33, #44, #45).

### 6.3 Roadmap acceptance criteria mapped to verifying issues

| Roadmap acceptance criterion (#1) | Verified in |
| --- | --- |
| End-to-end garment lifecycle with real labels and devices | #37 (custody rehearsal), #48 (dispatch leg), #61c (pilot) |
| Dispatch blocked until QC passes and payment rule satisfied | #34 (gate), #37 (fail-closed dispatch scan), #43 (rule), #48 (policy) |
| Role and branch isolation tests | #24 (regression suite and two-branch walkthrough), #56b |
| Photos and measurements protected by consent, retention, access controls | #26, #28, #31, #57 |
| Browser/device matrix | #52 |
| WCAG 2.2 AA on critical journeys | #50, #52 |
| No unresolved critical/high security findings | #56b (pen test), #59 |
| Load/performance targets and SLOs | #19 (targets), #58 (tests) |
| Backup, PITR, rollback, DR exercises | #59, #60 |
| GST configuration and sample output approved by accountant | #41, #42, #44 |
| UAT sign-off, training, runbooks, monitoring, hypercare | #61c |

### 6.4 Epic codes, issue numbers and exit criteria

Epic codes used in Section 7 map to the GitHub epic issues as follows (note the non-sequential numbering of
E09–E15):

| Epic | Issue | Children | Wave(s) |
| --- | --- | --- | --- |
| E01 | #2 | #17, #18, #19 | W0 |
| E02 | #3 | #20, #21, #22 | W1 |
| E03 | #4 | #23, #24, #25 | W1 |
| E04 | #5 | #26, #27, #28 | W2 |
| E05 | #6 | #29, #30, #31 | W2 |
| E06 | #7 | #32, #33, #34 | W3 |
| E07 | #8 | #35, #36, #37 | W3 |
| E08 | #9 | #38, #39, #40 | W4 |
| E09 | #13 | #41, #42, #43 | W3–W4 |
| E10 | #10 | #44, #45, #46 | W4 |
| E11 | #11 | #47, #48, #49 | W4 |
| E12 | #12 | #50, #51, #52 | W1, W5 |
| E13 | #14 | #53, #54, #55 | W1, W4 |
| E14 | #15 | #56, #57, #58 | W2, W5 |
| E15 | #16 | #59, #60, #61 | W5 |

An epic closes only when every child is merged with evidence, each exit criterion below has linked evidence,
architecture/security review findings are resolved, and the product owner records acceptance of the epic's
end-to-end workflow as a comment on the epic issue. The last PR of each epic adds `docs/evidence/eXX-closure.md`
collecting the links, and the roadmap checklist item in #1 is ticked in the same PR.

| Epic | Exit criterion | Verified by |
| --- | --- | --- |
| E01 | Workflow maps approved by owner | #17 walkthrough sign-off (W0 gate) |
| E01 | Context map, ownership, diagrams, ADRs version-controlled | #18 |
| E01 | NFRs measurable and mapped to CI/UAT evidence | #19 `docs/nfr/traceability.md` |
| E01 | No unresolved critical architecture/compliance decision | Section 11 items 1–2 closed before W1 |
| E02 | Clean checkout builds/runs via documented commands | #20 clean-clone logs (Windows/WSL and macOS/Linux) |
| E02 | CI validates compile, format, tests, migrations, dependencies, containers, security | #20 minimal CI, #22 failing-check demos |
| E02 | No production secret/customer data needed locally | #20 `.env.example`, #21 `seed-synthetic` |
| E02 | Claude Code instructions and governance concise, testable, versioned | #22 workflow demonstration |
| E03 | Permission matrix approved and enforced by policy | #24 (owner-approved `docs/security/permission-matrix.md`) |
| E03 | Horizontal/vertical escalation tests pass | #24 matrix + IDOR suite, #56b regression suite |
| E03 | Sensitive admin actions need step-up and are audited | #25 |
| E03 | Disabled users/revoked sessions lose access within SLO | #23, #25 (SLO value from #19) |
| E04 | Customer created/located without avoidable duplicates | #26 |
| E04 | Every category has an approved template and rules | #27 business review |
| E04 | Orders retain the exact measurement version | #28 immutability, #32 snapshot regression |
| E04 | Measurement journeys pass usability, a11y, authz, offline-resilience | #28 device tests, #51, #52 |
| E05 | Admins add/retire categories and options without deployment | #29 demo, #30 demo |
| E05 | Invalid option combinations prevented by configurable rules | #30 rule-engine tests |
| E05 | Tailors see exact approved designs/images on the job card | #30, #32 |
| E05 | Unauthorized/expired media access denied and logged | #31 |
| E06 | Every garment independently traceable | #32 job numbers and dependencies, #35 identities |
| E06 | Only authorized, valid, idempotent transitions | #33, #37 |
| E06 | QC failure routes to rework without losing history | #34 |
| E06 | Responsive queues for priority/overdue/blocked/assigned | #33 |
| E07 | No PII in any barcode payload | #35 payload test, #56b regression |
| E07 | Real labels and devices complete the physical workflow | #37 rehearsal, #48 dispatch leg, #61c pilot |
| E07 | Duplicate/out-of-order scans cannot corrupt state | #37 |
| E07 | Missing/rejected/damaged/reprinted labels have auditable recovery | #35 reprint, #37 reconciliation cases |
| E08 | Stock on hand derived from reconciled ledger | #39 rebuild property test and reconciliation job |
| E08 | No oversubscription under concurrency | #39 |
| E08 | Low-stock alerts to correct branch/role, no storms | #40, #47 |
| E08 | Stocktake/adjustment evidence auditable | #40 |
| E09 | Calculations pass accountant examples | #41 golden master |
| E09 | Posted records immutable; compensating corrections | #42, #43 |
| E09 | Concurrent numbering and callbacks idempotent | #42, #43, #55 |
| E09 | Cashier reconciliation and dispatch gate pass UAT | #43, #48 |
| E10 | Totals reconcile to approved samples | #44, #46 |
| E10 | Role/branch restrictions on screens, scheduled generation, downloads, exports | #44, #46 |
| E10 | Large-range queries meet targets without affecting order processing | #44 isolation, #46 mixed-load test, #58 |
| E10 | Definitions, freshness, limitations visible and documented | #44 metric dictionary and status strip, #45 |
| E11 | Unpaid/failed-QC cannot dispatch without authorized override | #37, #43, #48 |
| E11 | Retries do not send uncontrolled duplicates | #47 |
| E11 | Links expire, revocable, non-enumerable | #48 |
| E11 | Negative feedback/alteration create follow-up | #49 |
| E12 | Critical journeys pass device/browser/orientation matrix | #52 |
| E12 | Updates cannot strand an incompatible client/API | #51, #53 |
| E12 | No unresolved critical a11y violations | #52 |
| E12 | Performance/CWV budgets pass on mobile | #52 |
| E13 | Integration failures cannot corrupt core transactions | #21 outbox tests, #54, #55, #58 |
| E13 | Webhook signature/replay/idempotency/scoping tests pass | #54 |
| E13 | Provider replacement needs no domain change | #55 architecture test |
| E13 | Contracts, limits, errors, support ownership documented | #53, #55 |
| E14 | No unresolved critical/high finding | #56b pen test |
| E14 | Privacy/retention workflows evidenced and enforceable | #57 |
| E14 | SLO dashboards/alerts with runbooks | #58 |
| E14 | Security/performance/resilience/incident simulations pass | #56b, #58 game days |
| E15 | Release deployed and rolled back via automation | #59 |
| E15 | Restore/DR meet RPO/RTO | #60 |
| E15 | All acceptance gates and UAT pass | #61 |
| E15 | Ops ownership, monitoring, training, hypercare active | #61c, #58 |

---

## 7. Traceability matrix

Branch names follow `feat/eXX-fYY-<slug>`; docs-only issues use `docs/`. Size: S ≤ 1 session, M 1–2, L 2–3,
XL split into sub-issues. "Depends on" is the plan's operative list (it equals the issue's list unless a
Section 6.2 note says otherwise). "Evidence" lists what the PR must attach to satisfy the issue's acceptance
criteria.

| Issue | Epic | Wave | Lane | Branch | Size | Depends on | Modules / areas | Key evidence |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| #17 | E01 | W0 | C | `docs/e01-f01-workflows-glossary` | M | #1 | docs/prd | Current-state and target workflow maps per category, branch scenarios, glossary, RACI, exception catalogue and review record, configurable-vs-fixed table, confirmed category hierarchy, proposed measurement field sets, traceability to backlog issues, owner approval |
| #18 | E01 | W0 | C | `docs/e01-f02-architecture-adrs` | M | #17 | docs/adr, docs/architecture | C4 diagrams, ADR-0001…0013, module/storage ownership table, invariants, conventions, `architecture-rules.md` (`ARCH-001`…) |
| #19 | E01 | W0 | C | `docs/e01-f03-nfr-slo-dod` | M | #17, #18 | docs/nfr, docs/process | Numeric NFRs per candidate hosting model, SLOs incl. revocation SLO, RPO/RTO, data classification incl. credentials, DoR/DoD, release gates with severity and waiver owner, stakeholder and risk review records, traceability matrix |
| #20 | E02 | W1 | A | `feat/e02-f01-scaffold-local-env` | L | #18 | solution, compose, health, minimal CI | Clean-clone logs from Windows/WSL and macOS/Linux, tests run twice, all five component health checks, architecture test failing on forbidden reference, branch-protection screenshot, secret scan clean |
| #21 | E02 | W1 | A | `feat/e02-f02-persistence-outbox-config-flags` | L | #20 | Platform.Persistence, Worker, CLI | Outbox atomicity/duplicate/poison/replay tests, audit interceptor tests, config fail-fast test, secret-leak test, flag consistency test, migration from empty DB and prior snapshot |
| #22 | E02 | W1 | C | `feat/e02-f03-ci-governance-claude-staging` | M | #20, #21 | .github, CLAUDE.md, infra/compose staging | Failing checks demo (format/test/arch/secret/vuln), PR policy check, templates, CODEOWNERS, permissions review, artefacts/summaries, workflow demonstration with revert, interim staging URL, CI baseline time |
| #23 | E03 | W1 | A+B | `feat/e03-f01-auth-sessions-mfa` | L | #18, #19, #21 | Identity, Web host, Platform.Abstractions (email), PWA auth screens | Cookie prefix/attribute, CSRF (incl. login CSRF), lockout, fixation tests; no-token-in-storage browser test; revocation SLO test; timeout-warning and in-place re-auth test; MFA/passkey/recovery flows on phone and desktop; reset-then-login still demands MFA; authentication threat model; auth audit sample |
| #24 | E03 | W1 | A | `feat/e03-f02-rbac-branch-scope` | L | #23 | Platform.Security, all Api | Owner-approved permission matrix, generated matrix test report, field-mask assertions, IDOR/cross-branch tests, worker context test, two-branch manual walkthrough record |
| #25 | E03 | W1 | A+B | `feat/e03-f03-admin-users-branches-flags` | M | #23, #24, #50 | Identity Api, Platform (flag/outbox endpoints), PWA admin | Admin authorisation tests, step-up test, suspension revocation test, concurrent update test, audit before/after view, UAT script |
| #26 | E04 | W2 | A+B | `feat/e04-f01-customers-consent-dedup` | L | #24, #25 | Customers, Web host (timeline composition), PWA | Normalisation/dedup unit tests, merge/correction/deactivation/export/concurrency/timeline integration tests, consent contract tests, field-mask tests, phone/tablet screenshots |
| #27 | E04 | W2 | A+B | `feat/e04-f02-measurement-templates` | M | #17, #24, #29 | Customers (templates), Catalog validator, PWA admin | Property tests for unit conversion, lifecycle/permission tests, publish-time validation tests, seeded templates for 5 categories, business review note |
| #28 | E04 | W2 | A+B | `feat/e04-f03-measurement-capture` | L | #26, #27 | Customers (drafts/versions), PWA wizard | Validation/snapshot tests, idempotent confirm test, concurrent confirm test, template-change test, log-redaction test, interrupted-capture recovery on phone, device usability notes |
| #29 | E05 | W2 | A+B | `feat/e05-f01-category-service-catalog` | M | #17, #21, #25 | Catalog, PWA admin | Hierarchy/lifecycle tests, validator registration and availability contract tests, cache invalidation test, seeded categories, "add category without code" demo |
| #30 | E05 | W2 | A+B | `feat/e05-f02-design-catalog-snapshots` | L | #29, #27 | Catalog (rules, snapshot builder), PWA picker and job-card component | Rule-engine property tests, snapshot immutability after republish, 409 on concurrent revision, seeded option groups, tablet usability notes, "add option without deployment" demo |
| #31 | E05 | W2 | A+B | `feat/e05-f03-secure-media-pipeline` | L | #24, #26, #30 | Media, Worker, PWA | Adversarial upload corpus results (incl. decompression bombs), per-request authorisation and storage-endpoint isolation tests, EXIF strip test, SSE health check, retention idempotency/hold tests, outage/orphan cleanup tests, five-photo upload on throttled 4G < 20 s |
| #32a | E06 | W3 | A | `feat/e06-f01a-orders-backend` | L | #28, #29, #30, #31, #41 | Orders, confirmation-participant hook (note 10), `IPdfRenderer` (note 12), Notifications `customer_links` skeleton (note 9), `IOrderSnapshotQuery` (note 13) | Atomic/idempotent confirmation tests, snapshot regression, identifier tests, estimate PDF snapshot, revision-window tests, timeline source |
| #32b | E06 | W3 | B | `feat/e06-f01b-intake-ui` | L | #32a | PWA intake, Measurements-needed queue | Multi-garment intake on phone/tablet (screenshots), shared resumable drafts with per-garment measurement status, duplicate-garment, estimate print/share, validation summary, E2E confirm |
| #33 | E06 | W3 | A+B | `feat/e06-f02-workflow-assignment-workboard` | L | #32a, #32b, #24 | Orders (workflow, capabilities, SLA evaluator), Worker, PWA queues, admin editor | Graph property tests, invalid transition tests, concurrent transition tests, eligibility tests, due/SLA evaluator tests (timezone, dedup), workboard device test |
| #34 | E06 | W3 | A+B | `feat/e06-f03-qc-rework-alteration-hold-cancel` | L | #33 | Orders, Catalog (QC checklist versions), PWA | State-machine tests for every exceptional path, gate predicate tests incl. placeholder, events contract tests, dashboard reconciliation test, UAT notes |
| #35 | E07 | W3 | A+B | `feat/e07-f01-barcode-identity-labels` | L | #32a, #24 | Custody, Platform.Abstractions (`IBarcodeRenderer`, `IPrintQueue`), PWA print station and label screens | Collision/checksum/no-PII tests, partial unique index and reprint concurrency tests, authorisation tests per permission, resolve endpoint tests, printed test sheets scanned on devices and printers (incl. a label queued from a phone and printed by a station), by-number lookup tests, `identifiers.md` |
| #36 | E07 | W3 | B | `feat/e07-f02-scanner-experience` | M | #35, #50 | PWA scanner | Parser/debounce tests, continuous-mode E2E with duplicate and wrong-namespace scans, permission-denied fallback E2E (manual by job number), feedback visible with sound off, Android/iPhone/iPad (installed mode) and hardware scanner results, one-handed and accessibility review |
| #37 | E07 | W3 | A+B | `feat/e07-f03-custody-transfers-idempotency` | L | #35, #36, #33, #34 | Custody, Orders, Billing.Contracts (dispatch eligibility), PWA | State/property tests, concurrency/replay tests, fail-closed dispatch tests, reconciliation walkthrough per exception type, physical rehearsal record |
| #38 | E08 | W4 | A+B | `feat/e08-f01-inventory-masters` | M | #17, #21, #24 | Inventory, PWA | Unit conversion property tests, import/deactivation/permission tests, initial catalog review |
| #39 | E08 | W4 | A+B | `feat/e08-f02-stock-ledger-reservations` | L | #38, #33 | Inventory, PWA (receipts, workboard material panel, item timeline) | Balance invariant property tests, concurrency reservation tests, half-post failure tests, purchase/transfer/consume/return/correction E2E on device |
| #40 | E08 | W4 | A+B | `feat/e08-f03-low-stock-stocktake-valuation` | L | #38, #39, #47 | Inventory, Worker, Notifications (alert routing), PWA | Alert dedup and policy tests, stocktake approval separation tests, valuation golden-master tests, reconciliation footer, performance tests |
| #41 | E09 | W3 | A | `feat/e09-f01-pricing-gst-engine` | L | #19, #29 (the issue's #32 dependency is replaced by the pricing contract, see 6.2) | Billing (engine), PWA admin | Accountant golden-master tests, rounding property tests, version publish/reproduction tests, no `Billing → Orders` reference test |
| #42 | E09 | W4 | A+B | `feat/e09-f02-invoices-numbering-pdf` | L | #41, #32a | Billing, PDF adapter, PWA | Concurrent numbering tests, immutability trigger tests, from-order conversion idempotency, PDF snapshot + totals-match-snapshot + accessibility, barcode retrieval auth tests, accountant review |
| #43 | E09 | W4 | A+B | `feat/e09-f03-payments-receipts-cashier-dispatch-gate` | L | #42, #37, #24 | Billing, Custody (gate), PWA | Allocation property tests, idempotent payment tests, append-only tests, cashier close reconciliation tests, dispatch eligibility semantics tests, override separation tests, UAT |
| #44 | E10 | W4 | A+B | `feat/e10-f01-sales-gst-receivables-reports` | L | #42, #43, #21 | Reporting (foundations), Worker, PWA | Golden-data reconciliation, historical stability after new price/tax version, filter-inference and export leak tests, scheduled report authorisation test, timezone boundary and volume tests |
| #45 | E10 | W4 | A+B | `feat/e10-f02-pipeline-workload-quality-analytics` | M | #33, #34, #37, #44 | Reporting, PWA dashboards | Projection replay/idempotency tests, reconciliation to source jobs, performance test, ops UAT |
| #46 | E10 | W4 | A+B | `feat/e10-f03-inventory-profitability-exports` | M | #39, #40, #42, #43, #45 | Reporting, Worker exports, PWA | Reconciliation tests, export authorisation/expiry/cleanup tests, mixed-load test |
| #47 | E11 | W4 | A+B | `feat/e11-f01-notifications-templates-adapters` | L | #21, #26, #54 | Notifications, Integration adapters, Worker, PWA centre | Adapter contract/retry tests, consent/quiet-hour/dedup tests, template safety tests, audit/redaction tests, provider-down drill |
| #48 | E11 | W4 | A+B | `feat/e11-f02-delivery-queue-status-links-dispatch` | L | #34, #37, #43, #47 | Custody (queue), Notifications (links), PWA delivery | Unpaid/paid/partial E2E under each policy, token security tests, compensating custody tests, delivery-team mobile UAT |
| #49 | E11 | W4 | A+B | `feat/e11-f03-feedback-alterations-service-recovery` | M | #47, #48 | Notifications (feedback), Orders (alteration contract), PWA | Token replay/rate-limit tests, follow-up idempotency/escalation/closure tests, journey UAT |
| #50 | E12 | W1 | B | `feat/e12-f01-design-system-layouts` | L | #19, #20 | PWA design system, Storybook, manifest and Install page, i18n foundation, network/permission states | Component a11y + visual regression, form contract tests, pseudo-locale stories, reference journeys per role walked with VoiceOver/TalkBack, device/orientation/zoom matrix, overflow and obscured-focus helper, CSP report-only clean |
| #51 | E12 | W5 | B | `feat/e12-f02-pwa-install-updates-resilience` | L | #50, #21, #37, #48, #53 | PWA service worker, local drafts, offline queue | Install/update/rollback tests, 426 handling, offline/reconnect/duplicate/conflict E2E incl. doorstep delivery confirmation, cache/privacy inspection, quota and eviction tests, docs |
| #52 | E12 | W5 | B | `feat/e12-f03-wcag-cross-browser-performance` | M | #50, #51 | tests/e2e, Lighthouse CI, client telemetry | Cross-browser and axe reports, manual screen-reader/zoom notes, performance budget before/after report, client telemetry redaction test |
| #53 | E13 | W1 | A | `feat/e13-f01-api-standards-openapi-idempotency` | M | #18, #21, #23, #24 | Platform, Web host, contract tests | OpenAPI lint/diff and endpoint-inventory test in CI, idempotency/concurrency/timeout tests, rate-limit policy catalogue and forwarded-header spoof test, error-leak tests, version-compatibility test, enforcing CSP with zero violations across the #50 reference journeys, browser BFF/CSRF/no-token-storage run |
| #54 | E13 | W4 | A | `feat/e13-f02-integration-events-webhooks` | M | #21, #53 | Integration, module mappers, Worker | Commit/rollback/duplicate/ordering/replay tests, signature/rotation/SSRF tests, policy filter tests, slow-receiver load test |
| #55 | E13 | W4 | A | `feat/e13-f03-provider-adapters` | L | #53, #54, #43, #47 | Integration adapters (payment, accounting, print bridge) | Contract suites, fault injection (no half-posting), callback-never-posts and status-verification tests, SSRF/outbound policy tests, reconciliation report tests, accounting balanced totals, vendor-SDK isolation test |
| #56a | E14 | W2 | C | `docs/e14-f01a-threat-models-asvs` | M | #18, #19, #24, #53 | docs/security, CI gates | Threat models per flow, abuse cases, ASVS traceability skeleton with owners, vulnerability management and exception register, severity gates and licence audit in CI |
| #56b | E14 | W5 | C+A | `feat/e14-f01b-security-baseline-regression-pentest` | L | #56a, #55 | Platform.Security, CI, tests | Enforcing CSP, automated security regression suite, ASVS traceability audit, pen test report and remediation |
| #57 | E14 | W5 | A | `feat/e14-f02-privacy-audit-encryption-secrets` | L | #19, #24, #26, #31 | Platform (audit integrity, retention policies), Customers, Media, Worker | Data inventory, data-subject request tests, retention job tests, audit hash-chain/gap/completeness review, rotation exercise log |
| #58 | E14 | W5 | A+C | `feat/e14-f03-observability-slo-resilience` | L | #19, #21, #56a | Platform.Observability, infra/observability, tests/load | Telemetry redaction tests, burn-rate alerts with runbooks, retry-safety fault test, load/soak/mixed-load results vs NFRs, game-day records |
| #59 | E15 | W5 | C | `feat/e15-f01-environments-cicd-releases` | L | #22, #56b, #58 | infra, .github | Provenance chain, deploy-time signature check, clean-environment provisioning test, failed migration/health and rollback rehearsals, patch-management workflow, permissions review |
| #60 | E15 | W5 | C | `feat/e15-f02-backups-pitr-dr-runbooks` | M | #57, #58, #59 | infra/backup, docs/runbooks | Automated restore test, PITR and missing-object exercises, DR exercise by a non-author operator with RPO/RTO, backup access/immutability review |
| #61a | E15 | W5 | A+C | `feat/e15-f03a-test-strategy-fixtures` | M | #52 (runs alongside #59; gates only #61b) | tests/fixtures, docs/qa | Test strategy, consolidated fixture catalogue, import validation flow or "no source data" decision |
| #61b | E15 | W5 | B | `feat/e15-f03b-e2e-suite` | L | #61a, #59 | tests/e2e | Business-scenario regression per category and exception with stored artefacts, dress rehearsal incl. restore and rollback |
| #61c | E15 | W5 | C | `feat/e15-f03c-uat-pilot-golive` | L | #61b, #60 | docs/uat, docs/training, docs/launch | UAT signatures, accountant approval, training material, pilot report with entry/exit criteria, go/no-go record, hypercare and post-launch review schedule, release evidence index |

---

## 8. Issue blueprints: E01–E07

Each blueprint gives the implementing session enough shape to plan the PR; it does not replace the issue's own
acceptance criteria, which remain the contract.

### #17 [E01-F01] Workflow maps, glossary, configurable taxonomy

- **Deliverables**: `docs/prd/00-overview.md`; `docs/prd/glossary.md`; `docs/prd/workflows/<category>.md` with two
  sections each — *Current practice* (paper registers, verbal handoffs, paper job cards, cash book, as observed in
  the workshop) and *Target workflow* (Mermaid flowcharts for happy path and exceptions) — closing with a "what
  changes for staff" table that feeds training (#61c); `docs/prd/workflows/branch-scenarios.md` (single branch,
  customer served at a second branch, cross-branch garment/material transfer, branch-specific category/price
  availability, branch closure/holiday), each naming the owning module and the branch-scope rule (#24);
  `docs/prd/state-transitions.md` (transition, actor, preconditions, outputs, audit event, exception behaviour);
  `docs/prd/raci.md`; `docs/prd/configurable-vs-fixed.md`; `docs/prd/category-hierarchy.md` (confirmed initial
  hierarchy Blouse → Pattern, Aari work; Salwar; Lehenga; Gown; Kids; code/naming conventions; the rule that
  administrators add categories without deployment; the per-category links every service type carries:
  measurement template, workflow definition, design option groups, price-list item, QC checklist);
  `docs/prd/measurement-templates.md` (proposed field sets per category: key, label, group, canonical unit mm,
  display units, precision, required, ranges, conditional rules, diagram — the seed source for #27);
  `docs/prd/assumptions-and-open-decisions.md` with an explicit **Out of scope** section (customer self-service
  accounts, multi-legal-entity tenancy, e-commerce).
- **Method**: Claude drafts from the issues and this plan; owner runs the workshops using the drafts as the
  agenda; decisions are recorded in the open-decisions file and folded back into the documents.
- **Exception catalogue to map**: duplicate customer, missing material, changed measurements, rejected QC,
  rework, late order, damaged label, cancelled order, refund, unpaid dispatch attempt, negative feedback.
- **Evidence**: one end-to-end walkthrough per category in `docs/prd/walkthroughs.md`; exception flows reviewed
  with one representative each of Reception, Tailor Master, Inventory, Cashier and Delivery, recorded in
  `docs/prd/reviews/exception-review.md`; traceability table with columns *original request → workflow(s) →
  owning module → backlog issue(s)*; any request without an issue gets a decision (new issue or out of scope).

### #18 [E01-F02] Modular architecture, ownership, data model, ADRs

- **Deliverables**: `docs/architecture/context.md`, `container.md`, `components.md`, `deployment.md`,
  `sequences/` (order confirmation with barcode allocation, barcode handoff, invoice posting + payment, stock
  reservation + consumption), `docs/architecture/module-ownership.md` (Section 4.3 expanded, including the
  object-storage ownership column), `docs/architecture/invariants.md`, `docs/architecture/conventions.md`
  (money, time, identifiers, concurrency, API versioning, migration compatibility),
  `docs/architecture/architecture-rules.md` (rules `ARCH-001`… with assertion, allowed exceptions and the #20
  test class implementing each), ADRs in `docs/adr/` using MADR format: ADR-0001 modular monolith and extraction
  criteria; ADR-0002 .NET 10 LTS and Minimal APIs; ADR-0003 React PWA; ADR-0004 PostgreSQL schema-per-module;
  ADR-0005 object storage and authorised delivery; ADR-0006 BFF cookie session; ADR-0007 branch-aware single
  tenancy; ADR-0008 transactional outbox and background workers; ADR-0009 configurable taxonomy as versioned
  data; ADR-0010 deployment portability (containers, compose baseline, Kubernetes-ready); ADR-0011 reporting read
  models; ADR-0012 integration adapters and ports; ADR-0013 caching (D21).
- **Architecture-test rules** (implemented in #20, extended by later issues): `Domain` references only
  `Platform.Abstractions`; `Application` never references another module's `Infrastructure`; only `Contracts`
  and `Platform.*` cross modules; no `DbContext` maps tables of another schema; hosts reference modules only
  through registration extensions; every endpoint declares a policy or a justified `[AllowAnonymous]`; every
  command endpoint carries the audit filter; provider SDK packages are referenced only by
  `Integration.Infrastructure` and test projects; `Billing` never references `Orders`; `Reporting` references
  only `Contracts` projects.
- **Evidence**: review record of the four representative flows against the diagrams; failure-mode notes for
  database, object storage and worker outages.

### #19 [E01-F03] NFRs, SLOs, data policy, Definition of Done

- **Deliverables**: `docs/nfr/support-matrix.md` (devices, browsers, OS versions, orientations, camera/scanner/
  printer capabilities and fallbacks); `docs/nfr/capacity-and-performance.md` (users, branches, orders, scans,
  stock transactions, images, reports, concurrent billing; budgets for LCP/INP/CLS, API p95, bundle sizes,
  memory on the lowest supported device for the scan and capture screens); `docs/nfr/slo.md` stating
  availability, latency, error rate, job lag, notification latency, RPO, RTO, backup retention and restore-test
  cadence **per candidate hosting model** (single-VM on-prem/compose vs managed cloud) with the monthly cost band,
  backup destination, failover mechanism and staffing each requires — the selected model is marked once Section
  11 decision 2 is taken and the compatibility statement is signed by the owner; `docs/nfr/data-classification.md`
  (classes incl. credentials and secrets: password hashes, MFA secrets, recovery codes, session tickets, provider
  keys, backup keys; purpose, consent, access, retention, backup treatment for DB, media, logs, audit, exports,
  backups; items needing legal review marked); `docs/nfr/accessibility-localisation.md`;
  `docs/nfr/security-operations-targets.md` (patching and vulnerability SLAs, incident targets);
  `docs/process/definition-of-ready.md`, `definition-of-done.md`, `release-gates.md` (blocking severity and
  authorised waiver owner per gate with expiry), `waivers.md`; `docs/nfr/traceability.md` (NFR → test/monitor/
  evidence → owner); `docs/nfr/reviews/stakeholder-review.md` (product, engineering, security, operations and the
  accountant, who confirms GST record retention, financial-data classification and export needs);
  `docs/nfr/risk-review.md` (infeasible or costly targets with mitigation or waiver, completed before W1).
- **Proposed starting targets** (to be confirmed): availability 99.5% monthly; API p95 < 400 ms for reads and
  < 800 ms for commands at the stated load; scan round-trip p95 < 1 s on 4G; outbox lag < 30 s p95; session
  revocation and user deactivation effective on every host within 60 s p99; MFA challenge round-trip < 2 s p95;
  RPO ≤ 15 min (WAL archiving), RTO ≤ 4 h; backups retained 35 days plus monthly for 12 months; critical
  vulnerability fix ≤ 7 days, high ≤ 30 days, medium ≤ 90 days. Capacity starts from assumption A5 per hosting
  model with the monthly cost band; `accessibility-localisation.md` fixes launch languages (English UI; Tamil UI
  when the catalogue is ≥ 95% translated), the Tamil glossary for measurements and phases (from #17), and the
  rule that customer-facing pages follow the customer's language.

### #20 [E02-F01] Scaffold solution, module boundaries, local environment

- **Deliverables**: solution per Section 4.2 with empty module skeletons and registration extensions; PWA
  skeleton (Vite, TypeScript strict, ESLint, Prettier, Vitest, Storybook placeholder); `infra/compose/`
  (`docker-compose.yml` for PostgreSQL 16, MinIO, ClamAV, Mailpit, OpenTelemetry collector; observability stack
  optional profile) with `healthcheck` blocks (`pg_isready`, `/minio/health/ready`, `clamdcheck`, Mailpit);
  `.env.example` files with no secrets; `Tailor360.Cli` with `migrate`, `init-reference-data` (idempotent,
  production-safe: roles, permissions, default catalog, document sequences, an initial owner account created
  from a one-time secret) and `seed-synthetic` (development and test only; refuses unconditionally when
  `ASPNETCORE_ENVIRONMENT=Production`, with no override flag; in #20 it creates only the minimal rows the health
  checks need — one organisation, one branch — and #21 supplies the full deterministic dataset).
- **Health checks per component**: web host `/health/live`, `/health/ready`, `/health/startup` (database, object
  storage, ClamAV when enabled); worker host exposes the same three endpoints on `WORKER_HEALTH_PORT` reporting
  database connectivity and a heartbeat row in `platform.worker_heartbeats` (stale heartbeat → unhealthy); PWA
  check `GET /` returns the app shell whose build hash matches `GET /api/version`; `./scripts/dev status` prints
  all five states and its output is the PR evidence. Health payloads expose component status only, never
  configuration values.
- **Cross-platform commands**: `scripts/dev` (bash) and `scripts/dev.ps1` (PowerShell) with identical verbs
  `up | restore | build | test | run | reset | status`; `up` runs `restore` (`dotnet restore`, `pnpm install`)
  first. `docs/dev/setup.md` (Windows 11 + WSL2, macOS, Linux), `troubleshooting.md`, `ports.md`, `commands.md`.
- **Minimal CI and branch protection**: `.github/workflows/ci.yml` with restore/build/test for .NET and pnpm, and
  branch protection on `main` (required status check, one review, no direct pushes, linear history) so that #21
  and every later PR is gated; #22 extends the same workflow.
- **Integration-test infrastructure**: one shared container set per test run (`[CollectionDefinition]` fixture
  starts PostgreSQL and MinIO once; a migrated template database is cloned per test class with `CREATE DATABASE …
  TEMPLATE` and `Respawn` resets tables between tests); `FakeMalwareScanner` (allow/deny by content marker, EICAR
  recognised) is the default `IMalwareScanner`; the real `clamd` contract test (`[Trait("Requires","ClamAv")]`)
  runs in a separate job with a pinned image that bundles the signature database (no `freshclam` in CI), on PRs
  touching `src/Modules/Media/**` and nightly otherwise; Testcontainers images pinned by digest and cached;
  Playwright browsers cached. Test tiers are explicit traits: `Unit`, `Architecture`, `Contract` run anywhere;
  `Integration` uses `TAILOR360_TEST_DATABASE_URL` / `TAILOR360_TEST_S3_ENDPOINT` when set, else Testcontainers
  when a Docker daemon is reachable, else **skips with a visible warning** — `CI=true` turns that skip into a
  failure so nothing merges unverified.
- **Training banner**: the shell shows a persistent "TRAINING — not real data" banner when
  `GET /api/version.environment` is not `production`.
- **Architecture tests** from #18's rule list, including a deliberately failing example kept as a negative test.
- **Evidence**: clean-clone build logs from Windows/WSL and macOS or Linux; tests run twice to expose order
  dependence; secret scan output; branch-protection settings screenshot.
- **Environment prerequisite** (verified in the planning session: no `dotnet`, Node 22 present, Docker CLI without a
  daemon): `infra/dev-environment/` records the network allowlist a SessionStart hook needs (`dotnet.microsoft.com`,
  `builds.dotnet.microsoft.com`, `api.nuget.org`, `registry.npmjs.org`, the Playwright browser CDN), the hook
  itself (`dotnet-install.sh --channel 10.0`, `pnpm install --frozen-lockfile`, `pnpm exec playwright install
  chromium`) and `scripts/dev doctor`, which prints which test tiers can run in the current environment
  (Section 12).

### #21 [E02-F02] Persistence conventions, migrations, outbox, configuration, flags

- **Platform.Persistence**: base `ModuleDbContext` applying conventions (snake_case, `timestamptz`, `numeric`
  money, `xmin` concurrency, audit columns, schema name); migration runner with one history table per module
  schema (`<schema>.__ef_migrations_history`), a fixed context order (`platform`, `identity`, then modules
  alphabetically) under one `pg_advisory_lock`, and startup validation that refuses to serve when any migration in
  the running assembly is **unapplied** while tolerating applied migrations the assembly does not know (the state
  during a rollback to N with the database at N+1; never compare for set equality); `outbox_messages` (with the
  lease columns of Section 4.4), `inbox_messages`, `idempotency_keys`, `sequences`, `audit_events` (append-only,
  trigger-protected, hash-chained by a `BEFORE INSERT` trigger, month-partitioned), `feature_flags`,
  `feature_flag_evaluations` (sampled audit), `job_leases`, `worker_heartbeats` (per instance),
  `data_protection_keys`, `print_jobs`; `IAuditWriter`, the `SaveChanges` audit interceptor and the `[Audited]`
  endpoint filter (Section 4.4); the database roles, secret-file provider, Data Protection ring and connection
  budget of Section 4.4 (`docs/platform/database.md`, `docs/platform/secrets.md`); `migrate` refuses to run with
  the application role; the compatibility rule that release N+1 ships expand migrations only and the matching
  contract migration ships in N+2 at the earliest.
- **Worker**: outbox dispatcher per Section 4.4 (lease claim, per-aggregate ordering, `LISTEN/NOTIFY` wake-up,
  backoff with jitter, dead letter) built and tested with **two** dispatcher instances; inbox de-duplication for
  handlers; `job_leases` scheduler; per-instance heartbeat writer; in-process watchdog.
- **Sequencing note**: #21 precedes authentication (#23) and the permission catalogue (#24), so #21 exposes **no
  HTTP endpoints** for outbox replay or flag mutation. Operator replay and flag mutation ship as guarded
  `Tailor360.Cli` commands (`replay-outbox <id|--dead-letter>`, `flags set <key> --scope org|branch --reason`)
  executed with an operator identity recorded in `platform.audit_events`; the application services declare the
  authorisation requirements (`admin.outbox.replay`, `admin.feature_flags`) that #24 fulfils; the HTTP endpoints
  are delivered in #25 with step-up and reason.
- **Configuration**: typed options with `ValidateDataAnnotations().ValidateOnStart()`; secret sources: environment
  variables and Docker/Kubernetes secrets; optional Vault/Key Vault provider behind an interface.
- **Flags**: organisation/branch scope, mutation restricted to `admin.feature_flags` with reason, cached
  evaluation with `LISTEN/NOTIFY` change notification and a documented bound (visible on the mutating node
  immediately and on every node within 30 s; each evaluation records the flag version applied) in
  `docs/platform/feature-flags.md`; safe default off.
- **Seed**: `init-reference-data` (roles, permissions, default catalog placeholders, sequences, initial owner) is
  idempotent and safe in production; `seed-synthetic` creates deterministic synthetic branches (two, for
  branch-scope tests), users per role, and sample business data with fixed IDs for development and automated
  tests only and refuses unconditionally in production. This dataset is the origin of the fixture library
  consolidated by #61a.
- **Docs**: `docs/dev/migrations.md` — expand/contract rules, per-module migration generation, startup refusal on
  pending migrations, backward-compatibility check against the previous release snapshot, rollback/restore
  procedure (forward-only fix migration first; restore from backup when data was mutated), referenced by the PR
  template.
- **Health**: migration state, outbox backlog/age, failed jobs, flag store, database connectivity, heartbeats.
- **Tests**: outbox commit/rollback atomicity; duplicate delivery no double effect; two dispatchers never
  double-process or reorder one aggregate; expired lease redelivered exactly once; poison message to dead letter
  and replay; audit row written atomically with the mutation and rolled back with it; a direct UPDATE on
  `audit_events` by a superuser breaks chain verification for every later row; each database role's grants and
  REVOKEs (including "`t360_app` cannot ALTER its own tables" and "`migrate` refuses the app role"); concurrent
  update conflict; config missing → startup failure; anti-forgery token issued before a rolling restart is
  accepted after it (Data Protection ring); pool budget validated against `max_connections`; secrets never leak
  (sentinel values from each secret file never appear in logs, exception output, problem details, `/health/*` or
  exporter output, including on startup validation failure); flag propagation bound with two hosts; migration
  from empty DB and from the N-1 release snapshot (`tests/fixtures/db-snapshots/`, N-1 only; older snapshots are
  deleted and migrations older than two releases may be squashed).

### #22 [E02-F03] CI quality gates, repository governance, Claude Code workflow, interim staging

- **`CLAUDE.md`** (concise): architecture summary, commands (`dotnet build/test`, `pnpm test`, `./scripts/dev`),
  module boundary rules, security rules (authorisation, validation, idempotency, audit, no secrets/PII in logs),
  testing rules, Git workflow (one issue/one branch/one PR, Conventional Commits with `Refs #NN`), Definition of
  Done; path-scoped rules in `src/Modules/CLAUDE.md`, `clients/pwa/CLAUDE.md`, `infra/CLAUDE.md`.
- **Templates and governance**: issue templates (feature, bug, security), PR template mirroring the evidence
  checklist, ADR template, threat-model template, release evidence checklist; `CODEOWNERS`; branch protection
  documentation; **PR policy job** (`.github/workflows/pr-policy.yml`, a required status check) that fails when
  the PR body does not link exactly one open issue (`Refs #NN` / `Closes #NN`), when the branch name does not
  match `feat|fix|docs/eXX-fYY[a-c]?-<slug>`, or when a mandatory evidence-checklist item is unchecked once the PR
  is ready for review; a `release-ready` label is applied only by this check.
- **CI (`.github/workflows/ci.yml`)**: restore/build (.NET + pnpm), formatting (`dotnet format --verify-no-changes`,
  Prettier), lint (ESLint, analyzers as errors), unit/integration/architecture tests with Testcontainers, migration
  check (apply to empty DB + snapshot), PWA build and Vitest, dependency review, gitleaks, CodeQL, Trivy on images
  and IaC, SBOM (Syft) upload; artefacts and summaries (TRX/JUnit results, Cobertura coverage with a per-project
  floor, Playwright traces on failure, SARIF from CodeQL/Trivy/gitleaks, failing tests and rules written to
  `GITHUB_STEP_SUMMARY`); least-privilege `permissions:` blocks; third-party actions pinned by SHA; CI runs on
  `pull_request` only (never `pull_request_target` with a checkout of the PR head); deployment secrets exist only
  in protected GitHub Environments with required reviewers; fork PRs receive no secrets; `id-token: write` only in
  the release workflow; Dependabot with grouped updates; documented security exception process.
- **Interim staging** (Lane C): `infra/compose/docker-compose.staging.yml` (Caddy with ACME DNS-01, seeded synthetic
  data, no production secrets or provider credentials, its own Data Protection ring, `noindex`) deployed by the
  same pull-based `tailor360-deploy` script production will use (Section 4.7), so #59 hardens it instead of
  rebuilding it. Guard-rails: reachable only through a network gate in front of Caddy (IP allowlist for the shop
  networks **and** an identity-aware proxy or WireGuard/Tailscale enrolment for phones and tablets; the application
  login is the second factor, never the only one; until #23 merges there is no public hostname at all);
  PostgreSQL, MinIO and ClamAV ports are never published beyond the compose network; `/health/*` and the worker
  health port bind to the internal network. Deploy policy: each `main` deploy runs `migrate`; on failure the job
  resets the environment (`down -v`, `migrate`, `init-reference-data`, `seed-synthetic`), labels the run
  `staging-reset` and opens an issue. The VM location (cloud vs shop server) is decided with Section 11 item 2 and
  recorded in `docs/dev/staging.md` with how the shop network reaches it for printer and scanner rehearsals.
- **Evidence**: runs on deliberately broken branches (format, test, architecture, secret, vulnerability); baseline
  CI duration against the fixed budget (PR pipeline ≤ 15 min wall clock: build, unit, architecture, integration,
  contract, touched-journey E2E on Chromium; Firefox/WebKit and the device matrix nightly; an issue is opened when
  a PR pipeline exceeds the budget twice in a week); **workflow demonstration** — a sample change taken through issue →
  `feat/…` branch → PR with plan and evidence checklist → CI green → CODEOWNERS review → squash merge → tagged
  build, then reverted through a second PR to demonstrate rollback, recorded in `docs/process/workflow-demo.md`.

### #23 [E03-F01] Authentication, sessions, MFA, recovery

- **Identity module**: ASP.NET Core Identity user store in schema `identity`; password policy (length ≥ 12, breached
  password check via local k-anonymity list adapter optional); Argon2id hasher; TOTP authenticator enrolment with
  recovery codes; passkeys (WebAuthn) for phishing-resistant login; MFA required for Owner, Admin, Cashier and
  any role with `billing.*`/`admin.*` permissions (configurable per role).
- **Session/BFF**: cookie auth per Section 4.4 (`__Host-` prefix, opaque session id, rotation, sliding inactivity
  timeout default 30 min with the two-minute warning dialog and in-place re-authentication that retries the
  pending request, absolute timeout default 12 h); server-side ticket store (`identity.sessions`: id, user, device
  label, IP, user agent, created, last seen, absolute expiry, `last_strong_auth_at`, revoked); anti-forgery
  token endpoint (`Cache-Control: no-store`, excluded from the service-worker cache) and header validation
  including on login/MFA/passkey/recovery endpoints; `Origin`/`Sec-Fetch-Site` check; security headers baseline
  and a report-only CSP (enforcing from #53); revocation checked per request via the cache in D21; `GET /me`
  returns the user's locale and display preferences.
- **Shared devices**: after a successful MFA login the server may issue a revocable trusted-device cookie (per
  user, 30 days, listed in the session inventory) so later logins from that device need only the password for the
  roles the owner allows (never `admin.*`/`billing.post_invoice` unless configured; Section 11 item 12); the
  login screen offers an account switcher (names/initials of users previously signed in on the device, no
  session material); a **Lock** action in the shell ends the UI session without clearing server drafts.
- **Abuse controls**: per-account and per-IP throttling, progressive lockout, generic error messages, optional
  CAPTCHA adapter, anti-enumeration on recovery (same response timing and message).
- **Outbound email**: `Platform.Abstractions.IEmailSender` and a minimal `IEmailTemplateRenderer` for recovery,
  invitation and security-alert messages, `FakeEmailSender` for tests, SMTP adapter (Mailpit locally, configured
  relay elsewhere), no message body or token in logs; #25 invitations reuse it; #47 moves adapters under
  Integration.
- **Recovery**: email-based verified reset with single-use expiring token. Password recovery never alters MFA:
  after a reset the user still completes their TOTP/passkey challenge. Lost-MFA recovery is only
  `POST /admin/users/{id}/reset-mfa` by an Admin/Owner with step-up, a reason and out-of-band identity
  verification recorded in the audit event; it invalidates all sessions, sends the user a security alert and
  forces re-enrolment at next login. Recovery codes are single-use (hashed) and rate-limited by the
  `mfa-challenge` policy. MFA enrolment shows the `otpauth://` link as **Open in authenticator app** and the
  manual key with **Copy** beside the QR code, so enrolment works on the same phone.
- **Audit**: login success/failure, MFA enrol/challenge, recovery, session create/revoke, suspicious activity.
- **Endpoints**: `POST /api/v1/auth/login`, `/mfa/challenge`, `/passkeys/*`, `/logout`, `/logout-all`,
  `GET /me`, `GET/DELETE /sessions`, `POST /recovery/request`, `/recovery/confirm`, `GET /antiforgery`.
- **PWA screens (Lane B, on the #20 skeleton, re-skinned by #50)**: login; MFA challenge (TOTP, passkey, recovery
  code); MFA enrolment (QR + one-time recovery-code display); passkey registration and management; recovery
  request/confirm; session and device inventory with revoke and logout-all; step-up re-authentication dialog
  (reused by #25). Keyboard- and screen-reader-usable on phone/tablet/desktop; no token material client-side.
- **Threat model** (required verification): `docs/security/threat-models/authentication.md` using the #22
  threat-model template when it has already merged; otherwise #23 commits `docs/templates/threat-model.md` (the
  structure given for #56a) and #22 adopts it unchanged — DFD of login, MFA, passkey, recovery and admin reset; STRIDE table; abuse cases (credential stuffing,
  recovery-token interception, MFA fatigue, session fixation, admin reset misuse, step-up bypass); each
  mitigation mapped to a test in this PR; reviewed before merge; consolidated by #56a.
- **Tests**: session lifecycle, lockout, CSRF rejection, fixation (new session id after login), replay after
  revocation (denied within the #19 SLO), enumeration timing; Playwright browser tests for cookies/headers/logout
  and an assertion that `localStorage`, `sessionStorage` and IndexedDB contain no token/ticket material after
  login and MFA, that `document.cookie` cannot read the session cookie and that the anti-forgery token is the
  only value scripts can read; login CSRF (forged login form from another origin rejected); cookie prefix and
  attributes asserted; passkey ceremony rejects a challenge issued to another session; wizard step → session
  expired → in-place re-auth → confirm yields exactly one version; reset-then-login still demands MFA;
  `reset-mfa` without step-up is 403 and audited.

### #24 [E03-F02] RBAC, branch scopes, authorisation regression tests

- **Permission catalogue** in `Platform.Security` as a strongly typed list grouped by module with the
  `RequiresMfa`/`RequiresStepUp` flags of Section 4.4 (initial flagged set: all `admin.*`; `catalog.*.publish`;
  `billing.approve_dispatch_exception`, `billing.override_price`, `payments.reverse`, `payments.refund`,
  `payments.allocate_manual`; `inventory.approve_variance`, `inventory.approve_negative_stock`; `customers.merge`,
  `customers.export`, `customers.restrict`, `customers.request_deletion`; `custody.reprint_label`,
  `custody.invalidate_label`, `custody.generate_identity`, `custody.approve_reconciliation`; `audit.export`;
  `reports.export` above the row threshold; `notifications.replay`, `notifications.manage_templates`;
  `integration.manage_webhooks`, `integration.replay_delivery`; `admin.outbox.replay`); roles Owner, Admin,
  **Branch Manager**, Reception, Measurement Staff, Tailor Master, Tailor, Inventory Clerk, Cashier, Delivery
  Staff, Auditor — plus the vendor-side HyFib super-user — with default grants in seed data and an admin-editable
  role → permission map (custom roles allowed). *(Corrected on 2026-09-06 by the delivered
  `docs/security/permission-matrix.md`, which this list is one role short of: Branch Manager is the sole named
  actor for `orders.reschedule` in `docs/prd/state-transitions.md`, is accountable for four rows of
  `docs/prd/raci.md`, and is the role `docs/prd/00-overview.md` defines Admin as a superset of. Section 2 of the
  matrix records the choice as a documented default pending OD-13 and tabulates what changes if the owner decides
  otherwise.)*;
  `docs/security/permission-matrix.md` (role × permission × branch scope × flags with rationale) reviewed and
  approved by the owner before merge; the generated matrix test reads this file so approval and enforcement
  cannot diverge.
- **Policies**: `PermissionRequirement`, `BranchScopeRequirement` (user branch assignments vs resource branch),
  `ResourceOwnershipRequirement` (tailor sees assigned jobs only); endpoint filters that load the resource's
  branch before the handler runs; deny-by-default for every endpoint (architecture test); **field-level
  minimisation**: response DTOs for Tailor-facing endpoints (job card, measurement sheet, work queue) exclude
  customer contact details, pricing and payment state; matrix fixtures declare the allowed field set per role.
- **Background context**: `IWorkerScopeFactory` and `[WorkerJob]` per Section 4.4 (architecture test: no type
  outside the worker/CLI hosts references the factory; every worker job declares its scope); impersonation
  principal for jobs acting for a user (aborts with `job.requester-unauthorised` when the user has lost the
  permission); media and download endpoints re-check authorisation on every request.
- **Regression suite**: generated matrix test (role × endpoint × own-branch/other-branch, plus field masks) from
  `EndpointDataSource` metadata (the OpenAPI document is completed by #53) so an endpoint without a policy cannot
  escape the matrix; fixtures in `tests/Tailor360.IntegrationTests/Authorization/matrix.yaml` extended by every
  later PR (DoD item 2), with a fresh/stale step-up dimension for flagged permissions and a `transfer-pending`
  branch dimension (#37); IDOR tests with foreign identifiers; export and job authorisation tests; denied
  state-changing and step-up requests audited and coalesced per actor/endpoint/minute, never sampled.
- **Manual two-branch walkthrough** (required verification): two seeded branches with one representative user per
  role in each; scripted in `docs/security/role-walkthrough.md`; each role exercises its permitted actions in its
  own branch and the corresponding denied actions against the other branch (including edited identifiers);
  screenshots or problem-details responses per step attached to the PR.
- **Carried forward, and where.** Four clauses above are not delivered by #24 and are not delivered by nobody
  either — each names the issue that takes it, so that none of them becomes an unowned sentence in a plan:
  - *Media and download endpoints re-check authorisation on every request* — there is no media endpoint, no
    download endpoint and no object-storage code yet. **#28/#29**, which build them, and which the rule is
    recorded against in `docs/security/permission-matrix.md` section 7.
  - *Field-level minimisation* — the mechanism (`IFieldVisibilityPolicy`, the three declared response views) is
    built, registered and unit-tested, and no endpoint projects through it because no endpoint returns any of the
    three views. **#32a/#33/#26**, the issues that publish them; `docs/security/field-visibility.md` says at the
    top that it describes an intent until then.
  - *Sections 4 to 6 of the two-branch walkthrough* — they need an endpoint that enforces a permission and an
    administration surface that can create users, neither of which exists. **#25 and #32a**. Section 3, which
    needs neither, is run and its results are recorded.
  - *`docs/security/threat-models/authorisation.md`* — **#32a**, the first issue to publish a permissioned route,
    so that the model is written about endpoints that exist rather than about a mechanism in the abstract.

### #25 [E03-F03] Audited administration for users, branches, permissions, flags

- **Screens (desktop-first, tablet-capable)**: users (invite/create, activate, suspend, branch assignment, role
  assignment, tailor skills attribute, default locale, MFA status and `reset-mfa` per the #23 rules, trusted
  devices, revoke sessions), branches (code, name, timezone, working calendar and holidays, address, GST
  registration reference, contact, status, data-scope rules, print stations), roles/permissions with flags,
  feature flags and module/menu toggles, outbox dead-letter replay, audit history drawer (before/after, actor,
  reason, correlation ID, effective time).
- **Endpoints** (every mutation requires `Idempotency-Key`, `If-Match`, a reason and writes audit):
  `GET/POST /api/v1/admin/users`, `POST /api/v1/admin/users/{id}/invite|activate|suspend|reinstate|reset-mfa|revoke-sessions`,
  `PUT /api/v1/admin/users/{id}/branches`, `PUT /api/v1/admin/users/{id}/roles`; `GET/POST/PUT /api/v1/admin/branches`,
  `PUT /api/v1/admin/branches/{id}/calendar`, `POST /api/v1/admin/branches/{id}/deactivate`;
  `GET/PUT /api/v1/admin/roles/{id}/permissions`; `GET/PUT /api/v1/admin/feature-flags/{key}`,
  `PUT /api/v1/admin/modules/{code}/enabled`; `GET /api/v1/admin/audit?subject=…`;
  `POST /api/v1/admin/outbox/{id}/replay`.
- **Permissions**: `admin.users`, `admin.branches`, `admin.roles` (Owner, Admin); `admin.feature_flags` (Owner and
  the HyFib super-user role only); `admin.audit.read` (Owner, Auditor); `admin.outbox.replay` (Owner, Admin).
- **Contract**: `Identity.Contracts.IUserDirectory` (id, display name, roles, branch assignments, tailor-skills
  attribute, locale, active flag) for #33 assignment validation, #44 report recipients and #45 workload views;
  workers query it under their declared scope.
- **Composition, carried from #24**: `Identity.Infrastructure.Access.RequesterAuthorityStore` implements the
  platform's `IRequesterAuthorityStore` over `IUserAccessQuery` and is registered by `AddIdentityModule`, so any
  host that composes Identity answers "may this person still do this?" from the database. `Tailor360.Worker`
  composes no module today and therefore still resolves the fail-closed default, which refuses every job that
  declares `ActsForRequester`; the first such job is what makes the worker compose Identity, and that is this
  issue's to do alongside `IUserDirectory`. Also here: the command-line tool's `--operator <user>` / `--reason`
  outside Development, which needs `AddTailor360WorkerScopes()` in `CliHost` and a declared job identity for the
  destructive commands — today `init-reference-data` and `seed-synthetic` write rows attributed to whatever
  `IAuditContext` the host happens to register.
- **Controls**: step-up (re-authenticate with MFA within 5 minutes) for role/permission/billing/security changes;
  mandatory reason; dual confirmation for Owner-level changes; no deletion of referenced identities/branches
  (deactivate); safe search/filter/pagination; export restricted to Auditor/Owner.
- **Tests**: authorisation for every admin action, concurrent role/flag updates (optimistic concurrency),
  suspension revokes sessions immediately, historical references stable after deactivation; UAT script for
  onboarding, transfer, suspension, emergency revocation — written as
  [`docs/process/uat-administration.md`](process/uat-administration.md), and **not yet run**: it needs a business
  owner at a keyboard, which is the point of it.
- **Delivered 2026-09-07, with three things deliberately not built.** The **working calendar** is deferred to #33,
  the issue that first computes a promise date, because its semantics have no consumer to source them from until
  then (OD-06). The **tailor-skills attribute** on `IUserDirectory` is deferred to #45 for the same reason. And
  **dual confirmation** — a second administrator approving an Owner-level change — is recorded as **OD-16** rather
  than invented: step-up, a mandatory reason and before-and-after audit are built and enforced, and the issue's own
  criterion reads "step-up/confirmation", but who may approve, within what window, and what a single-Owner shop
  does are product decisions.

### #26 [E04-F01] Customer profiles, consent, search, deduplication, timeline

- **Model** (`customers` schema): `customers` (customer_number `C-<branch>-000001`, name, normalised_name,
  primary_phone, alternate_phone, phone_normalised (E.164), email, address, language, preferences, notes,
  status, visibility branches), `customer_aliases`, `consent_records` (purpose, wording version, source,
  granted/withdrawn, actor, time), `duplicate_candidates` (score, reasons, decision), `customer_merges`
  (survivor, merged, actor, reason). Consent purposes are seeded configuration (`measurement_storage`,
  `photo_capture`, `transactional_messages`, `marketing_messages`, `feedback_requests`) with versioned wording.
- **Search**: exact (phone incl. last 4–6 digits, customer number, alias) and fuzzy (trigram on `normalised_name`,
  which is produced by a transliteration-aware normaliser with a configurable substitution table for common
  Tamil→Latin variants — ksh/tch/ch, v/w, doubled consonants, final -i/-y/-ee, optional h — documented with test
  vectors in `docs/customers/name-normalisation.md`; an optional `name_native` Tamil-script column is stored and
  searched directly) with permission filtering; the search screen has a segmented **Phone / Name** mode (tel
  keypad vs text keyboard) and shows name, native name, masked phone, branch and last-order date so Reception can
  disambiguate without opening records, with **Create new** and the inline duplicate warning always visible;
  duplicate scoring at create time (phone match, name similarity on both name forms, address similarity) with
  explanation; merge (`POST /customers/{survivor}/merge`, `customers.merge`) writes `customer_merges`, keeps the
  merged number as an alias, re-points measurements/orders through `CustomerMerged`, is irreversible, and keeps the
  merged history readable in the survivor's timeline.
- **Lifecycle and corrections**: `PUT /api/v1/customers/{id}` (with `If-Match`) is a correction with mandatory
  reason and before/after audit; `id` and `customer_number` never change. `POST /customers/{id}/deactivate` and
  `/reactivate` (`customers.deactivate`, reason); deactivated customers stay searchable to Owner/Admin/Auditor and
  remain referenced by orders and invoices. There is **no delete endpoint**; the retention-approved erasure
  workflow arrives with #57 and refuses while orders, invoices, payments or holds reference the record.
  `POST /customers/{id}/export` (`customers.export`, Owner/Auditor by default) produces an audited, expiring
  download of profile, consent history and measurement summary (no images) — the basis of #57's subject-access
  export.
- **Consent contract** (`Customers.Contracts`): `IConsentQuery.Get(customerId, purpose)` → `{ status, version,
  recordedAt, source, recordId }`; `ICommunicationPreferenceQuery.Get(customerId)` → `{ allowedChannels, language,
  quietHours }`; integration events `customers.consent-recorded.v1`, `consent-withdrawn.v1`,
  `preferences-changed.v1`. Media (#31) stores the consent record id it relied on; Notifications (#47) evaluates
  the query server-side before every send; `ICustomerSnapshotQuery.Get(customerId, callerPermissions)` →
  `{ customerNumber, displayName, nativeName, language, branchId, contact fields only with customers.read_contact }`
  used by #32a (order/estimate snapshot) and #42 (invoice customer snapshot). Contract tests in this PR.
  **The three read contracts are built; the three integration events wait on #77.** #21 built one shared
  `platform.outbox_messages` where [ADR-0008](adr/0008-transactional-outbox-and-workers.md) decided a table per
  module schema, so a module's write and its event are on two contexts and two transactions and cannot commit
  together. Every consumer named above pulls, so nothing here is blocked by the wait; publishing a withdrawal
  event that a crash can lose, to a module that deletes photographs on it, would be.
- **Field-level visibility**: DTOs projected through a `CustomerViewPolicy`: `customers.read` returns name,
  customer number, branch and status; `customers.read_contact` adds phones/email/address; `customers.read_notes`
  adds notes; consent history requires `customers.read_consent`. Tailor and Tailor Master receive no contact
  fields (job cards show customer name and job number only). Search results and timeline entries are filtered by
  the same policy and by branch (visibility branches ∩ caller's branch assignments).
- **Timeline**: `Platform.Abstractions.ITimelineSource { For(customerId, branchScope, permissions, cursor) }`
  registered per module; #26 ships the consent, note, correction/merge and (after #28) measurement sources and the
  BFF composition endpoint `GET /api/v1/customers/{id}/timeline` in the web host; #32a, #34, #42, #43, #48 and #49
  register their sources in their own PRs. Ordering by server event time with stable tie-break; entries carry the
  permission required to expand them.
- **Screens**: search-first "Find or create customer" (phone keypad on mobile), create/edit with concurrency
  handling, detail with timeline tabs (tablet master-detail).
- **Tests**: normalisation/validation/duplicate scoring unit tests; merge, correction, deactivation, export,
  concurrency, branch scope, field mask and timeline ordering integration tests; consent contract tests;
  phone/tablet UAT screenshots.

### #27 [E04-F02] Configurable measurement-template administration

- **Model**: `measurement_templates` (category/service link, name), `template_versions` (status draft/in_review/
  published/retired, effective dates, reason, actor), `template_fields` (key, label, group, display order,
  canonical unit millimetre stored as `numeric(8,2)`, allowed display units, precision 0–2 or `fraction_of_inch`
  1/8 or 1/16, required, min/max/warning ranges, conditional rules in a small JSON rule language, help text,
  `diagram_media_id` nullable reference resolved through `Media.Contracts.IMediaReference` with a bundled
  line-drawing fallback by `diagram_key` until #31, `diagram_alt` text required, Tamil label optional).
- **Units**: exact constants (1 in = 25.4 mm, 1 cm = 10 mm); display rounding half-up to field precision; the
  stored value is the converted entered value, never a re-rounded display value.
- **Lifecycle and permissions**: `draft → in_review → published → retired`; commands
  `POST /api/v1/customers/measurement-templates/{id}/versions/{v}/submit|approve|publish|retire|clone`; edit and
  submit require `catalog.templates.edit`; publish/retire require `catalog.templates.publish` (Owner/Admin by
  default, step-up per #25; the submitter cannot also publish when more than one administrator exists,
  configurable); published versions immutable (database trigger); clone creates a new draft; retirement blocked
  while active orders reference the version unless a successor exists; audit with reason; concurrent publish
  returns 409; registers an `ICatalogDependencyValidator` (#29) rejecting catalog publication that references a
  non-published template and rejecting its own retirement while a published catalog version references it.
- **Publish-time validation** (field-level problem details): unknown field keys in conditions, cyclic conditional
  dependencies, min > max or warning range outside min/max, precision not allowed for the display unit, required
  field hidden by an unconditional rule, duplicate keys, missing or retired category/service reference; preview
  and test-data validation before publish.
- **Seed**: initial templates for Blouse (Pattern, Aari work), Salwar, Lehenga, Gown, Kids from
  `docs/prd/measurement-templates.md` (#17) loaded by `init-reference-data`.
- **Tests**: property tests (`toDisplay(fromDisplay(x)) == x` at field precision in both units; conversion
  monotonic; converted `min ≤ warning_low ≤ warning_high ≤ max`); lifecycle/permission/historical-render
  integration tests; publish-time validation tests; business review of each initial template.

### #28 [E04-F03] Responsive measurement capture, validation, versioning, reuse

- **Flow**: entered from the customer, from intake or from the **Measurements needed** queue (#32b, customer and
  category pre-selected) → wizard groups (from the active published template) with progress, diagrams, inline
  warnings and a step-aware error summary → review with previous-version comparison (changed fields first with the
  delta, out-of-range values highlighted, "Show all fields" for the rest; on tablet the diagram stays beside the
  list) → confirm.
- **Entry ergonomics**: the unit is chosen once at wizard start (template default or user preference) and shown
  as a persistent adornment on every field; changing it mid-capture requires confirmation and converts nothing
  silently. Inch fields with `fraction_of_inch` precision use a whole-number keypad plus a segmented fraction
  control (`FractionInput`, 1/8 or 1/16 steps); cm/mm fields use `inputmode="decimal"` and accept `,` or `.`;
  fields carry `enterkeyhint="next"` and Next moves focus in the template's physical order, the last field of a
  group shows **Next group**; a ±step control per field (`NumericStepper`) allows one-handed correction; a sticky
  bottom bar holds **Back / Next / Save & exit** and the bottom navigation is hidden inside the wizard; error
  summary entries carry the step index and navigate to the field.
- **Explicit reuse**: when a confirmed version exists for the customer/category, the wizard offers "Reuse version N
  (taken <date> by <staff>)" or "Take new measurements"; reuse requires explicit confirmation, records
  `reused_from_version_id`, actor and reason, never defaults silently to the latest version; a retired template
  version cannot be reused without the migration prompt.
- **Drafts (server-side only in this issue)**: `measurement_drafts` (customer, template version, values JSON,
  current step, `expires_at` default 24 h, `xmin`); autosave on every step change and on blur through
  `PUT /api/v1/customers/measurements/drafts/{id}` with `If-Match`; unsaved state stays in memory with a warning
  before navigation; expired drafts are hard-deleted by the retention job and return 410 with a "start again from
  the last confirmed version" action. Encrypted local drafts and background sync are #51's scope.
- **Server rules**: template version pinned at draft creation; republished template mid-capture → migration
  prompt; required/range/conditional validation server-authoritative; correction = new version with reason.
- **Confirm**: `POST /measurements/drafts/{id}/confirm` with mandatory `Idempotency-Key`; one transaction:
  validation, insert `measurement_versions` (version number per customer × template), mark the draft consumed
  (unique partial index), emit `MeasurementVersionConfirmed`; a retry with the same key or a consumed draft
  returns the original version, never a second one.
- **Audit**: `measurement.draft_confirmed`, `measurement.reused`, `measurement.corrected`,
  `measurement.sheet_viewed`/`sheet_printed`, `measurement.exported` (values included in the #26 export only for
  `customers.export`).
- **Outputs**: printable/view-only measurement sheet (A4 through `IPdfRenderer`, Tamil-capable font, sent to the
  print queue) for authorised tailors (no unrelated customer data; access audited; Tailor access only through the
  resource-ownership requirement that #33 registers for assigned jobs).
- **Tests**: validation and snapshot invariants; idempotent confirm; concurrent confirmation (only one version);
  template change during capture; interrupted capture recovery on a phone profile (network loss mid-wizard → app
  reopened → server draft resumed → exactly one version on confirm); log/telemetry capture asserting no field
  key/value appears (redaction policy lists `measurements.*`); matrix tests for `measurements.*` endpoints;
  immutability trigger on `measurement_versions` (job-card criterion completed by #32a's snapshot regression).

### #29 [E05-F01] Configurable stitching-category and service catalog

- **Model** (`catalog` schema): `categories` (code, name, parent, display order, active dates, branch
  availability, feature flag), `service_types` (category, code, name, description, expected duration, nullable
  references `measurement_template_id` (#27), `design_option_group_ids` (#30), `workflow_definition_id` (#33),
  `qc_checklist_template_id` (#34), `price_list_item_code` (#41)), `catalog_versions` (draft/published/retired
  snapshot of the hierarchy used by orders).
- **Validation and contracts**: publish runs every registered `Catalog.Contracts.ICatalogDependencyValidator`;
  #29 ships the built-in validators (cycles, unique codes, orphaned parent, retired references, branch
  availability ⊆ parent's) and the registration point; #27, #30, #33, #34 and #41 add validators for their
  references. A service with a missing dependency may be published only with `allow_incomplete=true` and is
  flagged `not_orderable`. `Catalog.Contracts.ICatalogAvailabilityQuery.IsOrderable(serviceTypeId, branchId, at)`
  (published version, active dates, branch availability, feature flag on, not `not_orderable`) is used by #32a at
  confirmation and by the read API (`GET /catalog/current`) so inactive/unavailable services are neither listed
  nor accepted.
- **Rules**: retirement blocked with in-progress orders; version-keyed cache (D21) with invalidation on
  `CatalogVersionPublished`; permission-aware read API.
- **Seed**: Blouse → Pattern, Aari work; Salwar; Lehenga; Gown; Kids (from `docs/prd/category-hierarchy.md`).
- **Screens**: admin tree editor with preview and branch availability; demonstration "add a category without a
  deployment" recorded as evidence.
- **Tests**: hierarchy/lifecycle, validator registration, availability contract, cache invalidation, concurrent
  publish.

### #30 [E05-F02] Visual shape and design catalog with garment-level selections

- **Model**: `design_option_groups` (category links, name, selection mode single/multiple, required, display
  order, `branch_availability`, `active_from/to`), `design_options` (label, `illustration_media_id` with bundled
  line-drawing fallback until #31, `illustration_alt` text, help text, price impact, time impact, active), `design_rules`
  (requires/excludes/conditional-note between options and categories), versioned with the catalog version.
- **Split of responsibilities**: #30 delivers, in Catalog, the rule engine
  (`IDesignSelectionValidator.Validate(catalogVersionId, categoryId, selections) → violations`), the snapshot
  builder (`GarmentDesignSnapshot.From(catalogVersion, selections, notes, instructions)` — a value object
  serialised as JSON embedding labels, illustration references and option versions so it renders without a
  catalog lookup), the responsive picker (thumbnail grid, zoom, selection summary, validation) and the job-card
  component with printable fallback. Persistence of confirmed snapshots (`orders.garment_design_snapshots`: job,
  `revision_number`, snapshot JSON, reason, approved_by, immutable rows) and the post-confirmation command
  `POST /jobs/{id}/design-revisions` (`orders.revise_design`, reason, price/due-date delta via #41 shown before
  approval, blocked once the workflow marks design frozen) are wired in #32a, with the alteration path in #34;
  `orders.design-revised.v1` is consumed by #47 for notification.
- **Seed** (`docs/prd/design-options.md`, loaded by `init-reference-data`): Blouse — front neck, back neck, sleeve
  type/length, fit, closure (hooks/zip, front/back), lining, padding, blouse length, embellishment, pattern, Aari
  work (motif, density, placement; shown only when service = Aari work); Salwar — neck, sleeve, kameez length,
  slit, bottom style; Lehenga — skirt style, flare, waist, blouse options, dupatta; Gown — neckline, sleeve,
  silhouette, length, closure; Kids — simplified subsets with age-band notes; dependency rules (padding requires
  lining; Aari embellishment requires the Aari service type).
- **Tests**: rule-engine unit/property tests; integration tests persisting draft snapshots in
  `catalog.design_selection_drafts` (reused for intake autosave in #32b) to prove immutability after a catalog
  republish and 409 on concurrent revision; tablet usability review on the seed; "add an option without
  deployment" demo.

### #31 [E05-F03] Secure customer-material and reference-image lifecycle

- **Model** (`media` schema): `media_objects` (purpose MATERIAL/REFERENCE/DIAGRAM/ILLUSTRATION/QC_EVIDENCE/
  DELIVERY_EVIDENCE, owner branch, links customer/order/job, classification, consent record id, retention date,
  status uploading/quarantined/ready/rejected/deleted, checksum, size, mime, dimensions, object version id,
  `alt_text` — required for DIAGRAM/ILLUSTRATION, caption for customer photos), `media_derivatives`,
  `media_access_log`, `retention_holds`.
- **Pipeline** per Section 4.4: allowlist (JPEG, PNG, WebP, HEIC → converted), size cap, quarantine bucket,
  decoding in the worker under the `MediaProcessing` bulkhead with dimension/pixel limits, decoded signature
  validation, ClamAV scan, metadata strip and re-encode, thumbnails, ready bucket; SkiaSharp by default with HEIC
  handled by a pinned libheif build in the worker image only; failures quarantine the object and audit
  `media.rejected`; random object keys; no PII in filenames; bucket-level server-side encryption mandatory with a
  startup health check that fails if a media bucket is publicly readable or SSE is disabled; admin upload path for
  DIAGRAM/ILLUSTRATION (branch-independent, no consent record, indefinite retention) and a migration registering
  the bundled assets from #27/#30 as media objects.
- **Delivery**: `GET /api/v1/media/{id}/content?variant=thumb|preview|original` re-evaluates the media policy on
  every request (branch, customer/order/job link, purpose, consent record, `status = ready`, and for Tailor roles
  an assignment to the linked job), writes `media_access_log`, and **streams** the object with `Cache-Control:
  private, no-store`, `Content-Disposition: inline; filename="<opaque id>"`, `X-Content-Type-Options: nosniff`, an
  explicit image `Content-Type` from the stored derivative and `Content-Security-Policy: sandbox`. Streaming is
  the only delivery mode in v1; the storage endpoint is not reachable from outside the compose network or security
  group (D4).
- **Client**: capture through `<input type="file" accept="image/*" capture="environment">` (and `getUserMedia`
  where the picker is poor) in a burst loop (take → thumbnail → take next); images are downscaled client-side to
  ≤ 2048 px on the longest edge, EXIF orientation applied, re-encoded JPEG (target ≤ 600 KB) before upload (the
  original is uploaded only when `Media:KeepOriginal=true`); uploads run in the background with a per-garment
  progress list, per-image retry and cancel, and survive navigation within the draft; the intake **Confirm**
  button shows "Waiting for N photos" until uploads are ready; camera-denied and no-camera cases fall back to the
  gallery picker; caption (used as alt text); deletion request flow.
- **Operations**: retention job under a system principal with `media.retention` selecting
  `retention_date < now AND status = ready AND NOT EXISTS (retention_holds)`; each deletion idempotent (status
  deleted, derivatives removed, storage delete tolerant of 404) with one audit event per media id; held records
  skipped and counted; failures retry with backoff and surface as a health signal; legal/business hold; orphan
  cleanup; storage health metrics; backup treatment documented.
- **Tests**: adversarial corpus (polyglot, spoofed extension, oversized, EICAR, decompression bombs in PNG and
  HEIC, oversized dimensions, truncated HEIC, embedded ICC/XMP payloads stripped); a streamed response is not
  cacheable by the service worker or an HTTP cache; a copied media URL from another session returns 403; the
  storage endpoint is unreachable from the E2E browser context; endpoint returns 403 immediately after session
  revocation, role change, consent withdrawal or media deletion; EXIF removed; tailor cross-job access denied;
  retention rerun performs zero additional deletions; outage/retry/orphan cleanup; five photos on a throttled 4G
  profile reach "ready" in under 20 s.

### #32 [E06-F01] Estimates, multi-garment orders, immutable job cards (sub-issues #32a backend, #32b intake UI)

- **Model** (`orders` schema, #32a): `order_drafts` (server-side, shared within the branch, locked per garment
  section with `If-Match`, expiry default 72 h), `estimates` (priced, shareable snapshot of a draft:
  `E-<branch>-<FY>-000001`, `PricingResult` snapshot with configuration versions, validity date, status
  issued/superseded/converted, artefact checksum), `orders` (display number `O-<branch>-<FY>-000001`, customer
  snapshot from `ICustomerSnapshotQuery`, branch, status draft/confirmed/in_production/ready/delivered/closed/
  cancelled, due date, priority, notes, totals, confirmed_at, idempotency), `garment_jobs` (display number
  `J-<branch>-<FY>-000001-01`, category/service version, `measurement_snapshot` — a **copy** of the confirmed
  version's values, template version, taken-by and taken-at written at confirmation, with `measurement_version_id`
  as a nullable provenance reference rather than a restricting foreign key — design snapshot, media links, price
  snapshot, due date, priority, lifecycle confirmed → in_production → ready → delivered → closed plus
  cancelled/on_hold, workflow definition reference), `garment_job_dependencies` (job, prerequisite job, type
  `finish_before` | `deliver_together`, reason), `garment_design_snapshots`, `order_revisions`.
- **Branch reach for collections (from #24)**: `ScopedToResource` decides one identifier taken from the route and
  nothing else, so a list, a search, an export, a bulk command, or an identifier carried in a body or a query
  string reaches its handler with no branch decision taken. This issue publishes the first of those, so it builds
  the collection half — a port that applies the caller's permitted branch set as a predicate and refuses rather
  than returning everything when the scope cannot be satisfied, plus the inspector rule that a permissioned `GET`
  returning a collection declares it — and adds the matrix dimensions for it. Recorded meanwhile in
  `docs/security/permission-matrix.md` section 7 and in `tests/Tailor360.IntegrationTests/Authorization/matrix.yaml`
  so that nobody writes the first list endpoint believing the question is already answered. Two more #24 clauses
  land here for the same reason: sections 4 to 6 of `docs/security/role-walkthrough.md`, which need an endpoint
  that enforces a permission, and `docs/security/threat-models/authorisation.md`, which needs endpoints to be
  about.
- **Identifiers**: the UUIDv7 resource id is the only identifier in API paths, deep links, printed QR/URLs and
  customer links (74 random bits, always authorised against branch scope; enumeration caught by the #24 IDOR tests
  and #53 rate limits); display numbers are human-readable references searchable only by authenticated users
  with `orders.read` in the branch and are never lookup keys on customer-facing surfaces; job-card barcodes carry
  the opaque `G-…` payload (D9), never the display number; the intake UI shows the display number only after
  confirmation.
- **Confirmation** (`POST /orders/{draftId}/confirm`, mandatory `Idempotency-Key`): single transaction: validate
  catalog/template/workflow availability (`ICatalogAvailabilityQuery`) and required evidence (measurements,
  consents), snapshot inputs, allocate display numbers, create job cards with the workflow *definition* reference
  (instantiation happens at start-production, #33), run the in-transaction `GarmentJobCreated` handlers (#35
  allocates the barcode identity so labels print in the reception session), write outbox events
  (`OrderConfirmed`, `GarmentJobCreated`) consumed by Inventory (reservations when configured), Billing and
  Reporting.
- **Estimates**: `POST /orders/{draftId}/estimates` renders a PDF through `Platform.Abstractions.IPdfRenderer`,
  which #32a introduces with the QuestPDF adapter (licence check recorded in the PR), the Tamil-capable font and a
  `FakePdfRenderer` (Section 6.2 note 12) — "Estimate — not a tax invoice" — plus an in-app print view and a
  print-queue hand-off once #35 lands; share issues an expiring customer link limited to `purpose = estimate`
  through the Notifications skeleton of Section 6.2 note 9; never numbered in the invoice sequence, never posted;
  #42 consumes `EstimateIssued` only to prefill an invoice draft.
- **Confirmation participants**: `Platform.Abstractions.IConfirmationParticipant<TEvent>` per Section 6.2 note 10;
  with none registered, confirmation succeeds and the job card shows "label not yet available".
- **Read contract**: `Orders.Contracts.IOrderSnapshotQuery.Get(orderId)` → order status, jobs with category/service
  version, `PricingResult` snapshot and configuration versions, customer snapshot, revision number; consumed by #42
  (from-order conversion), #43 (dispatch amount attribution) and #45.
- **Revision window**: `POST /orders/{id}/revisions` (`orders.revise`, reason, `If-Match`) is allowed while every
  job is `confirmed` and none has entered production; re-runs validation and pricing, supersedes the estimate,
  records `order_revisions` and republishes `OrderConfirmed` with `revision_number`; after production starts,
  changes go through #34 alteration requests only.
- **Dependencies between jobs**: `deliver_together` is honoured by the ready gate (#34) and delivery queue (#48)
  unless partial delivery is approved per configuration; `finish_before` blocks the dependent job's first phase
  until the prerequisite phase completes (#33); snapshot with the order.
- **Timeline**: registers an `ITimelineSource` for orders/jobs.
- **#32b intake UI**: select/create customer → add garments (category, service, design via the #30 picker, media,
  notes, dependencies) → per garment **Measurements: take now / take later / reuse version N** → dates/priority →
  totals via `IPricingService` (#41) → validation summary → estimate print/share → confirm. Drafts are shared and
  resumable: every user in the branch with `orders.intake` sees them; a garment marked *take later* appears in a
  **Measurements needed** phone queue for Measurement Staff (`measurements.capture`), who open the #28 wizard from
  the queue on their own device; the draft shows a completeness badge per garment and confirmation returns a
  field error listing garments with neither a confirmed measurement version nor an explicit reuse. **Duplicate
  garment** copies category, service, design selections and media references (never measurements) into a new
  garment; garments collapse/expand on phone. The display number appears only after confirmation.
- **Tests**: confirmation rollback and duplicate request; snapshot regression after catalog/measurement change;
  identifier and enumeration tests; estimate PDF snapshot; revision-window tests; role/branch restrictions on
  search/view/edit/print/export; phone/tablet UAT for single and multi-garment intake.

### #33 [E06-F02] Configurable production workflow, assignment, Tailor workboard

- **Model**: `workflow_definitions` → `workflow_versions` (phases with code, name, required roles, evidence,
  duration/SLA, optional/skippable, transitions graph, category mapping), `job_phases` (instantiated per job with
  server timestamps), `assignments` (job, phase, assignee, team, reason; new row per reassignment, never updated),
  `assignee_capabilities` (user or team × category code × phase code, `valid_from/to`, optional daily capacity,
  granted by Tailor Master/Admin with reason).
- **Entering production**: `OrderConfirmed` (#32a) does not instantiate the workflow; it records the definition
  id. `POST /jobs/{id}/start-production` (`orders.start_production`, Tailor Master/Admin; also triggered by the
  first phase scan in #37 when the branch setting `Workflow:AutoStartOnFirstScan=true`) resolves the currently
  published version for the definition, pins `garment_jobs.workflow_version_id`, creates `job_phases` and emits
  `JobEnteredProduction`. The pinned version never changes; order revision (#32a) is refused afterwards.
- **Seeded default workflow** (published by `init-reference-data`): Intake → Cutting → Specialist work (Aari,
  conditional) → Stitching → Finishing → QC → Ready for delivery; exception states Rework, On hold.
- **Administration** (desktop-first; `catalog.workflows.edit` / `catalog.workflows.publish`, step-up):
  `GET/POST /api/v1/orders/workflow-definitions`, `.../versions`, `.../versions/{v}/validate|publish|retire|clone`;
  editor with phase list, transition matrix and graph preview highlighting unreachable phases, dead ends, missing
  start/end and phases with no eligible role; publish refused while validation reports errors; retire refused
  while a published catalog version maps a category to the definition unless a successor is published;
  registers an `ICatalogDependencyValidator`; audit with reason.
- **Eligibility and assignment**: assignment validation = same branch ∧ active user ∧ role permitted for the
  phase ∧ a capability row for the job's category and phase (or `Workflow:RequireCapabilityMatch=false`);
  workload view groups open phases per assignee against capacity; reassignment keeps earlier completions
  attributed to the previous assignee.
- **Transitions**: authorised start/pause/resume/complete with server timestamps, job-version concurrency,
  `finish_before` dependency enforcement, and events `JobPhaseChanged`, `JobAssigned/Reassigned/Unassigned`
  written to the outbox in the same transaction (each carrying job, order, branch, category/service version,
  workflow version, phase code, actor role, server time and previous state).
- **Due/aging and SLA evaluator** (worker, D12): due dates and SLA clocks use the branch timezone and the optional
  branch working calendar (#25) — non-working days are skipped and SLAs pause when configured; a scheduled job
  (every 15 minutes) raises `JobDueSoon`, `JobOverdue`, `PhaseSlaBreached` (and `HoldOverdue` from #34) exactly once
  per job × condition, cleared on completion; due-soon window and escalation delay are branch configuration;
  feeds queue filters, #45 projections and #47 routing.
- **Screens**: phone scanner-first work queue (uses `ManualEntrySource` until #36; the `ScannerSource` interface is
  defined here), tablet master-detail workboard, desktop planning board, "Team" screen for capabilities; filters
  assigned/unassigned/overdue/priority/blocked/due-soon; measurement-sheet access registered for assigned jobs.
- **Tests**: graph property tests, invalid transitions, concurrent scans cannot double-complete, eligibility
  rejections, reassignment history, evaluator boundary at midnight `Asia/Kolkata` with no duplicate raise, queue
  reconciliation and response-time targets, workboard device test.

### #34 [E06-F03] QC, rework, alteration, hold, cancellation, completion controls

- **QC checklist templates are catalog configuration** (D8): `catalog.qc_checklist_templates` →
  `qc_checklist_versions` (category/service link; criteria typed pass/fail | measurement tolerance | fit check;
  defect codes; evidence required; responsible role; draft/published/retired), administered through the #29
  catalog admin shell with an `ICatalogDependencyValidator`. Orders stores in `qc_results` the
  `qc_checklist_version_id` plus a JSON copy of the criteria evaluated so results render identically after
  template changes.
- **Model** (`orders`): `qc_results` (immutable, defects, evidence media, actor), `rework_tasks`,
  `alteration_requests` (before/after delivery, original job link, reason, changed measurement/design versions,
  price/due decisions, communication status, source staff/feedback), `holds`, `cancellations`; reason codes
  configurable.
- **Ready-for-delivery gate**: `ReadyForDeliveryGate` evaluates `WorkflowComplete`, `QcPassed` (latest result
  pass, no open rework), `DocumentationComplete` (required evidence present), `NoOpenHold`, `DependenciesMet`
  (`deliver_together`) and `CustodyReconciled` (through `Custody.Contracts.ICustodyStateQuery`; #34 registers a
  placeholder returning `Unknown`, treated as blocked when `Custody:GateEnabled` is on — default off until #37);
  each predicate returns a reason code; the result is materialised as `garment_jobs.ready_state` by the gate
  alone, recomputed on every workflow, QC, hold, dependency and custody event, emitting `JobReadyForDelivery`.
- **Policies**: cancellation blocked in prohibited financial/stock/custody states (compensating flows required);
  hold/resume/reschedule/reopen with reason and approval; alteration hand-off contract
  `Orders.Contracts.IAlterationRequests.Open(orderId, jobId, reason, source, caseId)` used by #49.
- **Events** (versioned): `qc-recorded`, `rework-opened/closed`, `alteration-requested/decided/completed`,
  `job-held/resumed/rescheduled`, `job-cancelled`, `order-cancelled`, `job-reopened`, `job-ready-for-delivery`;
  billing adjustment intents (cancellation credit, alteration charge) travel as event data for #42/#43; Orders
  never posts financial documents. Registers a timeline source for alterations and cancellations.
- **Dashboards**: failed QC, repeated rework, overdue holds, pending alterations, cancelled jobs — query-time
  views over the source tables, not projections.
- **Tests**: state-machine tests for every exceptional path; gate predicate tests including the placeholder and
  enabled behaviour; integration with inventory/billing/notification stubs; dashboard reconciliation (replay a
  scripted history and assert dashboard counts equal counts derived from the event stream); UAT for QC failure,
  alteration and cancellation.

### #35 [E07-F01] Barcode identifiers, labels, reprint control

- **Model** (`custody` schema): `barcode_identities` (namespace, payload, entity type/id, status active/
  superseded/invalidated, created, reason; partial unique index on `(entity_type, entity_id) WHERE status =
  'active'` **and** `UNIQUE (payload)` across all statuses — the allocator retries on conflict and a payload is
  never re-issued after supersession or invalidation), `label_prints` (identity, template version, printer/format,
  actor, time, reason, batch, verified flag).
- **Format**: D9 with the alphanumeric check character; Code 128 primary, QR optional carrying the same payload;
  the parser classifies symbologies before namespace checks (`G-|S-|I-|R-` payloads validate the check character;
  EAN-8/EAN-13/UPC-A digit strings validate the GS1 check digit and resolve only against `stock_items.supplier_ean`
  within branch scope; anything else is `barcode.malformed`); label templates (thermal 50×30 mm, 40×25 mm optional
  per Section 11 item 9; A4 sheet) with job number, category cue, due cue, branch code, print version, Tamil-capable
  font; quiet-zone and resolution validation; `docs/architecture/identifiers.md` (namespaces, length,
  entropy/collision budget, checksum algorithm with test vectors, lifecycle, label content policy).
- **Allocation**: `Custody.Contracts.IBarcodeIdentityAllocator` registers as the first confirmation participant
  (Section 6.2 note 10) so the identity exists when the reception session prints;
  the first scan (`intake_received`, custodian = reception/branch store) is recorded when the label is attached
  to the customer's material (linked to `customer_material_custody` from #38 onward).
- **Authorisation**: manual `POST /api/v1/custody/identities` (recovery only) requires `custody.generate_identity`
  (Admin/Owner); printing uses `custody.print_label` (batch capped by configuration, default 50; larger batches
  need `custody.bulk_print_label`); reprint and invalidation use `custody.reprint_label` / `custody.invalidate_label`
  with a mandatory reason; reprint supersedes the old identity and inserts the new one in one transaction under
  the job's row lock; every denied attempt is audited.
- **Resolve endpoints** (server side of #36's confirmation card): `GET /api/v1/custody/barcodes/{payload}`
  validates format and check character, rejects `S-`/`I-`/`R-` payloads with `barcode.wrong-namespace`, and
  resolves to `{ status: active | superseded | invalidated | unknown, jobId, jobNumber, currentPhase, custodian,
  permittedActions[] }` under `custody.scan` and branch scope; `GET /api/v1/custody/jobs/by-number/{jobNumber}`
  returns the same DTO under the same rules so a damaged label can be resolved by its printed job number;
  `superseded`/`invalidated` responses include the job number and a **Reprint label** action for callers holding
  `custody.reprint_label`; with `X-Scan-Source: manual` both endpoints require `custody.manual_lookup` and a reason
  and write an audit event; neither returns customer contact fields (contract test).
- **Print queue and print station (Lane A+B)**: `IPrintQueue`/`platform.print_jobs` and the **Print station**
  screen per Section 4.4 (desktop or tablet physically connected to the printer; `custody.print_label` /
  `billing.print_receipt`; lists its branch's queued jobs, prints through the browser dialog with the printer
  remembered — Chrome kiosk-printing documented for station devices — and marks each job printed/failed; after
  printing a label it shows a **Verify** step that scans the fresh label and records `label_verified` before it
  is attached). Every print action on phone layouts offers **Send to print station <name>** first and **Download
  PDF** as fallback. Paper formats owned by `IPdfRenderer` templates: label 50×30 mm, receipt 80 mm continuous,
  invoice/estimate/measurement sheet A4. `POST /api/v1/custody/labels/preview` renders without recording issuance
  under `custody.print_label`, the `export-heavy` rate-limit policy and the batch cap; label print screen (select
  jobs by order, queue or scan; preview template/size/orientation); reprint action on the job card with reason.
- **Tests**: collision/property and checksum tests; no-PII assertion (payload produced only from
  `namespace + random + check`; generator signature accepts no customer, order, phone or name input); two
  simultaneous reprints yield exactly one active identity; unauthorised generate/print/bulk/reprint/invalidate
  rejected and audited; batch limit; resolve and by-number endpoint contract tests; printed test sheets per
  template/printer under `docs/custody/label-tests/` (including at least one label queued from a phone and printed
  by a station) with scan results for Android, iPhone/iPad and USB/Bluetooth scanners and a durability note (fold,
  handling, humidity, adhesive).

### #36 [E07-F02] Camera, hardware-scanner and manual-entry scanning experience

- **Scan modes**: *single* (scan → confirmation card → one primary action) for sensitive transitions; *continuous*
  (viewfinder stays open, each decode is validated through the resolve endpoint and appended to a running list
  with per-item status; one **Commit N** action submits N idempotent #37 commands sequentially, each with its own
  client event UUID and `Idempotency-Key`; items can be removed before commit) for everything else. The sensitive
  list is branch configuration `Scanning:ConfirmBeforeCommit` (default dispatch, delivery confirmed, QC result,
  cross-branch transfer-out, correction); receive, phase start/complete, transfer accept, stocktake count and
  material issue default to continuous; in single mode non-sensitive actions show a 3-second **Undo** before the
  command is sent.
- **PWA**: implements the `ScannerSource` abstraction defined in #33 (Section 4.4); camera flow (permission
  handling, rear camera, torch and `zoom` constraint for 50×30 mm labels, scan region overlay, debounce window,
  release on navigation); **feedback is visual first** — full-viewfinder colour flash, large icon and result text
  in an `aria-live="assertive"` region (green accepted, amber needs confirmation, red rejected with reason and next
  action); tone and vibration are secondary and user-switchable (iOS has no vibration API); **one-handed layout**
  (viewfinder in the upper 60%; source switch, torch, zoom and the primary action in a bottom bar within thumb
  reach; every control ≥ 48×48 px; no swipe-only or long-press-only actions); the `KeyboardWedgeSource` listener
  is mounted in the app shell so a hardware scanner works from the queue or job card whenever no text input has
  focus; confirmation card (job number, current state, expected action) before committing sensitive transitions;
  error handling for unknown/inactive/superseded/malformed/wrong-namespace codes.
- **Manual fallback resolves by job, not payload**: `ManualEntrySource` offers, in order, *pick from my queue*
  (jobs the user may act on, filterable by job number, order number or customer name), *job number* input
  (`autocapitalize="characters" autocorrect="off" spellcheck="false"`, format mask) and *raw payload* last; all
  three require `custody.manual_lookup` and a reason and produce a scan event with `source = manual`.
- **Scanner check** (Settings → Device): decodes any barcode and shows raw/normalised payload, namespace, checksum
  validity, source and decode latency without resolving or committing; used for device setup, print-quality
  checks and support.
- **Server authority and fallbacks**: the PWA classifies the namespace for immediate feedback but every resolve and
  #37 command re-validates server-side; every scan submission carries `source` (camera | wedge | manual) and, for
  manual, the reason; when camera permission is denied or no camera exists the scan screen shows platform-specific
  re-enable instructions and switches to wedge or manual entry without leaving the flow.
- **Telemetry**: decode latency, failures, permission denials, fallback usage (no frames recorded), posted through
  the client telemetry module.
- **Tests**: parser/normalisation/debounce unit tests; Playwright with injected scan events; continuous-mode E2E
  with 10 injected scans including one duplicate and one wrong-namespace payload; camera permission denied
  completes the action via manual entry by job number; feedback visible with sound off on the WebKit profile;
  wrong-namespace payload rejected by the API contract test;
  manual lookup without `custody.manual_lookup` returns 403 and is audited; real-device matrix results;
  accessibility and one-handed review.

### #37 [E07-F03] Custody transfers, phase scans, idempotency, reconciliation

- **Model**: `scan_events` (immutable: event id, idempotency key, client event UUID, job, action, from/to
  custodian (user | team | location), `location_id`, actor, device, branch, client time, server time,
  correlation, `source`, `manual_reason`, note, exception reason, `corrects_event_id`, `reconciliation_case_id`),
  `custody_transfers` (pending → accepted/rejected/expired), `reconciliation_cases` (job, type mismatch |
  duplicate | stale | unknown_location | lost | disputed, opened_by, evidence media, status, resolution,
  resolved_by, approved_by). `scan_events` and `custody_transfers` are append-only (database trigger).
- **Rules**: explicit transfer-out and receive; phase/QC/ready/delivery-receipt/dispatch scans integrate with the
  workflow state machine (start-production trigger per #33); validation of expected custodian, location,
  workflow/QC prerequisites and role; stale/conflicting events rejected with problem details; duplicates (same
  idempotency key or client event UUID) return the original outcome; supplies the real
  `Custody.Contracts.ICustodyStateQuery` and enables the #34 gate predicate.
- **Dispatch-eligibility contract (introduced here, implemented in #43)**:
  `Billing.Contracts.IDispatchEligibilityQuery.GetDispatchEligibility(orderId, jobIds[])` →
  `{ Eligible, Reason: NotEvaluated | Unpaid | PartialBelowThreshold | ApprovedException | Paid, OrderBalance,
  AttributableAmount, ExceptionId, EvaluatedAt, PolicyVersion }` with a default implementation that fails closed.
  The dispatch scan rejects with `custody.dispatch-blocked` unless the query returns `Paid`,
  `PartialAboveThreshold` or `ApprovedException`; dispatch exceptions are the single Billing-owned mechanism of
  #43 and Custody never reads `dispatch_exceptions` directly. Failed-QC, held or wrong-custody jobs have no
  exception path.
- **Cross-branch transfers**: `custody_transfers` carries `from_branch_id` and `to_branch_id`; while a transfer is
  pending, users assigned to the destination branch holding `custody.receive` are granted a transfer-scoped
  authorisation (`TransferScopeRequirement`, Section 4.4) for exactly the receive, reject and resolve actions on
  the listed jobs; after acceptance the job's `current_custody_branch_id` becomes the destination and ordinary
  branch scope applies to custody actions while order/billing ownership stays with the originating branch; the
  grant ends when the transfer is accepted, rejected or expired (IDOR tests).
- **Reconciliation and correction**: a correction is a new `scan_events` row with `action = CORRECTION` linked to
  the corrected event and case, setting the resulting custodian/location; requires `custody.reconcile` and, above
  a configurable threshold (branch change, phase skip, dispatch reversal), approval by a different user
  (`custody.approve_reconciliation`); timeline shows original and correcting events side by side.
- **Client**: scan submissions carry the client event UUID and `Idempotency-Key` (deduplicated server-side per
  actor, Section 4.4); in-memory retry for the current screen with visible pending state and conflict surfacing;
  continuous-mode commits from #36 submit sequentially; no persistent offline queue here (#51).
- **Screens**: pending transfers, overdue acceptance (configurable SLA per transfer type), custody mismatch,
  duplicate/stale scans, unknown-location; each row opens or links a case; chronological scan/custody timeline
  and search; registers a timeline source.
- **Tests**: state/property tests for all transitions and exceptions; concurrency/idempotency/replay tests;
  dispatch scan rejected with the default implementation, accepted with a fake returning `Paid`, accepted with a
  fake returning `ApprovedException`; cross-branch transfer scope granted only while pending; recorded walkthrough
  of each exception type through to a compensating event;
  physical rehearsal from intake scan to ready-for-delivery and delivery-team receipt, with dispatch through the
  exception path (paid dispatch rehearsed in #43 and #48).

---

## 9. Issue blueprints: E08–E15

### #38 [E08-F01] Inventory items, units, suppliers, locations, reorder rules

- **Model** (`inventory` schema): `stock_items` (sku, barcode `S-…` or supplier EAN, name, category, base unit,
  purchase/issue units with conversion factors, tracking method none/lot, tax metadata (HSN, GST rate reference),
  cost method reference, status, branch/location availability), `units`, `unit_conversions`, `suppliers` (name,
  contacts, GSTIN/tax details, approval status draft | approved | suspended | retired, lead time, payment terms,
  commercial notes; suspended/retired suppliers remain referenced by history and cannot be selected on new
  purchase orders), `locations` (type warehouse | store | bin, branch, parent, active flag, transfer rules:
  allowed destinations and approval-required flag), `reorder_rules` (item × location: minimum, reorder point,
  target quantity, lead time, responsible role), `customer_material_custody` (order/job link, description,
  quantity, received/returned, never valued).
- **Permissions** (branch-scoped): `inventory.manage_items`, `inventory.manage_suppliers`,
  `inventory.manage_locations`, `inventory.manage_reorder_rules`; edits outside the user's branches are rejected
  and audited.
- **Rules**: unique SKU/barcode within scope, conversion graph acyclic and invertible, retire instead of delete,
  retired items visible in history but not purchasable/issuable.
- **Screens**: item admin with bulk CSV import preview and error report; supplier and location admin; reorder rule
  editor.
- **Tests**: unit-conversion property tests; import validation, concurrency, deactivation and permission
  integration tests; initial catalogue business review.

### #39 [E08-F02] Immutable stock ledger for purchasing, reservation, consumption

- **Model**: `ledger_entries` (immutable, append-only, trigger-protected: item, location, type opening/
  purchase_receipt/reservation/release/issue/consumption/return/transfer_out/transfer_in/wastage/adjustment,
  quantity in base unit signed, unit cost, references job/phase/purchase/stocktake, actor, reason, idempotency
  key, correlation, `corrects_entry_id` for compensating entries — an adjustment with reason `correction` and no
  link is rejected), `balances` (per item × location: on hand, reserved, available, in transit; updated in the
  same database transaction as the ledger insert, never via the outbox; a derived cache rebuilt and reconciled by
  a scheduled job that alerts on any difference; the row lock is the serialisation point for reservations),
  `purchase_orders`, `purchase_receipts` (supplier, lines, cost, tax reference, lot, receiving location,
  evidence), `reservations`.
- **Rules**: transfers post balanced pairs in one transaction; negative-stock policy configurable (block/allow with
  approval); reservation uses `SELECT … FOR UPDATE` on the balance; corrections by compensating entries only.
- **Endpoints** (all with `Idempotency-Key`): `POST /api/v1/inventory/purchase-orders`, `/purchase-receipts`,
  `/reservations`, `/movements` (type issue | consumption | return | wastage | transfer | adjustment; job/phase
  references required for job-linked types), `GET /api/v1/inventory/items/{id}/balances`, `.../ledger`.
- **Screens (Lane A+B)**: purchase order/receipt entry (supplier, lines with unit and conversion preview to base
  unit, cost, tax reference, lot where enabled, receiving location, evidence photo via Media); "Record material"
  panel on the job phase screen from #33 for issue/consumption/return/wastage against job + phase with reason and
  scanner-first item selection (`S-` barcode via the #36 scanner); item balance card with transaction timeline;
  location transfer; ledger browser with filters and CSV export.
- **Events**: `StockReserved/Released/Consumed`, `PurchaseReceived`; `IStockBalanceQuery` contract.
- **Tests**: balance rebuild equals materialised balances (property test); high-concurrency reservation; duplicate
  requests; purchase receipt or transfer failing mid-post leaves no ledger entries and no balance change;
  end-to-end purchase → reserve → consume → return → correct on device.

### #40 [E08-F03] Low-stock alerts, stocktake, variance approval, valuation reports

- **Alert policy** (configuration per branch, audited): evaluation basis (available vs on-hand), whether open
  purchase quantity counts, hysteresis margin for clearing, evaluation cadence, escalation delay, target roles.
  Worker evaluates on ledger events and on the configured cadence; alert state machine (raised → acknowledged/
  snoozed → escalated → cleared) deduplicated per item × location; routed through Notifications (#47); cleared
  automatically on replenishment.
- **Stocktake**: sessions per branch/location with freeze policy, count sheets and mobile scanner-first entry in
  the #36 continuous mode,
  recount, variance explanation, approval by a different user above a threshold, posting as ledger adjustments
  linked to session evidence.
- **Reports and valuation (Inventory module, served from its own ledger; no `reporting` schema)**: current on
  hand/available/reserved/in transit per item × location, low-stock and replenishment list, stocktake variance,
  and `Inventory.Contracts.IValuationQuery` (value per item × location at a cut-off using the configured method —
  weighted average default, FIFO optional — and immutable source costs); `valuation_runs` (method, configuration
  version, cut-off, totals, actor); screens are operational (Inventory role), synchronous, bounded to one branch,
  with `inventory.view_reports` / `inventory.view_valuation` (cost figures), a reconciliation-totals footer
  (ledger sum at the cut-off), freshness timestamp and CSV export of the current screen; treatment of returns/
  adjustments documented in `docs/inventory/valuation.md` for accountant sign-off. Cross-branch historical
  analytics belong to #46.
- **Metrics** (for #58 dashboards): alerts raised/acknowledged/snoozed/escalated/cleared, alert-to-delivery
  latency, false-positive rate (cleared without replenishment), stocktake variance rate, recurring-variance items.
- **Tests**: threshold boundary and dedup; stocktake concurrency/approval/adjustment; accountant-supplied golden
  fixtures under `tests/fixtures/inventory/valuation/*.json` (weighted average and FIFO; differing purchase
  costs, supplier returns, wastage, adjustments, transfers, rounding to paise); report reconciliation at a
  cut-off; large-dataset performance.

### #41 [E09-F01] Configurable pricing, discounts, GST calculation engine

- **Contract first**: `Billing.Contracts.IPricingService` and `PricingRequest`/`PricingResult` (Section 6.2 note 1);
  no reference to order entities (architecture test).
- **Model** (`billing` schema): `price_lists` → `price_list_versions` (effective dates, branch availability,
  items: service/product reference, base rate, inclusive/exclusive flag, allowed discount rules, surcharges for
  design/material/labour, approval thresholds), `gst_registrations` (branch, GSTIN, state code),
  `tax_configuration_versions` (tax codes, HSN/SAC, component rates CGST/SGST/IGST/cess, effective dates,
  place-of-supply rules), `calculation_snapshots`.
- **Engine**: pure, deterministic `Calculate(request) → PricingResult` with line components, document totals,
  rounding allocation and the configuration versions used; decimal arithmetic; rounding per D10; inclusive and
  exclusive pricing; intra- vs inter-state by place of supply; manual override requires
  `billing.override_price`, reason and audit and reports variance from catalogue.
- **Admin**: price/tax version editor with preview and test cases before publish; validation of missing tax
  configuration before order/invoice confirmation; registers an `ICatalogDependencyValidator` for price items.
- **Tests**: accountant-supplied golden master (`tests/fixtures/billing/`); property tests for rounding/allocation
  invariants; concurrent publish and historical reproduction; `Billing → Orders` reference rejected.

### #42 [E09-F02] GST invoices, numbering, PDFs, financial immutability

- **Model**: `invoices` (status draft/posted, branch, financial year, number, customer snapshot, GST registration,
  place of supply, totals, tax components, `source_order_id`, `source_estimate_id`, posted_at, artefact hash),
  `invoice_lines` (with `garment_job_id`), `invoice_tax_components`, `invoice_cancellations` (appended record:
  actor, reason, time, approval — the displayed status is derived), `credit_notes`, `debit_notes`,
  `document_sequences` (branch × document type × financial year), `document_artifacts` (PDF version, checksum,
  storage key under `documents/`).
- **Conversion from orders**: `POST /api/v1/billing/invoices/from-order/{orderId}` (`billing.create_invoice`,
  `Idempotency-Key`): eligibility = order confirmed and not cancelled, no open draft/posted invoice covering the
  same garment jobs (partial invoicing per job set only when policy permits), branch tax configuration present;
  reads the price and calculation snapshot through `Orders.Contracts.IOrderSnapshotQuery`, recalculates with the
  same price-list/tax versions, fails with `billing.snapshot-mismatch` if totals differ, stores customer snapshot,
  GST registration and place of supply on the draft.
- **Immutability**: posting allocates the next number under a row lock in the same transaction and freezes the
  row; a database trigger blocks every UPDATE/DELETE on posted rows with no exceptions; cancellation within the
  configured window is an appended `invoice_cancellations` record plus a credit note where value has been
  recognised; receipts and posted payments follow the same rule; deletion prohibited when referenced.
- **PDF**: accessible invoice (tagged PDF where the renderer supports it, Tamil-capable font, A4) with
  business/customer details, lines, discounts, GST components, totals, balance, terms, human-readable reference
  and an `I-…` barcode/QR; in-app print view and print-queue hand-off; download/print audited; email is a `NotificationIntent` (`invoice.issued`, `credit_note.issued`)
  enqueued behind the `notifications.email` flag (no-op until #47 merges), audited either way.
- **Lookup**: `GET /api/v1/billing/barcodes/{payload}` resolves under authorisation only.
- **Timeline**: registers an `ITimelineSource` for invoices and notes.
- **Tests**: concurrent posting (no duplicate or skipped numbers), duplicate-post idempotency, conversion
  idempotency and rejection of a second invoice for invoiced jobs, UPDATE on a posted invoice fails at the
  database even for the application role, a cancelled invoice still renders its original PDF (hash unchanged),
  `PdfTotalsMatchSnapshot` (text layer totals equal the persisted calculation snapshot over the #41 golden
  fixtures), PDF snapshot with a Tamil customer name and address, PDF accessibility check, barcode retrieval
  authorisation; accountant review of samples.

### #43 [E09-F03] Advances, payments, receipts, cashier reconciliation, dispatch gate

- **Model**: `payment_modes` (configuration: code, name, requires_reference, requires_provider,
  allowed_for_refund, active, branch availability; seeded cash / card / UPI / bank_transfer / other),
  `payments` (mode, amount, external reference, status, payer, branch, cashier, idempotency key, provider
  reference), `payment_allocations`, `advances` (unapplied amounts linked to customer/order),
  `refunds`/`reversals` (compensating, approval), `receipts` (numbered, `R-…` barcode),
  `cashier_sessions` (open/close, expected vs counted, variance reason/approval), `reconciliation_batches`
  (type cashier_session | provider_settlement, period, expected vs recorded totals by mode, variance, reason,
  approver), `dispatch_exceptions`. `payments`, `payment_allocations`, `refunds`, `receipts` are append-only
  (trigger); status changes only through new rows.
- **Card data**: PAN, CVV and track data are never stored or logged; only masked last four digits, network,
  provider transaction reference and authorisation code (validator rejects a 13–19 digit numeric value in any
  reference field; redaction test in logs).
- **Rules**: deterministic allocation (oldest invoice first) with authorised manual allocation; duplicate
  requests/callbacks idempotent by key and provider reference; balance formula per Section 4.5.
- **Dispatch eligibility semantics** (implements the #37 contract): evaluated from Billing's own records only —
  posted charges − allocations − credits + refunds across the order's posted invoices plus unapplied advances
  linked to the order; no posted invoice → `NotEvaluated` (blocked) unless `dispatch.allow_on_advance` is on and
  advances ≥ the configured share of the order price snapshot; rule `full` (default) requires balance = 0;
  `partial_threshold` requires paid share ≥ threshold; the result carries the policy version; Custody (#37) and
  Delivery (#48) never compute balances themselves.
- **Dispatch exception (single mechanism, owned by Billing)**: a dispatch that the payment rule blocks may proceed
  only with a `billing.dispatch_exceptions` record created under `billing.approve_dispatch_exception` (default
  grant Owner only; Admin only if Section 11 item 4 says so; `RequiresStepUp`), with a reason code and free text,
  bound to `(order_id, job_ids[], max_outstanding_amount, policy_version, expires_at ≤ 72 h)`; it is single-use
  (consumed by the dispatch authorisation that relies on it) and invalid if the outstanding balance at that time
  exceeds `max_outstanding_amount` or the job set differs; the approver must differ from the user performing the
  dispatch (enforced at scan time); `IDispatchEligibilityQuery` returns `Reason = ApprovedException` with the
  exception id; events `DispatchExceptionApproved/Consumed/Expired` on the billing outbox, surfaced in #44/#45 and
  the Owner dashboard.
- **Events**: `PaymentRecorded/Allocated/Reversed`, `RefundRecorded`, `AdvanceReceived/Applied`,
  `InvoicePaidStatusChanged`, `CashierSessionClosed`, `DispatchExceptionApproved` — written to the billing outbox
  in the same transaction, carrying configuration versions and persisted tax components so consumers never
  recalculate; `Billing.Contracts.IFinancialTotalsQuery` for reconciliation; timeline source for payments and
  receipts.
- **Screens**: take payment on phone/tablet (amount field with `inputmode="decimal"` and `en-IN` formatting,
  quick-fill **Balance** / **Advance**, cash tendered → change, payment mode as large segmented buttons, receipt
  share via the customer link as the primary phone action, receipt print to the queue), allocate advances, cashier
  open/close with a denomination count sheet (₹2000/500/200/100/50/20/10 and coins) computing the counted total
  against the expected total, outstanding balances, dispatch exception approval with step-up.
- **Tests**: allocation/balance property tests; concurrent payment, idempotency, reversal; append-only trigger;
  cashier close totals by mode equal the sum of payments and refunds in the session; receipt amounts equal
  allocations plus unapplied advance (property test); eligibility semantics for each rule; exception consumed
  exactly once under concurrent dispatch attempts; exception rejected when the balance grew after approval or the
  job set differs; approver = dispatcher rejected; unpaid/paid/partial dispatch integration tests against the real
  implementation; cashier close and dispatch UAT.

### #44 [E10-F01] Reconciled sales, GST, payment and receivables reporting

- **Foundations delivered by this issue (first Reporting-module issue)**: the `reporting` schema and
  `ReportingDbContext`; the projection runner (inbox-deduplicated event handlers, `projection_checkpoints` with
  last event position and `projected_at`, `rebuild-projection` CLI command that truncates and replays from the
  module event streams); the freshness and reconciliation framework (`reconciliation_runs`: report, cut-off,
  source total, projected total, delta, status, run time); scheduled reports (`report_schedules`: report, filters,
  recipients as users/roles, cadence, branch scope, owner; run by the worker under the impersonation principal of
  the owner rebuilt at run time; a per-recipient variant is generated and re-authorised for each recipient so a
  recipient receives only rows within their own branch scope, and recipients outside the schedule's branch scope
  are rejected at creation; delivered as an in-app notification with an authorised expiring download, never an
  email attachment; suspended when the owner loses the permission); the governed
  export service (`export_jobs`: requester, report, filters, format, classification, row count, status, storage
  key under `exports/`, `expires_at`; worker generates encrypted files; `GET /api/v1/reporting/exports/{id}/download`
  re-authorised per request and audited; expired files deleted by the retention job; small ranges may render
  synchronously behind a row limit); **query isolation** (dedicated connection pool with `statement_timeout`
  30 s interactive / 10 min asynchronous, `work_mem` cap, read-only transactions; large ranges run in the worker
  with bounded concurrency and a queue limit; read replica optional in #59). #45 and #46 build on these and must
  not re-create them.
- **Read models** fed by outbox events with rebuild: `sales_daily`, `invoice_register`, `gst_summary` (from
  persisted tax snapshots; configurable `gst_summary_layouts` — grouping keys tax rate, HSN/SAC, place of supply,
  registered/unregistered customer; included document types; period basis; default mirrors the accountant's
  monthly return working paper), `payments_register`, `receivables_aging`, `cashier_variance`,
  `cancellations_register` (orders/jobs/invoices cancelled: reason, actor, approval, financial effect),
  `discount_register` (line/document discounts, price overrides with reason and approver, variance from
  catalogue); metric dictionary `docs/reports/metrics.md` (definitions, cut-off and timezone rules, treatment of
  cancelled/voided/credited invoices, advances, partial payments).
- **Reconciliation job**: runs after each projection checkpoint and at the daily branch cut-off; obtains source
  totals only through `Billing.Contracts.IFinancialTotalsQuery` (architecture test: Reporting references no
  `Billing.Infrastructure` type and no `billing.*` table); stores runs; raises `ReportReconciliationMismatch` /
  `ReportFreshnessBreached` routed to Owner and Auditor through #47.
- **Screens**: sales dashboard (daily/monthly/custom, by branch/category/cashier/payment mode/customer segment
  where permitted), invoice register, GST summary, payments/advances/refunds, outstanding and aging with
  drill-through to authorised documents; **status strip** on every report screen ("data as of <branch time>",
  projection lag, last reconciliation run and delta, rebuilding state; warning and "unreconciled" label when
  thresholds are exceeded) served by `GET /api/v1/reporting/status?report=…`; exports print generated-at,
  data-as-of and reconciliation status and record them in the export audit entry.
- **Tests**: accountant-approved sample set under `tests/fixtures/reporting/` with expected totals signed off;
  publish a new price-list and tax-configuration version dated after the sample period, rebuild all projections
  and assert every historical total is identical; filter-inference test (a user scoped to branch A cannot obtain
  organisation-wide or branch-B totals through any filter, drill-through or export); scheduled report owned by a
  user without access to branch B yields no branch-B rows; timezone boundary; volume.

### #45 [E10-F02] Order pipeline, tailor workload, turnaround, quality analytics

- **Projections** from Orders and Custody contract events only (architecture test): pipeline by branch/category/
  phase, queue age, promised vs actual turnaround, overdue/blocked, assignment load, phase durations, QC pass/fail
  reasons, rework cycles, alteration rate, `customer_retention` (new vs repeat customers per period, repeat
  interval, category mix; feedback metrics are served by #49 from the Notifications module and are not projected
  here — any later join lands as a sequenced Reporting follow-up issue with its own migration); one migration
  named `45_<slug>`.
- **Metric dictionary**: one entry per metric in `docs/reports/metrics.md` — formula, source events, denominator,
  exclusions, and the approved treatment of held time (excluded from turnaround by default), rework time (counted
  in production time, reported separately as cycles) and cancelled jobs (excluded from turnaround, counted as
  pipeline exits); owner sign-off recorded.
- **Screens**: a phone **Today** summary as the Owner's default landing (sales today, payments by mode, due today
  / overdue counts, low-stock count, cash variance, projection freshness) with drill-through to the authorised
  registers; owner/manager pipeline dashboard (due today / due within N days (configurable) / overdue by branch
  and category; bottleneck trend = queue age and WIP per phase over time; active vs closed toggle), Tailor
  Master workload/capacity view (assigned/unassigned, filters by category eligibility from #33 capabilities and
  the optional tailor-skills attribute from #25), quality dashboard with context (no misleading staff ranking),
  drill-through to authorised job timelines; reuses the #44 status strip.
- **Operations**: projection lag/mismatch alerts; rebuild reproduces live results.
- **Tests**: replay/idempotency, reconciliation to source jobs for a cut-off/filter, performance with
  production-sized history, ops UAT scenarios (overdue, hold, QC fail, reassignment).

### #46 [E10-F03] Inventory, wastage and estimated-profitability analytics with governed exports

- **Reports** (Reporting module, analytical, cross-branch, historical, built from Inventory events and
  `IValuationQuery`; #40's operational screens are not duplicated): movement, purchase, consumption, return,
  wastage, stocktake variance, aging and reorder status at any cut-off with valuation method/version; trends by
  category/service/branch in day/week/month buckets (branch timezone); every figure drills through to the ledger
  entries, purchase receipts, stocktake adjustments or invoices behind it via `Inventory.Contracts` /
  `Billing.Contracts` queries that re-check the caller's branch and permission.
- **Profitability**: estimated job/garment profitability from invoiced/quoted revenue − allocated material cost −
  configured labour/overhead − discounts/credits − wastage; `reporting.costing_assumption_versions` (labour rate
  per category/service or phase, overhead percentage, allocation basis, effective dates, draft/published/retired)
  editable by Owner without code; each figure stores the assumption version and shows a "method and assumptions"
  panel with every unavailable component marked and the figure labelled "estimated — incomplete".
- **Exports**: field-level export policies so users only receive columns permitted to their role and branch;
  actor, filters, row count, classification, status and download audited; uses the #44 export service.
- **Isolation of reporting load**: uses the #44 pool/timeouts; exports and ranges above a configured size run only
  in the worker with bounded per-branch and global concurrency and a queue limit ("queued" rather than more
  work); long-running exports cancellable; synchronous endpoints reject requests whose estimated row count
  exceeds the limit and offer the asynchronous path.
- **Freshness and reconciliation**: reuses the status strip and `reconciliation_runs`; stock reconciliation compares
  projected on-hand and valuation per item × location at the cut-off with `IStockBalanceQuery`/`IValuationQuery`.
- **Tests**: ledger/valuation/profitability reconciliation; export authorisation, expiry and cleanup; k6
  mixed-load scenario — order confirmation, payment and scan traffic at the #19 target rate while N concurrent
  large exports run — asserting transactional p95 stays within budget and queueing behaves as configured.

### #47 [E11-F01] Notification templates, consent, delivery tracking, provider adapters

- **Model** (`notifications` schema): `notification_intents` (event, audience, priority, template version,
  locale, channel preference, consent requirement, quiet hours, fallback policy, dedup key), `templates` →
  `template_versions` (channel, declared variables allowlist, body, status draft → published (immutable) →
  retired), `deliveries` (queued/accepted/delivered/failed/bounced/suppressed/acknowledged, provider reference,
  attempts; rendered bodies stored only here, classified personal, retention per #19), `in_app_notifications`.
- **Templates**: `POST /api/v1/notifications/templates/{id}/versions/{v}/preview` renders with synthetic sample
  data; `test-send` delivers only to the caller's own verified address; rendering uses a logic-less engine in safe
  mode (auto-escaping, no expression evaluation, variables resolved only from the declared allowlist and a
  per-event variable schema); SMS/WhatsApp bodies are plain text; per-language template versions with the editor
  showing GSM-7 vs UCS-2 segment counts (Tamil SMS is 70 characters per segment); publishing requires
  `notifications.manage_templates` and a reason.
- **Service**: outbox-driven, idempotent, retry/backoff, rate limits, dedup, dead letter, operator replay; consent
  and preference checks server-side through the #26 contracts; transactional messages allowed per policy;
  configurable role/branch routing (event → audience, channel policy, template version) with rules registered for
  the events that exist at merge time — due-soon/overdue jobs (#33 evaluator), pending/overdue transfers (#37),
  payment events (#43); #40 (low stock), #48 (delivery) and #49 (feedback follow-up, service-recovery closure)
  register their own routing rules and templates in their PRs without changing the routing engine; in-app centre
  in the PWA (the fallback for push, which iOS delivers only to installed clients); operator screen for
  failed/dead-lettered deliveries with reason, single retry and bulk replay (`notifications.replay`, audited).
- **Adapters** (Integration module): `IEmailSender` (moved from #23), `ISmsSender`, `IWhatsAppSender`,
  `IPushSender` with fakes; provider payload mapping and credentials outside domain modules.
- **Audit and logging**: audit events for template create/publish/retire (body hash, actor, reason), each send
  decision (intent id, template version, channel, recipient reference — never the address — and outcome
  accepted/suppressed with reason: consent, quiet hours, dedup, rate limit, preference), provider status
  transitions and replays; Serilog redaction adds recipient address, body and provider credentials; telemetry
  emits counts and latencies by channel/provider/outcome only.
- **Tests**: adapter contract and retry/idempotency; consent, quiet hours, dedup, fallback; template safety
  (undeclared variable rejected, markup escaped); audit/redaction; provider-down drill.

### #48 [E11-F02] Payment-cleared delivery queue, customer status links, dispatch confirmation

- **Queue** (Custody module): jobs past the ready-for-delivery gate grouped by order with due/ready age, balance,
  exceptions; delivery-team receive scan establishes custody; server-side dispatch eligibility through the #37/#43
  contract (QC passed, custody, active order, holds resolved, payment rule); **partial-delivery policy**
  (`custody.dispatch_policy` configuration per branch): `whole_order` (every job ready and the payment rule holds
  for the whole order), `per_job` (ready jobs when the rule holds for the amount attributable to those jobs) or
  `exception` (Owner/Admin approval with reason, audited); `deliver_together` dependencies honoured; the queue
  shows the policy outcome and blocking reason per job.
- **Two-stage dispatch**: the delivery-team **receive scan** at the branch (online) evaluates
  `IDispatchEligibilityQuery`, consumes any dispatch exception and records a `dispatch_authorisation` (policy
  version, amount, expiry = branch end of day, approver) on the queue entry — this is where the payment rule
  blocks; the doorstep **delivery-confirmed scan** references that authorisation, captures the recipient name plus
  a 6-digit OTP sent through the customer's consented channel (valid 10 minutes, 5 attempts per delivery attempt,
  bound to order, jobs and dispatching user, hash stored on the scan event) or, per branch policy
  `custody.recipient_confirmation = otp_or_signature | signature | otp`, a signature stroke path (≤ 10 KB) and
  optional photo (uploaded when online) — never skipping confirmation silently — and is on the #51 offline
  allowlist; on replay the server re-validates the authorisation (not expired, order not cancelled, custody
  unchanged) and surfaces conflicts. `DeliveryConfirmed` (order, jobs, recipient confirmation type, receipt
  reference, evidence media ids, actor, branch, server time) is written to the custody outbox in the confirmation
  transaction and consumed by Billing (delivery receipt document via the #42 document service), Notifications
  (customer message and the #49 invitation), Orders (status → delivered) and Reporting; `DeliveryFailed` /
  `DeliveryReturned` create the compensating custody transfer back to the branch and reopen the queue entry;
  `HandoffDisputed` opens a reconciliation case; lost-link reissue flow. Delivery loading uses the #36 continuous
  scan mode; the delivery phone layout offers the high-contrast theme for sunlight, 48 px targets, a route-order
  list and per-stop status.
- **Customer status link**: `purpose = status` on the customer-link store of Section 4.4 (hashed token, `/c/status/
  {token}` outside the PWA shell, rendered in the customer's language with an English/Tamil switch, light bundle
  tested with the Lighthouse mobile profile), showing minimal progress and the configured amount display; no
  enumerable identifiers.
- **Timeline**: registers an `ITimelineSource` for delivery events.
- **Tests**: unpaid/paid/partial E2E under each policy; token enumeration/expiry/authorisation/rate-limit; the
  token appears nowhere in proxy/app logs or spans and a database dump contains no usable link; OTP attempt cap;
  offline delivery confirmation replayed with an expired authorisation surfaces a conflict; compensating custody
  on failed/returned delivery; delivery-team mobile UAT in installed mode; paid-dispatch physical rehearsal.

### #49 [E11-F03] Stitching feedback, alteration requests, service-recovery workflow

- **Flow**: `DeliveryConfirmed` → feedback invitation (consent/channel policy) with a `purpose = feedback` token on
  the shared customer-link store (one-time edit window bound to the token hash; page in the customer's language) →
  short accessible form (overall, fit, stitching quality, design match, timeliness, comments, contact/alteration
  request) → optional one-time edit window → policy evaluation.
- **Configuration**: `service_recovery_policy` per branch (rating threshold default ≤ 3 of 5, automatic case on
  explicit alteration request, owner role, due time in business hours, escalation ladder).
- **Cases**: `service_recovery_cases` (owner, due date, status, contact attempts, resolution code, revised job link,
  closure); creation idempotent on feedback id; closing emits `ServiceRecoveryClosed`, which #47 turns into a
  customer closure confirmation on the preferred consented channel (suppressed and audited when none exists);
  accepted alteration requests call `Orders.Contracts.IAlterationRequests.Open(...)` (#34) and store the returned
  job id; feedback rows are read-only after the edit window and never mutate measurements/design/job history.
- **Restrictions and reporting**: free text visibility limited by role/branch; rating, response rate, themes,
  alteration rate, resolution time; timeline source for feedback.
- **Tests**: token expiry/replay/rate limit; follow-up idempotency (exactly one case per low rating or alteration
  request), assignment, escalation, closure confirmation; journey UAT (positive, negative, alteration).

### #50 [E12-F01] Responsive design system and role-optimised layouts

- **Tokens**: colour (light/dark/high-contrast ready), type scale, spacing, elevation, motion (reduced-motion
  aware), focus rings, density, breakpoints/container queries.
- **Components**: navigation (bottom bar, side nav, tabs), forms with one `FieldProps` contract (label,
  description, required, error, `aria-describedby`, `inputmode`, unit adornment) and a shared step-aware
  `FormErrorSummary` (receives focus on failed submit, links to the first invalid field — on another wizard step if
  necessary — `aria-live`; server problem-details field errors map onto it), numeric measurement input with
  `FractionInput` and `NumericStepper` variants, selects, toggles, date, camera/scanner, tables and cards
  (responsive switch), filters, drawers/dialogs (bottom sheet on phone, dialog on desktop, focus trapped,
  background `inert`, focus restored), timeline, status badges (icon + text), alerts, empty/error/loading states,
  the network and permission states of Section 4.6 (`useNetworkState`, `NetworkStatusBanner`,
  `OfflineBlockedAction`, `RetryableError`, `Forbidden`), and `ConfirmDialog` in three tiers (confirm; confirm
  with reason for corrections, reprints, holds, cancellations, manual lookups; typed confirmation on desktop/tablet
  admin actions only — never on phone layouts, which use confirm-with-reason plus a second explicit tap) with a
  client-side 3-second undo for non-sensitive field actions and the wording "Cannot be undone — a supervisor
  correction is needed" where that is true. No inline scripts or styles; CSP nonce support; Storybook and
  Playwright run with the production CSP in report-only mode.
- **WCAG 2.2 specifics**: every drag interaction (assign, move phase, reorder) has a button/menu alternative that
  is the primary implementation and the one exercised by E2E (2.5.7); shells set `scroll-padding-bottom` to the
  bottom-bar height, use `interactive-widget=resizes-content` and hide the bottom navigation while the virtual
  keyboard is open so the focused field and primary action stay visible (2.4.11 — the overflow helper gains an
  obscured-focus assertion); a `RouteAnnouncer` moves focus to the page heading on navigation and scan results,
  sync state and autosave use `aria-live` regions (4.1.3); customer fields carry `autocomplete` and
  `inputmode="tel"` (1.3.5); a **Help** entry sits in the same shell position on every screen (3.2.6); the
  optional CAPTCHA adapter is enabled only with a non-cognitive alternative (3.3.8). Display preferences (theme
  system/light/dark/high-contrast for sunlight; text size 100/125/150%) are stored server-side in
  `identity.user_preferences` and applied at login so shared devices do not leak or lose settings.
- **Localisation foundation**: per Section 4.6 (`react-intl`, `en-IN`/`ta-IN`, fonts, pseudo-locale story required
  for every component, `formatters` module, numeric parsers accepting `,` and `.`).
- **Manifest and Install page**: `manifest.webmanifest` (name, maskable icons, theme/background colour,
  `start_url`, `display: standalone`, screenshots), iOS meta tags, an Install page with per-platform instructions
  (Android `beforeinstallprompt`; iOS Share → Add to Home Screen) and `GET /api/version` on the About screen;
  device evidence from #28 onward is recorded in installed mode on iOS and Android as well as in a browser tab
  ("installed mode" column in `docs/nfr/support-matrix.md`). No service worker yet (#51).
- **Layouts**: phone (bottom navigation, scanner-first floating action), tablet (master-detail for measurements,
  queues, job cards, inventory, billing), desktop (side navigation, dense tables/dashboards); safe areas, virtual
  keyboard handling, 200% zoom, variable text size, 44 px touch targets; no hover-only information.
- **Evidence at W1**: reference journeys per role in Storybook against synthetic data — Reception (find customer →
  intake), Measurement Staff (wizard), Tailor (scan → queue), Tailor Master (workboard), Inventory (stock
  entry), Cashier (payment), Delivery (queue → dispatch), Owner (dashboard) — used for role walkthroughs and the
  device/orientation/zoom matrix; a shared Playwright helper `expectNoHorizontalOverflow(page)` runs at
  320/360/768/1024/1280 px and 200% zoom and is required by DoD item 7 for every later UI PR.
- **Tests**: automated component accessibility and visual regression; form contract tests; pseudo-locale stories;
  device/orientation/keyboard/zoom matrix; the eight reference journeys walked with VoiceOver (iOS) and TalkBack
  (Android) against `docs/nfr/a11y-checklist.md` (DoD item 7 requires the checklist for each new journey; #52
  remains the full audit and adds NVDA); role walkthroughs.

### #51 [E12-F02] Installable PWA, safe updates, bounded network resilience

- **Service worker** (Workbox) on top of the #50 manifest and Install page: versioned precache, network-first API,
  stale-while-revalidate only for an explicit allowlist of non-sensitive reference endpoints (`/catalog/current`,
  template versions, workflow definitions, feature flags; cache keys include branch and version tag), everything
  else under `/api` network-only; installability checks; consumes the #50 network/permission states unchanged and
  adds only the persisted queue, pending count, conflict list and recovery actions.
- **Compatibility**: the PWA sends `X-Client-Version`; on the server's 426 (#53) or a newer `current` from
  `GET /api/version` it blocks mutations, shows the update prompt and reloads after the new service worker takes
  control (`skipWaiting` only on user consent or when no unsaved work exists); cache migration/cleanup; rollback =
  republishing the previous asset build with the same minimum; after an update a plain-language **What changed**
  sheet (Tamil/English, from release notes) is shown once per version.
- **Local drafts**: encrypted IndexedDB drafts (per-session key) bound to user/branch, expiring, cleared on
  logout/revocation, layered under the server-side drafts of #28 and #32b; quota exhaustion handling.
- **Offline queue** (the only implementation): persistent, bounded (configurable maximum entries and age, default
  200 events / 24 h), encrypted IndexedDB queue whose operations allowlist is a single typed constant containing
  the #37 scan endpoints and `custody.delivery-confirmed` (#48); entries are bound to `(user_id, branch_id)`, not
  to the session — after re-authentication by the same user they replay in order with their original client event
  UUID and `Idempotency-Key`, entries for a different user or branch are moved to a visible "cannot replay" list
  and never silently dropped; replay pauses on the first 409/422 with the server problem details shown; queued
  delivery confirmations show as "delivered — not yet synced", never as delivered; billing posting, payment
  reconciliation and prohibited transitions remain online-only; sync state, pending count, conflicts and recovery
  actions visible; background/foreground resume handling. Eviction is expected: `navigator.storage.persist()` is
  requested at install and login (result recorded in client telemetry); a `localStorage` mirror keeps only queue
  metadata (job numbers, action, count — no PII) so that a missing IndexedDB queue on start shows "N scans from
  <date> were not sent: <job numbers>" with a **Re-scan** action; a persistent notice blocks sign-out with queued
  items until the user acknowledges; queued items are shown inline on the scan screen as "not yet sent".
- **Documentation**: `docs/pwa/offline-and-resilience.md` (what works offline, what is blocked, queue limits,
  conflict handling, clearing local data) and `docs/support/pwa-troubleshooting.md` (install, stuck update,
  storage quota, camera permission).
- **Tests**: install/update/rollback on Android, iPhone/iPad, desktop; 426 handling; offline/reconnect/duplicate/
  conflict E2E including a doorstep delivery confirmation; queue survives re-login by the same user and is
  quarantined for a different user; cache/privacy inspection; quota exhaustion and simulated eviction.

### #52 [E12-F03] WCAG accessibility, cross-browser fallbacks, mobile performance budgets

- **Support contract** in `docs/nfr/support-matrix.md`: latest two stable Chrome, Edge, Firefox, Safari; approved
  iOS/iPadOS Safari, Android Chrome, Samsung Internet; graceful behaviour outside it.
- **Automation**: owns `tests/e2e/playwright.config.ts` (Chromium, Firefox, WebKit × phone/tablet/desktop ×
  portrait/landscape), the page-object layer, axe and Lighthouse integrations and one smoke scenario per critical
  path (customer search, measurement capture, design/media, order confirmation, barcode fallback, production/QC,
  inventory, billing, delivery, feedback) on the `seed-synthetic` data; Lighthouse CI budgets (JS/CSS/image sizes,
  LCP, INP, CLS) on a throttled mobile profile; #61b reuses the configuration and page objects.
- **Optimisations (measured first, then applied)**: route-level code splitting and lazy loading of the scanner,
  camera, PDF and dashboard bundles; `manualChunks` for vendor libraries; responsive image derivatives (#31) with
  `srcset`, lazy loading and explicit dimensions; list virtualisation for queues, ledgers and registers; cursor
  pagination defaults; font subsetting that keeps the Tamil subsets and `font-display`; before/after profiles in
  `docs/nfr/performance-report.md`; memory targets for the scan and capture screens on the lowest supported device.
- **Fallbacks**: capability detection for camera, barcode decoding, push, clipboard/share, printing, file capture,
  install prompts with documented alternatives.
- **Client telemetry**: browser module posting batched, sampled events to `POST /api/v1/telemetry/client`
  (same-origin, session-authenticated, rate-limited): unhandled errors and promise rejections (message, stack
  hash, route name — never URL query or form data), service-worker failures, capability-detection results,
  web vitals, browser/OS/device class; a redaction allowlist strips text content, identifiers and measurements;
  the server maps events to OpenTelemetry logs correlated with the session's request ids (#58 dashboard).
- **Manual**: screen reader (VoiceOver, TalkBack, NVDA), keyboard, zoom/reflow, real-device profiling.

### #53 [E13-F01] Versioned API, BFF, OpenAPI, idempotency standards

- **Standards document** `docs/api/conventions.md`: same-origin BFF boundary; resource/command conventions; URI
  versioning `/api/v1`; deprecation policy (`Deprecation`/`Sunset` headers, minimum notice period); HTTP
  semantics; problem details; cursor pagination, filtering, sorting, sparse fields; timezone and money formats;
  correlation IDs; request size/time limits; rate limits; cancellation; `docs/api/internal-endpoints.md`.
- **Implementation**: OpenAPI generation with examples and security schemes; Spectral lint (every operation
  declares security requirements, problem-details responses 400/401/403/404/409/422/429 and at least one example);
  **endpoint inventory** contract test (every mapped endpoint appears in the OpenAPI document or carries
  `[InternalEndpoint("reason")]`); `oasdiff` breaking-change gate that fails unless the change is under a new
  major path or the PR carries the `api-breaking-approved` label (applied only by CODEOWNERS of `docs/api/`) plus
  a linked deprecation entry; idempotency middleware for the command allowlist per Section 4.4; the rate-limit
  policy catalogue and `ForwardedHeaders` configuration of Section 4.4 (429 responses carry `Retry-After` and are
  audited for the `auth-anon`, `mfa-challenge` and `link-anon` policies); concurrency tokens (`ETag`/`If-Match`)
  across editable aggregates; the enforcing nonce-based CSP (`default-src 'self'; script-src 'nonce-…'
  'strict-dynamic'; object-src 'none'; base-uri 'none'; frame-ancestors 'none'; img-src 'self' data:; connect-src
  'self'; form-action 'self'`) once #50's components are merged; the architecture test that no endpoint accepts
  more than one authentication scheme; `GET /api/version` `{ api, minimumClient, current, environment, commit
  (Development only), schemaVersion }` and middleware returning 426 problem details when `X-Client-Version` is
  below the minimum; generated client wrapper injects `X-Correlation-Id`, `Idempotency-Key`, `X-Client-Version`
  and the anti-forgery header.
- **Tests**: OpenAPI lint/diff and inventory; idempotency/concurrency/timeout including key reuse with a
  different hash and in-flight duplicates; rate-limit policies and a spoofed `X-Forwarded-For` from a non-proxy
  source ignored; authorisation, error-leak; version-compatibility; enforcing CSP with zero violations across the
  #50 reference journeys; browser security run (extend the #23 Playwright suite: cookie attributes,
  anti-forgery required on every state-changing request, cross-origin POST/fetch rejected, CORS denies non-origin
  callers, session not replayable after logout-all, no token or session identifier in browser storage).

### #54 [E13-F02] Transactional integration events and signed webhook delivery

- **Mapping ownership**: each owning module maps its domain events to integration events in its own
  `Application` layer (e.g. `Orders.Application.IntegrationEventMappers`) and writes the envelope (id,
  type/version, occurred at, organisation/branch, aggregate ref, `sequence` monotonic per aggregate,
  correlation/causation, classification, minimal payload) to its own `outbox_messages` in the same transaction;
  the Integration relay handler (worker, inbox-deduplicated) copies envelopes into `integration.integration_events`
  for diagnostics and replay and fans out to deliveries. Payload contracts live in the owning module's `Contracts`
  as versioned records with JSON Schema and examples under `docs/integration/events/`; field allowlist per
  Section 5.2.
- **Model**: `webhook_subscriptions` (endpoint verified by challenge, allowlisted events, branch scope, approved
  classification, status active/paused, secret with dual-secret rotation window), `webhook_deliveries` (attempt,
  response class, next retry, state delivered/failed/dead-lettered).
- **Administration**: `GET/POST/PATCH /api/v1/integration/webhooks` under `integration.manage_webhooks`
  (Owner/Admin, step-up, reason, audited); `GET .../webhooks/{id}/deliveries` (redacted diagnostics: status,
  response class, headers minus secrets, payload digest); `POST /api/v1/integration/deliveries/{id}/replay` under
  `integration.replay_delivery` (audited, idempotent); desktop "Webhooks" screen with health (circuit state, last
  success, backlog) and delivery log.
- **Delivery**: a delivery is created only if the event's branch is within the subscription's scope and the
  classification ≤ the approved classification (skipped events recorded redacted); all outbound calls, including
  the endpoint verification challenge, go through the outbound HTTP policy of Section 4.4 (HTTPS only, private and
  metadata ranges rejected after DNS resolution, no automatic redirects); HMAC-SHA256 signature with timestamp
  header and documented replay window; per-attempt timeout 10 s; response body cap 64 KB; per-subscription concurrency 2 and
  circuit breaker; global worker bulkhead; response classes: 2xx delivered, 4xx except 408/429 failed-no-retry,
  408/429/5xx/timeout retried with exponential backoff and jitter then dead-lettered with an alert; per-aggregate
  ordering (a failing delivery blocks later events of the same aggregate only); fake receiver for tests.
- **Tests**: commit/rollback/duplicate/ordering (per-aggregate order under two concurrent dispatchers)/replay;
  signature, rotation, rate; outbound policy (redirect to an internal address, DNS answer flipping to an internal
  address between resolve and connect, IPv6 link-local, metadata IP — all rejected); policy filter;
  slow/unavailable receivers.

### #55 [E13-F03] Replaceable payment, messaging, accounting and printing adapters

- **Port introduction order**: `IPdfRenderer` (#32a), `IBarcodeRenderer` and `IPrintQueue` (#35), messaging senders
  (#23, #47) and `IDispatchEligibilityQuery` (#37/#43) already exist; #55 adds `IPaymentGateway` (initiate, status,
  refund, callback verification), `IAccountingExporter` (versioned mapping, batch identity, balanced totals,
  correction and re-export policy; Tally XML first) and `IPrintBridge` (an adapter that drains
  `platform.print_jobs` to a network/local bridge; the queue and print station belong to #35), plus the adapter
  contract-test suites that every earlier adapter must also pass before flag enablement; existing ports change
  only by non-breaking extension.
- **No half-posting rule**: provider calls never run inside a database transaction; record intent
  (`payment_intents`, `accounting_export_batches` in `generated`, `print_jobs` in `queued`) plus outbox message in
  one transaction → worker/adapter call using the intent id as the provider idempotency key → idempotent outcome
  handler (`succeeded` / `failed` / `unknown`); `unknown` resolved by status polling, never assumed; financial
  state changes only on a verified provider outcome; workflow/custody state never changes as a side effect of an
  adapter call.
- **Callback endpoint**: `POST /api/v1/integration/payments/{provider}/callback` is `[AllowAnonymous("provider
  callback")]`, mapped outside the cookie scheme (no cookie or anti-forgery processing), per-provider source-IP
  allowlist where the provider publishes one, `callback` rate-limit policy, raw-body HMAC verification with a
  dual-secret rotation window, timestamp replay window ≤ 5 min, provider event id as idempotency key. The callback
  **never posts a payment**: it records `payment_callbacks` (redacted body digest, verification result) and
  enqueues `VerifyPaymentIntent(intent_id)`; the worker calls the provider status API through the outbound HTTP
  policy, and only a status response matching amount, currency, reference and intent posts the payment through
  #43; mismatches are parked with an alert and acknowledged with 2xx; all outcomes audited.
- **Reconciliation worker**: scheduled (daily) and on-demand job pulls provider settlement/status and compares to
  `payments`, producing missing/duplicate/mismatched items (alerted via #58; resolved through #43 compensating
  entries and `reconciliation_batches`); accounting batches `generated → exported → acknowledged | superseded`
  with reversing lines on re-export.
- **Provider health and ownership**: each enabled adapter registers a health probe reported as `degraded` (never
  `unhealthy`) in `/health/ready` and on a dashboard; `docs/integrations/<provider>.md` (support owner, escalation,
  rate limits, error mapping, sandbox procedure, reconciliation steps) must exist before the provider's flag can be
  enabled; sandbox/fake mode cannot be enabled in production (startup guard).
- **Tests**: contract suites; fault injection (timeout after provider debit, duplicate callback, callback before
  status poll, adapter crash between steps, provider down during cashier close — exactly one consistent payment
  record, no orphaned receipt); callback security (forged callback with a valid signature but no matching provider
  status does not post; callback before status poll converges to one payment; secret rotation window);
  reconciliation report; accounting balanced totals and re-export; architecture test (only `Integration.Infrastructure` and tests reference provider SDKs; negative test
  adding a vendor package to `Billing.Application` fails) and a "swap provider by configuration only" demo.

### #56 [E14-F01] Threat modelling and ASVS-aligned security baseline (sub-issues #56a, #56b)

- **#56a (W2, docs)**: `docs/security/threat-models/*.md` (DFDs and STRIDE per flow: auth (from #23),
  customer/measurement/media, order/workflow, barcode custody, inventory, billing/payment, reports/exports,
  customer links, integrations, deployment) drafted from the architecture docs and this plan, each ending with a
  residual-risk table with owner and acceptance date; `docs/security/abuse-cases.md` (IDOR, privilege escalation,
  workflow bypass, barcode replay, invoice/payment tampering, stock manipulation, malicious uploads, export
  leakage, SSRF, credential abuse, denial of service); `docs/security/asvs-traceability.md` (ASVS L2 + selected
  L3; columns requirement → control → issue/PR → test → evidence → residual risk → owner → review date);
  `docs/security/vulnerability-management.md` (triage, SLAs critical 7 / high 30 / medium 90 days, disclosure);
  `docs/security/exceptions.md` (finding, approver, expiry, compensating control) with a CI check failing on
  expired exceptions; CI gates extended with a licence allowlist and severity SLAs. Each later feature PR
  references its threat model and closes the mapped controls (DoD item 6).
- **#56b (W5)**: CSP hardening review of the policy enforced since #53 (adds `require-trusted-types-for 'script'`
  where supported and removes any remaining report-only entries), central validation/encoding review, business-state authorisation checks (no client-driven state), automated security
  regression suite (IDOR, escalation, workflow bypass via direct API, barcode replay, invoice/payment tampering,
  stock manipulation, malicious upload, export leakage, SSRF, credential abuse, DoS limits, no-PII-in-barcodes
  over all rendered barcodes in the E2E fixtures), ASVS traceability audit, independent penetration test before
  production and after material auth/payment/upload changes, verified remediation.

### #57 [E14-F02] Privacy lifecycle, immutable audit, encryption, secrets management

- **Data inventory** `docs/privacy/data-inventory.md` mapping each class (identity, contact, measurements, images,
  design, order, barcode, stock, financial, feedback, logs, audit, exports, backups, credentials/secrets) to owner,
  access policy, retention, backup behaviour and key ownership.
- **Removable vs retained**: `platform.retention_policies` (configuration: data class, branch scope, retention
  period, effective-from, legal basis, actor, reason). Approved deletion of a customer **pseudonymises** the
  customer master (name → `Deleted customer <random 8-character id>` generated with `RandomNumberGenerator`, never
  derived from any customer attribute; phone/email/address cleared; aliases and duplicate candidates removed;
  `customer_number` retained as the only stable reference), hard-deletes measurement versions and drafts, media
  objects/derivatives, feedback free text and customer links past retention, **clears** `garment_jobs.
  measurement_snapshot`, design notes and media links on jobs whose statutory retention (delivery + configured
  period) has elapsed, records the cleared classes on the request, and **never touches** posted invoices, notes,
  payments, receipts, ledger entries, scan events or audit events, which keep their point-in-time customer
  snapshot for the statutory period (GST records: confirm the period with the accountant) and are excluded from
  deletion by policy.
- **Workflows**: `data_subject_requests` (type correction/export/restriction/deletion, subject, requester, reason,
  status, approver, executed classes, skipped classes with reason: hold, statutory retention);
  `POST /api/v1/customers/{id}/data-export` (async, encrypted, expiring, audited; extends the #26 export),
  `POST /customers/{id}/restrict` (blocks non-essential processing and outbound communication),
  `POST /customers/{id}/deletion-requests` → Owner/Admin approval with step-up → worker executes after hold check;
  retention job (D12) runs per policy and writes an exception report (items skipped by hold or reference)
  surfaced in the admin audit view; backup treatment: deleted data persists until backup expiry (#60) and a
  restore replays requests executed after the restore point before the environment is opened.
- **Audit integrity** (chain from #21): hourly verification job re-computing the chain since the last anchor; the
  chain head `(seq, row_hash, verified_at)` anchored every hour to the object-locked backup bucket
  (`audit-anchors/`) and emitted as an OpenTelemetry log record so an alert fires when the in-database chain and
  the last anchor disagree; gap detection on `seq` continuity; restricted viewer/export (`audit.read`,
  `audit.export`; Auditor/Owner; searchable by correlation id, actor, subject, resource, time); retention as
  partition detach under the retention role, independent of application logs; sensitive-read audit review
  (measurement sheet, media, exports); a completeness review generated from OpenAPI (every state-changing
  operation has a test asserting its audit event and correlation id).
- **Encryption and secrets**: TLS everywhere, encrypted volumes/object storage (SSE from #31). **Secret classes**:
  (a) verify-only secrets — passwords, recovery codes, API keys, customer-link and invitation tokens — stored as
  salted hashes (Argon2id for passwords/recovery codes, SHA-256 for high-entropy tokens); (b) reversible secrets —
  TOTP seeds, webhook signing secrets, provider credentials, SMTP passwords — encrypted with Data Protection
  purposes under the KMS/certificate-protected ring of #21; (c) infrastructure secrets — connection strings,
  storage keys, backup keys — only from the environment/secret manager as files. Key ownership and rotation per
  class in `docs/privacy/data-inventory.md`; rotation runbook and emergency revocation; verification tests for
  logout/revocation, export expiry, media deletion, link revocation, secret/key rotation.

### #58 [E14-F03] Observability, SLO alerting, performance resilience, incident response

- **Instrumentation**: OpenTelemetry across web, worker, database, outbox, media, scans, inventory, billing,
  reports, notifications, integrations; correlation/causation propagation; redaction processors; backend per D13
  (collector-only on the VM shipping to the chosen backend; self-hosted profile otherwise) with dashboards and
  alert rules versioned as code (deployed by #59); the client-telemetry endpoint and event → log mapping belong to
  #52 (same parallel group, in the web host under `Telemetry/`, not in `Platform.Observability`) — #58 adds the
  client-telemetry dashboard panel and alert rules only after #52 merges, and if #58 merges first #52 adds the
  panel from #58's dashboard conventions; the "one correlation trail" criterion is met by correlation ids in
  structured logs plus sampled traces.
- **Signals and alerts**: latency/errors/saturation, failed jobs, outbox lag, scan conflicts, stock reconciliation
  mismatch, numbering/payment mismatch, notification failures, WAL freshness from `pg_stat_archiver` ("no WAL
  archived for 15 min" = RPO) and backup-scheduler metrics ("no successful base backup for 26 h"), report
  freshness, projection lag, inventory alert metrics (#40), disk usage at 80%; dashboards per role; multi-window
  burn-rate alerts per SLO (fast 1 h/5 m, slow 6 h/30 m) so alerts fire before budget exhaustion; every alert
  carries a `runbook_url` into `docs/runbooks/`; grouping/dedup/severity via Alertmanager or the hosted
  equivalent; receivers built from Section 11 item 15; the external dead-man's switch receives pings from the
  backup job, the worker heartbeat and the alerting watchdog, and an external HTTP check hits `/health/live`.
- **Resilience**: timeouts, bounded retries with backoff, circuit breakers, bulkheads, queue limits, cancellation,
  safe degradation (scanning continues if notifications are down); **retry-safety test**: fault injection retries
  every protected command (confirm, scan, invoice post, payment, webhook delivery, payment callback) under
  timeouts and worker restarts and asserts exactly one business effect; load/stress/soak tests (k6) for intake,
  scan bursts, concurrent reservation/invoice/payment, uploads, large reports, and the mixed-load scenario from
  #46.
- **Incident process**: severity, ownership, escalation, communication, evidence preservation, recovery,
  post-incident review; game days (database slowdown, object storage outage, provider outage, stuck outbox,
  duplicate callback, failed deployment) with alert/runbook effectiveness scored.

### #59 [E15-F01] Hardened environments, CI/CD, versioning, safe database releases

- **Images**: minimal, non-root, pinned, scanned, read-only filesystem, dropped capabilities; SBOM, provenance
  attestation and cosign signature; build once, promote the same digest.
- **Environments**: dev/test/staging/production as code (Terraform for cloud, Ansible for on-prem) with network
  boundaries, TLS, DNS, secrets, storage, database, observability, optional read replica for reporting; staging
  verification and manual approval gates; separation of deployment duties; protected production credentials;
  the interim staging from #22 is destroyed.
- **Branch and release protection**: `main` and `release/*` require a PR linked to one issue, CODEOWNERS review,
  all CI checks, no direct pushes, linear history; a production deployment requires a release tag whose GitHub
  Release carries the evidence checklist (regression report, security scan summary, UAT sign-off link, migration
  dry-run) — the deploy workflow fails if it is missing.
- **Deploy-time verification**: the deploy job verifies the image signature and provenance against the digest in
  the approved release before pulling; `/api/version` is compared to the release record during post-deploy
  verification.
- **Releases**: semantic versioning, build metadata, release notes stating the expected interruption window,
  compatibility matrix (PWA/API/DB/worker; patched PostgreSQL/MinIO/ClamAV/proxy versions; component lifecycles —
  .NET 10 LTS and PostgreSQL 16 support end Nov 2028, so the .NET 12 and PostgreSQL major moves are scheduled for
  2028); expand-migrate-contract per the #21 migration execution model with preflight checks, migration locks,
  the latest backup label and `pg_current_wal_lsn()` recorded before `migrate` as the PITR target (not a new full
  backup per deploy), monitored post-deploy verification; pull-based deployment and rollback rules per Section 4.7
  (`minimumClient` two-release rule; API ignores unknown request fields; PWA treats 404 on a new endpoint as "not
  available in this version"); rolling or blue-green only with Kubernetes.
- **Patch management**: weekly scheduled rebuild of base images and lock files, Trivy re-scan of deployed digests
  with alerts, patching SLAs from #19 enforced as a release gate, expedited path for emergency patches.
- **Evidence**: provenance chain for a tagged artefact; clean-environment provisioning and deployment test (IaC
  from empty → healthy stack in CI against an ephemeral environment); rollback rehearsal (deploy N+1 with its
  expand migration, roll back to N while the database stays at N+1, verify health and a smoke journey with PWA
  N+1 still cached in a browser, roll forward); failed-migration and failed-health-check rehearsals; permissions
  and secret-access review.

### #60 [E15-F02] Encrypted backups, PITR, disaster recovery, runbooks

- **Backup architecture by hosting model** (decided with Section 11 item 2): self-hosted PostgreSQL runs
  pgBackRest inside the `tailor360-postgres` image (`postgres:16` + `pgbackrest`, `archive_mode=on`,
  `archive_command='pgbackrest … archive-push %p'`, encrypted S3 repository; the D17 "backup sidecar" is only the
  scheduler running nightly incremental and weekly full backups and publishing metrics); managed PostgreSQL uses
  provider PITR (35 days) plus a nightly `pg_dump --format=directory` to the locked bucket so a copy exists
  outside the provider account. Backup bucket: versioning + object lock (35 days) with **lifecycle expiry**
  (never `pgbackrest expire`) and a 12-month-locked prefix for monthly copies; the archiver identity has put/get/
  list but no delete, application roles have no access; the repository cipher passphrase and the Data Protection
  key-encryption key are held in the owner's password manager/KMS and never only on the VM. Media: versioning with
  non-current-version expiry ≥ 35 days, `media_objects` storing the object version id, and a mandatory off-site
  copy (`rclone`/`mc mirror`) to a second bucket. Scope: database, media, audit anchors, configuration files, Caddy
  certificate storage, MinIO configuration, DNS/registrar and ACME credentials (password manager), dashboards as
  code (git); encryption keys are escrowed separately and never stored in the same backup set as the data they
  protect. Monitoring of age/completeness/failures per #58 with a recorded alert test; monthly backups exclude
  media derivatives and expired exports.
- **Retention across backups**: bounded retention per D18 so deleted data ages out; `docs/runbooks/restore.md`
  requires replaying `data_subject_requests` executed after the restore point before the restored environment is
  opened (#57); retention review in the quarterly DR record.
- **Restore automation**: the weekly automated restore runs on the staging VM or a throwaway VM created by the IaC
  job — never on the production host and never on GitHub-hosted runners — with a read-only backup credential that
  lives only in that environment; it restores to `now() − 1 h`, runs `migrate --check`, the invariant queries
  (ledger balances, invoice sequence gaps, media references verified by `HEAD` against the media bucket) and
  records measured RPO/RTO; PITR to a chosen timestamp; missing-object recovery from bucket versioning.
- **Runbooks** `docs/runbooks/`: accidental deletion, bad migration, corruption, lost object, site failure, secret
  compromise, ransomware-like event (restore on a machine that never held the key file), PostgreSQL major upgrade
  (maintenance-window logical upgrade into the new-version container, rehearsed in the restore environment, new
  pgBackRest stanza and full backup before traffic returns; CloudNativePG preferred if Kubernetes is adopted);
  owners, communications, decision points, DNS/certificate/provider dependencies, return-to-primary.
- **Cadence and evidence**: automated restore weekly; PITR exercise monthly; missing-object recovery monthly; full
  DR exercise quarterly executed by an operator who did not author the runbook; every exercise records actual
  RPO/RTO against #19 and opens a `release-blocker` corrective issue when a target is missed; backup access and
  immutability review.

### #61 [E15-F03] Automated QA, UAT, training, pilot rollout, go-live gates (sub-issues #61a, #61b, #61c)

- **#61a — strategy and fixtures**: `docs/qa/test-strategy.md` (risk-based: unit, property, architecture, database,
  integration, contract, browser E2E, accessibility, security, performance, resilience, migration, backup and
  DR); consolidates the fixture library that started in #21 (`seed-synthetic`) and was extended by every module
  issue into `tests/fixtures/` with a dataset catalogue (branches, roles, categories, customers, measurements,
  images, orders, barcodes, stock, invoices, payments, delivery, feedback), versioning rules and a
  production-refusal test; legacy import validation flow (preview, rejection, reconciliation, idempotent rerun,
  rollback) or a documented "no source data" decision. Starts when #52 opens; does not gate #52.
- **#61b — regression suite**: reuses the #52 Playwright configuration and page objects; adds business-scenario
  regression per garment category, multi-garment order, barcode handoffs, QC rework, stock consumption, GST
  billing, partial/full payment, blocked/allowed dispatch under each policy, feedback/alteration; full regression
  with stored artefacts; production-like dress rehearsal including restore and rollback.
- **#61c — UAT, training, pilot, go-live**: role-based UAT scripts and sign-off for Owner, Reception, Measurement
  Staff, Tailor Master, Tailor, Inventory, Cashier, Delivery, Auditor; accountant approval of billing examples;
  training material and quick guides (fed by the #17 "what changes for staff" tables); support contacts and
  runbooks; controlled production-access onboarding; pilot plan with explicit entry/exit criteria (≥ N orders
  across every seeded category, zero P1 defects open for five consecutive days, daily reconciliation of stock
  ledger, invoice sequences and cash drawer with no unexplained variance), defect triage cadence and severity
  rules, real devices/printers/scanners; go-live record naming owners (business, technical, security,
  operations), dashboards/alerts in force, rollback decision owner and criteria, support rota and escalation,
  hypercare exit criteria (duration, SLO attainment, open-defect thresholds); a per-role first-run checklist in
  the app shell (install, camera permission, scanner check, print-station pairing, one test scan against the
  printed training label sheet) tracked in `identity.user_onboarding`; one-page quick cards per role in Tamil and
  English generated from the #17 "what changes for staff" tables; the IaC staging environment retained after
  go-live as the training environment; `docs/launch/post-launch-review.md`
  (weekly for four weeks, then monthly; KPIs: incidents, adoption by role, turnaround, rework rate, stock variance,
  financial reconciliation); `docs/launch/release-evidence-index.md` mapping every #1 release criterion and every
  P0/P1 journey to its evidence link.

---

## 10. Risks and mitigations

| Risk | Impact | Mitigation |
| --- | --- | --- |
| Claude Code environment lacks .NET SDK and blocked download egress | Backend issues cannot be built or tested in cloud sessions | Provision an environment image with .NET 10 SDK, Node 22, pnpm, Docker (or allow egress to Microsoft hosts); verify in #20 before other backend work |
| Business approvals (workshops, accountant, device matrix, hosting) delay dependent issues | Critical-path slip | Draft-first approach; time-boxed reviews; provisional decisions recorded with owner and expiry; W0 gate lists exactly what must be confirmed |
| Cross-wave contracts (#41/#32, #37/#43, #34/#37, media before #31, email before #47) | Blocked starts or ad-hoc coupling | Contracts and fail-closed defaults defined in Section 6.2 notes and the blueprints; architecture tests enforce direction |
| Over-scoped issues (#32, #56, #61) | Unreviewable PRs, late threat modelling | Sub-issues with own branches; threat models moved to W2 |
| Licensing of rendering/imaging libraries | Compliance | D15 lists permissive alternatives; verify before adoption in #32a/#35 |
| Real-device and printer testing cannot run in CI | Late discovery of scanning/printing problems | Interim staging from W1; device lab checklist from #19; physical rehearsals in #35, #37, #48, #61c; emulation treated as supplementary |
| Configurable taxonomy increases complexity | Slower first release | Seed data gives a working default; admin UIs ship with each catalog issue; rule languages kept small |
| Offline/PWA state corrupting authoritative data | Financial/custody integrity | Offline queue limited to idempotent scans and owned by one issue; server always authoritative; conflicts surfaced (#37, #51) |
| Multi-branch scoping mistakes | Data leakage | Deny-by-default policies, generated matrix tests with field masks (#24), architecture test for missing policy, filter-inference tests (#44) |
| Report/projection drift or reporting load hurting transactions | Wrong decisions, slow billing | Reconciliation jobs and visible freshness (#44–#46); projections never authoritative; dedicated pool, timeouts and worker-only large ranges; mixed-load test |
| Provider integration half-posting | Financial inconsistency | Intent → call → verified outcome pattern, idempotent callbacks, reconciliation worker (#55) |
| Pre-baseline staging exposed on the internet | Credential theft, early compromise | Interim staging behind a network gate and identity-aware proxy, no public hostname before #23, synthetic data only (#22) |
| Phone browsers cannot drive thermal printers | Labels not printed at the counter | Print queue and print station from #35; PDF fallback |
| Observability stack too heavy for one VM | Disk/RAM exhaustion, unowned operations | Collector-only default with hosted backend, log caps, external dead-man's switch (D13) |

---

## 11. Decisions required from the business owner

1. **Backend platform confirmation and build environment** (D1): keep ASP.NET Core (.NET 10) as the roadmap states
   and either allowlist the SDK/NuGet/npm/Playwright hosts for Claude Code cloud sessions (Section 12) or run
   backend issues on a self-hosted runner/dev container with Docker; or switch to a Node.js/TypeScript backend that
   the current environment supports. The plan assumes ASP.NET Core. Needed before W1.
2. **Hosting model and indicative monthly budget** (D17): cloud (which provider) or on-premises; this fixes IaC
   tooling, backup targets, TLS, and is needed by #19 for the RPO/RTO/availability compatibility criterion.
3. **Providers** (D20): SMS/WhatsApp/email vendors, payment gateway (UPI/card), accounting export target.
4. **Payment rule for dispatch (#43) and partial-delivery policy for multi-garment orders (#48)**: full payment,
   partial threshold, per-job share, or approved exceptions (who may approve — Owner only, or Admin too); whether
   advances may unlock dispatch; whether Delivery may collect the balance at the doorstep (cash/UPI) — if yes,
   the take-payment screen is granted to Delivery for their own stops (online-only) and the dispatch policy gains
   `collect_on_delivery`.
5. **Valuation method** (#40) and rounding/round-off conventions (#41) with the accountant; statutory retention
   period for GST records (#57).
6. **Branches at launch** with timezones, working calendars and GST registrations (#25).
7. **Device, browser and printer matrix** to support (#19, #52), including hardware scanners and label printers.
8. **Retention periods** for measurements, images, feedback free text, notification bodies, logs and backups
   (#19, #57).
9. **Label format** (thermal size, QR in addition to Code 128) (#35).
10. **Initial catalog, measurement templates, design options and QC checklists** to be reviewed from the seeded
    drafts (#17, #27, #29, #30, #34).
11. **Amend issue #41's "Depends on"**: replace E06-F01 with the pricing contract defined in Section 6.2 (and add
    E09-F01 to #32's dependencies) so that wave 3 can start without a dependency cycle; if declined, the fallback
    in Section 6.2 note 1 applies.
12. **Primary authentication strategy and devices** (#23): local staff accounts with password + TOTP/passkeys (the
    plan's assumption) or federation with an external identity provider; MFA-required roles beyond Owner, Admin
    and Cashier; whether counter and workshop devices are shared per station or personal, and for which roles
    password-only re-login on a trusted device is acceptable.
13. **Permission matrix** (#24): approve the default role → permission grants and any custom roles required at
    launch.
14. **Telemetry backend** (D13): hosted backend (default: collector on the VM shipping to Grafana Cloud or
    equivalent) or the self-hosted stack on a separate VM.
15. **Operations ownership and alert channel** (#58): who receives P1 pages outside business hours, via which
    channel (WhatsApp/SMS/Telegram/e-mail), and the escalation contact; the alert receivers are built from this.

---

## 12. Executing this plan with Claude Code

- **Environment** (verified in the planning session: no `dotnet`, Node 22 present, Docker CLI without a daemon):
  a Claude Code environment needs a SessionStart hook that installs the .NET 10 SDK (`dotnet-install.sh --channel
  10.0`, cached), `pnpm install --frozen-lockfile` and `pnpm exec playwright install chromium`, which requires the
  network allowlist `dotnet.microsoft.com`, `builds.dotnet.microsoft.com`, `api.nuget.org`, `registry.npmjs.org`
  and the Playwright browser CDN; Docker-in-Docker is not available there, so `Integration` tests use an
  externally provided PostgreSQL/MinIO (`TAILOR360_TEST_DATABASE_URL`, `TAILOR360_TEST_S3_ENDPOINT`) when set,
  Testcontainers when a daemon is reachable, and otherwise skip with a visible warning (a failure under `CI=true`).
  `scripts/dev doctor` prints which tiers can run. The image definition and hook live under
  `infra/dev-environment/` (#20). Alternatively, backend issues run on a self-hosted runner/dev container with
  Docker (Section 11 item 1).
- **Operating procedure (one issue or sub-issue per session)**: (1) read the issue, this plan's blueprint, the
  Section 6.2 notes that touch it and the threat model covering its flow; (2) write a short plan in the PR
  description with the evidence checklist; (3) implement on `feat/eXX-fYY[a-c]-<slug>` with Conventional Commits
  `Refs #NN`; (4) run the fast checks locally (`dotnet format`, `dotnet build`, unit/architecture/contract tests,
  integration tests when the environment supports them, `pnpm lint`, `pnpm test`, overflow helper for UI); (5)
  open a ready-for-review PR linked to the issue and read the CI run (which carries the integration and E2E
  evidence a cloud session cannot produce) before requesting review; (6) address review and CI until green;
  (7) attach evidence; (8) close the issue on merge with evidence links; the last PR of
  an epic adds the closure document (Section 6.4).
- **Session boundaries**: sessions do not start a dependent issue until the dependency is merged; parallel
  sessions follow the wave/lane table to avoid overlapping modules; Reporting-module issues are sequenced.
- **Scope discipline**: no unrelated refactors; anything discovered out of scope becomes a new issue linked to the
  epic; contracts owed to later issues (Section 6.2 notes) are delivered exactly as specified so the later issue
  can start.
- **Repository instructions**: `CLAUDE.md` (from #22) is the single place for commands and rules; this plan is
  referenced, not duplicated.

---

## 13. Immediate next steps (first ten pull requests)

| # | PR | Issue | Notes |
| --- | --- | --- | --- |
| 1 | This plan (`docs/IMPLEMENTATION_PLAN.md`) | #1 | Owner reviews decisions in Section 11 |
| 2 | Workflow maps (current and target), glossary, RACI, exception catalogue, category hierarchy, measurement field sets | #17 | Drafted for the workshop |
| 3 | Architecture docs, architecture rules and ADR-0001…0013 | #18 | Includes object-storage ownership |
| 4 | NFRs per hosting model, SLOs, data classification, DoR/DoD, release gates, reviews | #19 | Proposed numeric targets for confirmation |
| 5 | Solution scaffold, compose environment, health checks per component, minimal CI, branch protection, architecture tests | #20 | First backend PR; validates the environment |
| 6 | Persistence conventions, migrations, outbox, audit writer, configuration, flags, seed commands | #21 | Platform foundation |
| 7 | CI gates, PR policy check, templates, CODEOWNERS, `CLAUDE.md`, interim staging, workflow demo | #22 | Can run in parallel with #23 and #50 |
| 8 | Authentication, sessions, MFA, passkeys, recovery, email port, auth screens, auth threat model | #23 | Behind the BFF |
| 9 | Design system, layouts, form contract, reference journeys, Storybook | #50 | Parallel frontend lane |
| 10 | RBAC, branch scopes, permission matrix approval, authorisation regression suite | #24 | Unlocks all business modules |

Earlier in this session a Node.js/TypeScript scaffold (pnpm workspace with a shared domain package: category
measurement fields, GST calculation, barcode format, order state machine, permissions) was drafted before the
roadmap's ASP.NET Core direction was reviewed. It is kept out of the repository; its category field lists and GST
test cases are reused as seed-data and golden-test drafts in #17, #27 and #41.
