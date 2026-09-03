# HyFib Tailor 360 — Implementation Plan

Status: **Proposed** (for review by the product owner and technical reviewer)
Source of truth for scope: GitHub issues [#1](https://github.com/MK-AIFy/HyFib-Tailor360/issues/1) (roadmap), #2–#16 (epics) and #17–#61 (feature issues).
Last reviewed against issues: 2026-09-03 (all 61 open issues read in full).

> This plan turns the roadmap into an executable, dependency-ordered sequence of pull requests. It records the
> architecture, conventions and quality gates every implementation issue must follow, and gives each feature
> issue a concrete blueprint (modules, data, endpoints, screens, tests, evidence). Items that need a human
> decision are collected in [Section 11](#11-decisions-required-from-the-business-owner).

---

## Table of contents

1. [Executive summary](#1-executive-summary)
2. [What the issues require (requirements digest)](#2-what-the-issues-require-requirements-digest)
3. [Key decisions and assumptions](#3-key-decisions-and-assumptions)
4. [Target architecture](#4-target-architecture)
5. [Engineering standards and Definition of Done](#5-engineering-standards-and-definition-of-done)
6. [Delivery plan: milestones, lanes and ordering](#6-delivery-plan-milestones-lanes-and-ordering)
7. [Traceability matrix (issue → milestone → branch → evidence)](#7-traceability-matrix)
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
| M3 | Customer, measurements, catalog, design, media | #26, #29, #27, #28, #30, #31 | Customers, versioned templates, measurement capture, design snapshots, secure images |
| M4 | Orders, workflow, barcode custody, QC | #41, #32, #33, #34, #35, #36, #37 | Multi-garment orders, job cards, workflow engine, labels, scanning, custody chain |
| M5 | Inventory, billing, payments, reporting, delivery, feedback | #38, #39, #42, #43, #40, #54, #47, #48, #49, #44, #45, #46, #55 | Stock ledger, GST invoices, payments and dispatch gate, notifications, delivery, feedback, reports, adapters |
| M6 | Hardening, integrations, security, operations, launch | #51, #52, #56, #57, #58, #59, #60, #61 | PWA resilience, WCAG/cross-browser, ASVS baseline, privacy/audit, observability, CI/CD, backups/DR, UAT and go-live |

Delivery is one focused branch and pull request per implementation issue (a roadmap delivery principle). The
plan identifies three parallel lanes (backend platform, frontend/PWA, governance/docs) so that two or three
sessions can run concurrently without merge conflicts.

**Two things need attention before implementation starts** (details in Section 3 and Section 11):

1. The roadmap's backend is ASP.NET Core. The Claude Code cloud environment used for this session has **no .NET
   SDK** and its egress policy blocks the Microsoft download hosts, so backend issues need an environment with the
   .NET 10 SDK pre-installed (or an allowed egress rule). A Node.js/TypeScript backend would work in the current
   environment but contradicts the roadmap; that choice is the owner's, not this plan's.
2. Several M1 items are business approvals (workshops, accountant sign-off, device matrix, hosting model).
   Claude can draft every artefact, but approval gates are human. The plan schedules drafting first so approvals
   are never on the critical path for longer than one review cycle.

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
- Categories, measurements, workflow phases, taxes, prices, alerts and feature availability are **configuration,
  not code**.
- Posted invoices and stock-ledger entries are immutable; corrections are compensating transactions.
- Reporting projections are never the authoritative source of financial, stock, workflow or custody state.
- Module ownership is preserved: no cross-module table access unless an ADR permits it.
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
| D4 **(ADR)** | **Private S3-compatible object storage** (MinIO locally; S3, R2, or Azure Blob via S3 API in production), random object keys, short-lived signed URLs issued only after an authorisation check. | #31 requires private storage, signed access and no stable URLs. |
| D5 **(ADR)** | **BFF pattern**: the ASP.NET Core host serves the PWA and the `/api/v1` surface on the same origin; authentication is an `HttpOnly; Secure; SameSite=Lax` session cookie backed by server-side session/ticket storage; anti-forgery via header token; no bearer tokens in browser storage. Third-party/trusted clients authenticate separately (API keys or OAuth client credentials) and never share the cookie scheme. | #1, #23, #53. |
| D6 **(ADR)** | **Transactional outbox** table per module schema written in the same transaction as the aggregate; a worker dispatches to in-process handlers, notification channels and webhooks with at-least-once delivery, inbox/idempotency records and dead-letter queue. | #21, #47, #54. |
| D7 **(ADR)** | **Single organisation, branch-aware from day one**: `organisation_id` (fixed) and `branch_id` on all operational aggregates; policy-based authorisation evaluates branch scope; tenancy can be added later without schema rewrites. | #18 acceptance criteria. |
| D8 **(ADR)** | **Configurable taxonomy stored as versioned data**: categories, service types, measurement templates, design option groups, workflow definitions, QC checklists, price lists and tax configuration are draft → published (immutable) → retired records with seed data for the initial scope. | #1, #27, #29, #30, #33, #41. |
| D9 | **Identifiers**: UUIDv7 primary keys (`Guid.CreateVersion7()`), human-readable numbers allocated from per-branch/financial-year sequences at posting time (`SELECT … FOR UPDATE` on a sequence row), barcode payloads = namespace letter + a 12-character Crockford base32 body made of 11 random characters (55 bits of entropy) followed by 1 check character (Crockford mod-37 check symbol computed over the namespace and the 11 random characters), e.g. `G-7K3M9QW2XZ4B` (garment job; `B` is the check character), `S-…` (stock), `I-…` (invoice), `R-…` (receipt). | #35 (opaque, no PII, separate namespaces), #42 (atomic numbering). |
| D10 | **Money and tax**: `decimal(18,2)` amounts, `decimal(18,4)` unit rates, `decimal(6,3)` tax rates; line-level half-up rounding to paise, document round-off to the nearest rupee (configurable), CGST/SGST vs IGST decided by place of supply; financial year April–March; every calculation stores the pricing/tax configuration version used. | #41, #42. |
| D11 | **Time**: `timestamptz` in UTC; branch IANA timezone (default `Asia/Kolkata`) for display, due dates and report cut-offs; server timestamps are authoritative for scans and transitions. | #33, #37, #44. |
| D12 | **Background processing**: a .NET Worker Service container running outbox dispatch, notification delivery, webhook delivery, low-stock evaluation, retention/cleanup, export generation, report projection rebuilds and backup-age checks; database-lease based scheduling (Quartz.NET with the PostgreSQL job store is the fallback if scheduling needs grow). | #21, #40, #46, #47, #57. |
| D13 | **Observability**: OpenTelemetry traces/metrics/logs exported via OTLP; Serilog structured logs with a redaction policy; `AspNetCore.HealthChecks` for liveness/readiness/startup including database, object storage, outbox lag and migration state. | #20, #58. |
| D14 | **Testing**: xUnit + FluentAssertions, Testcontainers (PostgreSQL, MinIO, ClamAV) for integration, NetArchTest/ArchUnitNET for module boundaries, FsCheck for property tests, Verify for snapshots (PDF/JSON), Playwright (Chromium, Firefox, WebKit) + axe-core for E2E/accessibility, k6 for load, Lighthouse CI for performance budgets. | #20, #22, #52, #58, #61. |
| D15 | **Barcode/PDF rendering**: ZXing.Net for Code 128/QR bitmaps; PDF via a `IPdfRenderer` port with QuestPDF as default adapter (verify the Community licence fits HyFib's revenue) and PDFsharp as the MIT alternative. Image processing via SkiaSharp or Magick.NET (permissive licences) for decode-validate, EXIF strip, re-encode and thumbnails. | #31, #35, #42, #55. |
| D16 | **Malware scanning**: ClamAV (`clamd`) behind an `IMalwareScanner` port, feature-flagged; uploads are quarantined until the scan passes. | #31, #56. |
| D17 | **Deployment baseline**: Docker Compose (reverse proxy with automatic TLS, web host, worker, PostgreSQL, MinIO, ClamAV, OpenTelemetry collector) for single-VM/on-prem; the same images run under Kubernetes/Helm later. Terraform (cloud) or Ansible (on-prem) for environment provisioning. | #1 portability, #59. |
| D18 | **Backups**: pgBackRest (or WAL-G) base + WAL archiving to encrypted object storage with separate credentials; MinIO versioning and replication; weekly automated restore into an isolated environment. | #60. |
| D19 | **Repository layout**: single repository (`src/`, `clients/pwa/`, `tests/`, `docs/`, `infra/`, `.github/`) so one PR can carry API, UI, migrations and docs for an issue. | #20, #22. |
| D20 **(owner)** | **Providers**: default adapters are fakes; first real adapters are SMTP email, an Indian SMS provider (e.g. MSG91), WhatsApp via Meta Cloud API or an aggregator, UPI/card via Razorpay or PhonePe, accounting export in Tally XML. Enabled per branch by feature flag only after contract tests pass. | #47, #55. |

**Assumptions**

- A1. One legal entity (organisation) with one or more branches, all in India; INR only; GST-registered.
- A2. Staff-only application; customers interact through expiring links (status, feedback), not accounts.
- A3. Concurrent users ≈ 20–50 per branch, orders ≈ 100–500/month per branch, images ≈ 5 per garment. The NFR
  issue (#19) will replace these with measured targets.
- A4. Hardware: Android phones/tablets, iPhone/iPad, desktop browsers, USB/Bluetooth keyboard-wedge scanners,
  thermal label printers (via PDF/print dialog or a print bridge) and A4 printers.

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
│  Orders/Workflow · Custody/Barcode · Inventory · Billing/Payments · Reporting ·       │
│  Notifications/Feedback · Integration · Platform (outbox, audit, flags, config)      │
└───────────────┬───────────────────────────────┬───────────────────────────────────────┘
                │ EF Core (schema per module)    │ signed URLs / uploads
                ▼                               ▼
        PostgreSQL 16 (one DB)          Private object storage (S3 API)
                ▲
                │ outbox / inbox / leases
┌───────────────┴───────────────── Worker host (.NET Worker Service) ───────────────────┐
│  Outbox dispatcher · Notification sender · Webhook sender · Low-stock evaluator ·      │
│  Retention/cleanup · Export generator · Projection rebuilds · Backup-age monitor       │
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
│   │   ├── Tailor360.Platform.Persistence    # EF conventions, outbox/inbox, sequences, idempotency, audit
│   │   ├── Tailor360.Platform.Security       # permission catalogue, policies, branch scope, step-up
│   │   └── Tailor360.Platform.Observability  # OTel, Serilog redaction, health checks, correlation
│   ├── Modules/
│   │   ├── Identity/      (Domain, Application, Infrastructure, Api, Contracts)
│   │   ├── Customers/     (customers, consent, measurement templates, measurement versions)
│   │   ├── Catalog/       (categories, service types, design option groups, rules, media links)
│   │   ├── Media/         (upload pipeline, storage, signed access, retention)
│   │   ├── Orders/        (estimates, orders, garment jobs, workflow engine, QC, alterations, holds)
│   │   ├── Custody/       (barcode identities, labels, scan/custody events, reconciliation)
│   │   ├── Inventory/     (items, units, suppliers, locations, ledger, reservations, stocktake, alerts)
│   │   ├── Billing/       (pricing/tax engine, price lists, estimates, invoices, credit notes, payments, receipts, cashier sessions)
│   │   ├── Reporting/     (read models, projections, exports)
│   │   ├── Notifications/ (templates, intents, dispatch, in-app centre, customer links, feedback, service recovery)
│   │   └── Integration/   (integration events, webhooks, provider adapters, accounting export)
│   ├── Hosts/
│   │   ├── Tailor360.Web              # BFF + API + static PWA hosting, module registration
│   │   └── Tailor360.Worker           # background processing host
│   └── Tools/
│       └── Tailor360.Cli              # migrate, seed (guarded), create-admin, reprint-label, replay-outbox
├── clients/pwa/                       # React + TypeScript PWA (Vite), generated API client, Storybook
├── tests/
│   ├── Tailor360.UnitTests            # per-module domain/application tests, property tests
│   ├── Tailor360.IntegrationTests     # Testcontainers (PostgreSQL, MinIO, ClamAV), API tests via WebApplicationFactory
│   ├── Tailor360.ArchitectureTests    # module dependency rules, no cross-schema access
│   ├── Tailor360.ContractTests        # OpenAPI diff, consumer contracts, adapter contract suites
│   ├── e2e/                           # Playwright (Chromium/Firefox/WebKit, phone/tablet/desktop profiles), axe
│   └── load/                          # k6 scenarios
├── docs/                              # PRD, glossary, ADRs, threat models, runbooks, metric dictionary, UAT scripts
├── infra/                             # docker-compose.*.yml, Dockerfiles, reverse proxy, otel, backup, terraform/ansible
└── .github/                           # workflows, issue/PR templates, CODEOWNERS, dependabot
```

Module internals follow one shape: `Domain` (aggregates, invariants, domain events — no framework references),
`Application` (commands/queries, validators, authorisation requirements, ports), `Infrastructure` (EF `DbContext`,
repositories, adapters, outbox handlers), `Api` (Minimal API endpoint groups, DTOs), `Contracts` (integration
events and public read contracts other modules may reference). Only `Contracts` may be referenced across
modules; enforced by architecture tests (#18, #20).

### 4.3 Module responsibilities and owned data

| Module | Owns (schema) | Publishes (contracts) | Consumes |
| --- | --- | --- | --- |
| Identity/Admin | users, roles, permissions, branch assignments, sessions, MFA/passkeys, recovery, branches, admin audit views; the feature-flag administration UI/API calls Platform through its contract (Platform owns the flag store) | `UserDeactivated`, `BranchCreated` | Platform (flag contract) |
| Customers/Measurements | customers, consent records, communication preferences, duplicate candidates, merges, measurement templates/versions, measurement versions, drafts | `CustomerCreated/Merged`, `MeasurementVersionConfirmed` | Identity (branch scope) |
| Catalog/Design | categories, service types, catalog versions, design option groups/options/rules, QC checklist templates | `CatalogVersionPublished` | — |
| Media | media objects, derivatives, quarantine, retention holds, access log | `MediaReady`, `MediaQuarantined` | Identity, Customers/Orders (authorisation callbacks) |
| Orders/Workflow | estimates, orders, garment jobs, snapshots (measurement, design, price), workflow definitions/versions, job phases, assignments, QC results, rework, alterations, holds, cancellations | `OrderConfirmed`, `JobEnteredProduction`, `JobPhaseChanged`, `QcRecorded`, `JobReadyForDelivery`, `OrderCancelled` | Customers, Catalog, Billing (pricing service contract), Custody (custody state), Billing (paid status) |
| Custody/Barcode | barcode identities, label prints, scan/custody events, pending transfers, reconciliation cases | `CustodyTransferred`, `ScanRecorded`, `DispatchRecorded` | Orders (job state), Billing (paid status), Identity |
| Inventory | items, units/conversions, suppliers, locations, reorder rules, ledger entries, reservations, purchase receipts, stocktakes, low-stock alerts, customer-material custody records | `StockReserved/Consumed`, `LowStockRaised/Cleared`, `StocktakePosted` | Orders (job references) |
| Billing/Payments | price lists, tax configuration, calculation versions, estimates, invoices, invoice lines/tax components, sequences, credit/debit notes, payments, allocations, refunds, receipts, cashier sessions | `InvoicePosted`, `PaymentRecorded`, `InvoicePaidStatusChanged`, `CreditNotePosted` | Customers, Orders, Catalog |
| Reporting | read models/materialised views, metric dictionary, export jobs, reconciliation results | — | all modules' events |
| Notifications/Feedback | templates/versions, intents, deliveries, in-app notifications, customer status links, feedback tokens/responses, service-recovery cases | `NotificationDelivered/Failed`, `FeedbackReceived`, `ServiceRecoveryOpened` | Customers (consent), Orders, Billing, Custody, Inventory events |
| Integration | integration event envelopes, webhook subscriptions/deliveries, provider configurations, accounting export batches, payment callbacks | `WebhookDelivered/DeadLettered`, `PaymentCallbackReconciled` | outbox events from all modules |
| Platform | outbox/inbox, idempotency keys, sequences, audit events, configuration, feature flags (`platform.feature_flags`: store, evaluation, evaluation audit) and correlation | `FeatureFlagChanged` | — |

### 4.4 Cross-cutting mechanisms

- **Authentication and session (D5, #23)**: ASP.NET Core Identity with a strengthened password hasher (Argon2id
  or PBKDF2 at ASVS-compliant cost), TOTP MFA and passkeys (WebAuthn, supported natively by Identity in .NET 10),
  server-side session tickets with rotation on privilege change, sliding inactivity and absolute timeouts, device
  and session inventory, logout-all and immediate revocation checked per request via a revocation cache.
- **Authorisation (#24)**: a permission catalogue (`customers.read`, `orders.confirm`, `custody.scan`,
  `billing.post_invoice`, `payments.record`, `inventory.approve_variance`, `reports.export`, `admin.users`, …)
  mapped to default roles; `IAuthorizationRequirement` handlers evaluate permission + branch scope + resource
  ownership; endpoints declare `.RequirePermission("orders.confirm")` and resource handlers load the aggregate's
  branch; workers carry an explicit system principal with a declared scope.
- **Validation and errors (#53)**: FluentValidation in the application layer; RFC 9457 problem details with
  field errors, correlation ID and no stack traces; request size/time limits; rate limits per user/IP/route.
- **Idempotency (#21, #53)**: `Idempotency-Key` header required on confirm/scan/post/pay/webhook commands; key +
  user + route + request hash stored with the response for replay; scan events also carry a client event UUID.
- **Concurrency (#21, #53)**: `xmin` (or explicit `version`) as concurrency token; `ETag`/`If-Match` on editable
  aggregates; 409 problem details with current version.
- **Audit (#57)**: append-only `platform.audit_events` written in the same transaction as the mutation via a
  `SaveChanges` interceptor plus explicit domain audit calls for reads of sensitive data (measurement sheet,
  media, exports); restricted viewer; retention independent of logs; hash chain per day for tamper evidence.
- **Outbox/inbox (D6)**: `outbox_messages` per module schema; worker polls with `FOR UPDATE SKIP LOCKED`, dispatches
  with retry/backoff/jitter, marks processed, moves poison messages to dead letter with operator replay.
- **Feature flags (#21, #25)**: `platform.feature_flags` with organisation/branch scope, owner-only mutation,
  evaluation audit, `Microsoft.FeatureManagement` filters for evaluation, safe defaults (off).
- **Configuration (#21)**: `IOptions<T>` bound from `appsettings` + environment + secret store, validated on
  startup (`ValidateOnStart`), fails fast if required values are missing.
- **Media pipeline (#31)**: upload → size/type allowlist → decode-validate signature → quarantine object →
  malware scan → strip metadata and re-encode → derivatives (thumb/preview) → mark ready → signed URL issuance
  after authorisation → access log.
- **Barcode/scan abstraction (#36)**: PWA `ScannerSource` interface with `CameraSource` (ZXing / BarcodeDetector),
  `KeyboardWedgeSource` (buffer + terminator detection, ignored while typing in unrelated fields) and
  `ManualEntrySource`; all produce `{ raw, normalised, namespace, id, checksumValid, source, timestamp }`.

### 4.5 Data model overview (aggregate roots and invariants)

- **Customer** (branch visibility, normalised name/phone, consent records, preferences, status). Invariant: phone
  is validated, not unique; duplicates are detected and merged only by authorised decision.
- **MeasurementTemplateVersion** (category link, fields with unit/precision/ranges/conditions, diagram media,
  status). Invariant: published versions immutable.
- **MeasurementVersion** (customer, template version, canonical values in millimetres with display unit,
  reason, taken-by). Invariant: never edited; corrections create a new version.
- **CatalogVersion** (categories, service types, design option groups/options/rules). Invariant: one coherent
  published version per order.
- **Order** → **GarmentJob[]** (category/service version, measurement version, design snapshot, media links,
  price snapshot, due date, priority, workflow version, phases, assignments, QC results, rework, alteration
  links, holds, custody state). Invariant: job snapshots immutable after confirmation; ready-for-delivery derived
  from workflow complete + QC passed + custody reconciled.
- **BarcodeIdentity** (namespace, opaque payload, entity ref, status active/invalidated/superseded, label
  prints). Invariant: exactly one active identity per garment job.
- **ScanEvent / CustodyTransfer** (immutable; idempotency key; from/to custodian; state pending/accepted/
  rejected/expired). Invariant: server validates expected custodian and prerequisites; history is never edited.
- **StockItem**, **LedgerEntry** (immutable, signed quantity in base unit, type, references), **Reservation**,
  **PurchaseReceipt**, **Stocktake**. Invariant: balances rebuildable from ledger; no oversubscription.
- **PriceListVersion**, **TaxConfigurationVersion** (effective-dated, immutable when published).
- **Invoice** (draft → posted; posted immutable; lines and tax components snapshot; sequence per branch/FY),
  **CreditNote/DebitNote**, **Payment** (with allocations, reversal/refund), **Receipt**, **CashierSession**.
  Invariant: balance = posted charges − allocations − credits + refunds; numbers never reused.
- **NotificationIntent → Delivery**, **CustomerLink** (random token, expiry, revocation, purpose), **Feedback**,
  **ServiceRecoveryCase**.
- **IntegrationEvent**, **WebhookSubscription/Delivery**, **AccountingExportBatch**.

### 4.6 PWA architecture (#50, #51, #52)

- Routing by role-optimised shells: phone (bottom navigation + scanner-first "Scan" action), tablet
  (master-detail), desktop (side navigation + dense tables). Layouts chosen by container queries, not user agent.
- State: TanStack Query for server state with `Idempotency-Key` injection and conflict handling; local drafts
  in IndexedDB encrypted with a per-session key, bound to user + branch, expiring, cleared on logout.
- Bounded offline queue only for approved idempotent operations (scan submissions); billing, payment and
  inventory reconciliation are online-only with explicit "offline: not available" UI.
- Service worker: precache versioned assets; network-first for API; never cache `/api` responses containing
  protected data; update prompt with API compatibility check (`/api/version` minimum client version).
- Design system: tokens (colour, type scale, spacing, motion, focus, density, breakpoints), accessible components
  (forms, numeric measurement input with large touch targets and numeric keypad, scanner, camera capture, tables
  and cards, filters, drawers/dialogs, timeline, status badges, alerts, empty/error/loading, confirmation with
  undo). Storybook with axe checks and visual regression baselines.
- Generated API client from OpenAPI (`openapi-typescript` + `openapi-fetch`), so contract changes fail the
  frontend type check.

### 4.7 Deployment topology (#59)

- Images: `tailor360-web` (host + PWA), `tailor360-worker`, `tailor360-cli`; non-root, read-only filesystem,
  pinned base images, SBOM + signature.
- Compose stack: reverse proxy (Caddy or Traefik, automatic TLS), web, worker, PostgreSQL, MinIO, ClamAV,
  OpenTelemetry collector, backup sidecar. Environments: dev (compose), test (CI ephemeral), staging, production.
- Release: build once, promote the identical digest; expand-migrate-contract migrations run by the CLI job before
  rollout; readiness gates; documented rollback/roll-forward.

---

## 5. Engineering standards and Definition of Done

These apply to every implementation issue and are the content of `CLAUDE.md` (#22).

### 5.1 Definition of Done (per pull request)

1. Linked to exactly one issue; only scoped changes; branch `feat/eXX-fYY-short-name`.
2. Server-side authorisation, validation, idempotency (where the endpoint is retried) and audit events on every
   state-changing endpoint.
3. Unit tests for domain rules, integration tests for persistence/API, architecture tests still green,
   E2E coverage for any new critical journey; synthetic data only.
4. Migrations forward-only and backward compatible with the previous release (expand/contract); rollback or
   restore note in the PR.
5. OpenAPI updated; generated client regenerated; no undocumented breaking change.
6. No secrets, no PII in logs, telemetry names and redaction reviewed.
7. Accessibility check (axe) and responsive check (phone/tablet/desktop profiles) for UI changes.
8. Documentation updated (module README, ADR if a decision changed, runbook if operations changed).
9. PR evidence checklist completed (tests run, screenshots for UI, migration output, security notes).

### 5.2 Conventions

- Money, time, identifiers and rounding per D9–D11; no floating point for money.
- Naming: schemas `identity`, `customers`, `catalog`, `media`, `orders`, `custody`, `inventory`, `billing`,
  `reporting`, `notifications`, `integration`, `platform`; tables snake_case; all tables carry `id`,
  `organisation_id`, `branch_id` (where scoped), `created_at`, `created_by`, `updated_at`, `updated_by`, `xmin`.
- No soft-delete of business records; deactivation flags instead. Hard delete only for retention-policy jobs on
  approved classes (media derivatives, expired drafts, expired links).
- REST: `/api/v1/{module}/{resource}`; commands as POST sub-resources (`/orders/{id}/confirm`); cursor
  pagination; `filter[...]`, `sort`, `fields`; problem details errors; `X-Correlation-Id`.
- Domain events named in past tense; integration events versioned `orders.order-confirmed.v1`.
- Logging: no request bodies, tokens, measurements, image bytes; correlation and causation IDs everywhere.
- Frontend: TypeScript strict, ESLint + Prettier, no `any`, components documented in Storybook, translations via
  keys (English first).

### 5.3 Test pyramid and gates

| Layer | Tooling | Gate |
| --- | --- | --- |
| Unit and property | xUnit, FsCheck, Vitest | every PR |
| Architecture | NetArchTest/ArchUnitNET rules | every PR |
| Integration | Testcontainers PostgreSQL/MinIO/ClamAV, WebApplicationFactory | every PR |
| Contract | OpenAPI lint + diff (oasdiff), adapter contract suites | every PR |
| E2E + accessibility | Playwright (Chromium/Firefox/WebKit; phone/tablet/desktop), axe | every PR for touched journeys; full nightly |
| Security | CodeQL, dependency review, gitleaks, Trivy (images/IaC), SBOM | every PR / release |
| Performance | Lighthouse CI budgets, k6 smoke; full load test per release | release |
| Operations | migration dry-run, restore test, DR exercise | release / scheduled |

---

## 6. Delivery plan: milestones, lanes and ordering

### 6.1 Ordering rules

- The order below is derived from the "Depends on" field of every feature issue. An issue starts only when its
  dependencies are merged to `main` (or the owner explicitly accepts a stub).
- One issue = one branch = one Claude Code session = one pull request. Issues marked XL are split into
  sub-issues (proposed splits are in the blueprints) so that each PR stays reviewable (target < 1,500 changed
  lines excluding generated code and tests).
- Three lanes can run concurrently: **Lane A** backend platform and modules, **Lane B** PWA and design system,
  **Lane C** governance, security and operations documents. Issues in the same wave touch disjoint modules.
- Business approvals (workshops, accountant, device matrix) are scheduled as review gates at the end of the
  wave that produces the draft, never as blockers for drafting.

### 6.2 Waves

| Wave | Milestone | Issues (parallel groups shown with `∥`) | Exit gate |
| --- | --- | --- | --- |
| W0 | M1 | #17 → #18 → #19 | Owner approves glossary, workflow maps, ADRs and NFRs; repo has `docs/prd`, `docs/adr`, `docs/nfr` |
| W1 | M2 | #20 → #21 → (#22 ∥ #23 ∥ #50) → #24 → (#25 ∥ #53) | Clean clone builds; CI gates block bad changes; login with MFA behind BFF; permission matrix tests green; admin UI; design system in Storybook |
| W2 | M3 | (#26 ∥ #29) → #27 → (#28 ∥ #30) → #31 | Customer search/dedup; catalog and measurement templates seeded and published; measurement wizard on phone/tablet; secure image pipeline |
| W3 | M4 | #41 → #32 → (#33 ∥ #35) → (#34 ∥ #36) → #37 | Multi-garment order confirmed with snapshots; job cards; workflow engine and workboards; labels printed; scanning on real devices; custody chain end-to-end |
| W4 | M5 | (#38 ∥ #42 ∥ #54) → (#39 ∥ #43 ∥ #47) → (#40 ∥ #48 ∥ #44) → (#49 ∥ #45 ∥ #46 ∥ #55) | Ledger-backed stock; posted GST invoices and receipts; payment-gated dispatch; notifications; feedback; reconciled reports; adapters behind flags |
| W5 | M6 | (#51 ∥ #56 ∥ #57) → (#52 ∥ #58) → #59 → #60 → #61 | Installable PWA with safe updates; ASVS baseline and pen test; privacy/audit; observability and load tests; CI/CD promotion; backups/DR rehearsed; UAT and go-live |

Dependency note: #41 (pricing engine) lists #32 (orders) as a dependency while #32 needs the pricing service for
order totals, so the issues as written form a cycle and neither could start under the rule in 6.1. The plan
resolves it as follows and the owner is asked to amend issue #41 accordingly (Section 11, item 11):

- #41's dependency on E06-F01 (#32) is **replaced** by a dependency on the pricing contract
  `Billing.Contracts.IPricingService` (`PricingRequest` = catalog service/product references, quantities,
  discounts, place of supply, effective date; `PricingResult` = line components, document totals, configuration
  versions). The contract is the first deliverable of #41 and does not reference any order entity.
- #41 therefore starts W3 with dependencies #19 and #29 only; #32 depends on #41 in addition to its own list.
- Until the owner amends #41, the plan's traceability matrix (Section 7) is the operative dependency list.

### 6.3 Milestone exit criteria mapped to the roadmap

| Roadmap acceptance criterion (#1) | Verified in |
| --- | --- |
| End-to-end garment lifecycle with real labels and devices | #37 (custody rehearsal), #61 (pilot) |
| Dispatch blocked until QC passes and payment rule satisfied | #34, #43, #48 |
| Role and branch isolation tests | #24 (regression suite), #56 |
| Photos and measurements protected by consent, retention, access controls | #26, #28, #31, #57 |
| Browser/device matrix | #52 |
| WCAG 2.2 AA on critical journeys | #50, #52 |
| No unresolved critical/high security findings | #56 (pen test), #59 |
| Load/performance targets and SLOs | #19 (targets), #58 (tests) |
| Backup, PITR, rollback, DR exercises | #59, #60 |
| GST configuration and sample output approved by accountant | #41, #42 |
| UAT sign-off, training, runbooks, monitoring, hypercare | #61 |

---

## 7. Traceability matrix

Branch names follow `feat/eXX-fYY-<slug>`; docs-only issues use `docs/`. Size: S ≤ 1 session, M 1–2, L 2–3,
XL split. "Evidence" lists what the PR must attach to satisfy the issue's acceptance criteria.

| Issue | Epic | Wave | Lane | Branch | Size | Depends on | Modules / areas | Key evidence |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| #17 | E01 | W0 | C | `docs/e01-f01-workflows-glossary` | M | #1 | docs/prd | Workflow maps per category, glossary, RACI, exception catalogue, configurable-vs-fixed table, owner approval |
| #18 | E01 | W0 | C | `docs/e01-f02-architecture-adrs` | M | #17 | docs/adr, docs/architecture | C4 diagrams, ADR-0001…0010, module ownership table, invariants, conventions, architecture-test rule list |
| #19 | E01 | W0 | C | `docs/e01-f03-nfr-slo-dod` | M | #17, #18 | docs/nfr | Numeric NFRs, SLOs, RPO/RTO, data classification, DoR/DoD, waiver process, traceability matrix |
| #20 | E02 | W1 | A | `feat/e02-f01-scaffold-local-env` | L | #18 | solution, compose, health | Clean-clone build log, health checks passing, architecture test failing on forbidden reference, secret scan clean |
| #21 | E02 | W1 | A | `feat/e02-f02-persistence-outbox-config-flags` | L | #20 | Platform.Persistence, Worker | Outbox atomicity test, duplicate delivery test, config fail-fast test, flag audit, migration from empty DB and prior snapshot |
| #22 | E02 | W1 | C | `feat/e02-f03-ci-governance-claude` | M | #20, #21 | .github, CLAUDE.md | Failing checks demo (format/test/arch/secret/vuln), templates, CODEOWNERS, permissions review, CI baseline time |
| #23 | E03 | W1 | A | `feat/e03-f01-auth-sessions-mfa` | L | #18, #19, #21 | Identity, Web host | Cookie/CSRF/lockout tests, revocation SLO test, MFA/passkey/recovery flows, auth audit sample |
| #24 | E03 | W1 | A | `feat/e03-f02-rbac-branch-scope` | L | #23 | Platform.Security, all Api | Permission catalogue, matrix test report, IDOR/cross-branch tests, worker context test |
| #25 | E03 | W1 | A+B | `feat/e03-f03-admin-users-branches-flags` | M | #23, #24 | Identity Api, PWA admin | Admin authorisation tests, step-up test, suspension revocation test, audit before/after view |
| #26 | E04 | W2 | A+B | `feat/e04-f01-customers-consent-dedup` | L | #24, #25 | Customers, PWA | Normalisation/dedup unit tests, merge/concurrency/timeline integration tests, phone/tablet screenshots |
| #27 | E04 | W2 | A+B | `feat/e04-f02-measurement-templates` | M | #17, #24, #29 | Customers (templates), PWA admin | Property tests for unit conversion, publish/retire tests, seeded templates for 5 categories, business review note |
| #28 | E04 | W2 | A+B | `feat/e04-f03-measurement-capture` | L | #26, #27 | Customers, PWA wizard | Validation/snapshot tests, concurrent confirm test, template-change test, device usability notes |
| #29 | E05 | W2 | A+B | `feat/e05-f01-category-service-catalog` | M | #17, #21, #25 | Catalog, PWA admin | Hierarchy/lifecycle tests, cache invalidation test, seeded categories, "add category without code" demo |
| #30 | E05 | W2 | A+B | `feat/e05-f02-design-catalog-snapshots` | L | #29, #27 | Catalog, Orders (snapshot contract), PWA | Rule-engine property tests, snapshot immutability test, job-card render, tablet usability notes |
| #31 | E05 | W2 | A+B | `feat/e05-f03-secure-media-pipeline` | L | #24, #26, #30 | Media, Worker, PWA | Adversarial upload corpus results, signed URL expiry test, EXIF strip test, outage/orphan cleanup tests |
| #32 | E06 | W3 | A+B | `feat/e06-f01-orders-job-cards` (split: 32a backend, 32b intake UI) | XL | #28, #29, #30, #31 (plan adds #41, see 6.2) | Orders, PWA intake | Confirmation atomic/idempotent tests, snapshot regression tests, multi-garment UAT screenshots |
| #33 | E06 | W3 | A+B | `feat/e06-f02-workflow-assignment-workboard` | L | #32, #24 | Orders (workflow), PWA queues | Graph property tests, invalid transition tests, concurrent transition tests, workboard device test |
| #34 | E06 | W3 | A+B | `feat/e06-f03-qc-rework-alteration-hold-cancel` | L | #33 | Orders, PWA | State-machine tests for every exceptional path, integration with inventory/billing/notification stubs, UAT notes |
| #35 | E07 | W3 | A | `feat/e07-f01-barcode-identity-labels` | M | #32, #24 | Custody, PDF adapter | Collision/checksum tests, reprint/invalidate tests, printed test sheets scanned on devices |
| #36 | E07 | W3 | B | `feat/e07-f02-scanner-experience` | M | #35, #50 | PWA scanner | Parser/debounce tests, Android/iPhone/iPad/hardware scanner results, accessibility review |
| #37 | E07 | W3 | A+B | `feat/e07-f03-custody-transfers-idempotency` | L | #35, #36, #33, #34 | Custody, Orders, PWA | State/property tests, concurrency/replay tests, physical rehearsal record |
| #38 | E08 | W4 | A+B | `feat/e08-f01-inventory-masters` | M | #17, #21, #24 | Inventory, PWA | Unit conversion property tests, import/deactivation tests, initial catalog review |
| #39 | E08 | W4 | A | `feat/e08-f02-stock-ledger-reservations` | L | #38, #33 | Inventory | Balance invariant property tests, concurrency reservation tests, purchase/transfer/consume/correction E2E |
| #40 | E08 | W4 | A+B | `feat/e08-f03-low-stock-stocktake-valuation` | L | #38, #39, #47 | Inventory, Worker, Reporting, PWA | Alert dedup tests, stocktake approval separation tests, reconciliation and performance tests |
| #41 | E09 | W3 | A | `feat/e09-f01-pricing-gst-engine` | L | #19, #29 (the issue's #32 dependency is replaced by the pricing contract, see 6.2) | Billing (engine), PWA admin | Accountant golden-master tests, rounding property tests, version publish/reproduction tests |
| #42 | E09 | W4 | A+B | `feat/e09-f02-invoices-numbering-pdf` | L | #41, #32 | Billing, PDF adapter, PWA | Concurrent numbering tests, immutability tests, PDF snapshot + accessibility, barcode retrieval auth tests |
| #43 | E09 | W4 | A+B | `feat/e09-f03-payments-receipts-cashier-dispatch-gate` | L | #42, #37, #24 | Billing, Custody (gate), PWA | Allocation property tests, idempotent payment tests, cashier close UAT, unpaid dispatch rejected E2E |
| #44 | E10 | W4 | A+B | `feat/e10-f01-sales-gst-receivables-reports` | L | #42, #43, #21 | Reporting, Worker, PWA | Golden-data reconciliation, export leak tests, timezone boundary and volume tests |
| #45 | E10 | W4 | A+B | `feat/e10-f02-pipeline-workload-quality-analytics` | M | #33, #34, #37 | Reporting, PWA dashboards | Projection replay/idempotency tests, performance test, ops UAT |
| #46 | E10 | W4 | A+B | `feat/e10-f03-inventory-profitability-exports` | M | #39, #40, #42, #43 | Reporting, Worker exports, PWA | Reconciliation tests, export authorisation/expiry/cleanup tests, load test |
| #47 | E11 | W4 | A+B | `feat/e11-f01-notifications-templates-adapters` | L | #21, #26, #54 | Notifications, Integration adapters, Worker, PWA centre | Adapter contract/retry tests, consent/quiet-hour/dedup tests, provider-down drill |
| #48 | E11 | W4 | A+B | `feat/e11-f02-delivery-queue-status-links-dispatch` | L | #34, #37, #43, #47 | Custody, Notifications (links), PWA delivery | Unpaid/paid/partial E2E, token security tests, delivery-team mobile UAT |
| #49 | E11 | W4 | A+B | `feat/e11-f03-feedback-alterations-service-recovery` | M | #47, #48 | Notifications (feedback), Orders (alteration link), PWA | Token replay/rate-limit tests, follow-up idempotency/escalation tests, journey UAT |
| #50 | E12 | W1 | B | `feat/e12-f01-design-system-layouts` | L | #19, #20 | PWA design system, Storybook | Component a11y + visual regression, device/orientation/zoom matrix, role walkthrough notes |
| #51 | E12 | W5 | B | `feat/e12-f02-pwa-install-updates-resilience` | L | #50, #21, #37 | PWA service worker, drafts, offline queue | Install/update/rollback tests, offline/reconnect/conflict E2E, cache/privacy inspection |
| #52 | E12 | W5 | B | `feat/e12-f03-wcag-cross-browser-performance` | M | #50, #51 | tests/e2e, Lighthouse CI | Cross-browser and axe reports, manual screen-reader/zoom notes, performance budget results |
| #53 | E13 | W1 | A | `feat/e13-f01-api-standards-openapi-idempotency` | M | #18, #21, #23, #24 | Platform, Web host, contract tests | OpenAPI lint/diff in CI, idempotency/concurrency tests, error-leak and rate-limit tests |
| #54 | E13 | W4 | A | `feat/e13-f02-integration-events-webhooks` | M | #21, #53 | Integration, Worker | Commit/rollback/duplicate/replay tests, signature/SSRF tests, slow-receiver load test |
| #55 | E13 | W4 | A | `feat/e13-f03-provider-adapters` | L | #53, #54, #43, #47 | Integration adapters | Contract suites, fault injection, payment callback security tests, accounting balanced totals |
| #56 | E14 | W5 | C+A | `feat/e14-f01-threat-model-asvs-baseline` | L | #18, #19, #24, #53 | docs/security, Platform.Security, CI | Threat models, ASVS traceability, security regression suite, pen test report and remediation |
| #57 | E14 | W5 | A | `feat/e14-f02-privacy-audit-encryption-secrets` | L | #19, #24, #26, #31 | Platform (audit), Customers, Media, Worker | Retention job tests, audit completeness review, rotation exercise log |
| #58 | E14 | W5 | A+C | `feat/e14-f03-observability-slo-resilience` | L | #19, #21, #56 | Platform.Observability, infra/otel, tests/load | Telemetry redaction tests, load/soak/fault results vs NFRs, game-day records |
| #59 | E15 | W5 | C | `feat/e15-f01-environments-cicd-releases` | L | #22, #56, #58 | infra, .github | Provenance chain, failed migration/health rehearsal, permissions review |
| #60 | E15 | W5 | C | `feat/e15-f02-backups-pitr-dr-runbooks` | M | #57, #58, #59 | infra/backup, docs/runbooks | Automated restore test, PITR exercise, DR exercise with RPO/RTO |
| #61 | E15 | W5 | A+B+C | `feat/e15-f03-qa-uat-pilot-golive` (split: 61a test strategy and fixtures, 61b E2E suite, 61c UAT/training/pilot) | XL | #52, #56, #58, #59, #60 | tests, docs/uat, docs/training | Regression artefacts, dress rehearsal, UAT signatures, pilot report, go/no-go record |

---

## 8. Issue blueprints: E01–E07

Each blueprint gives the implementing session enough shape to plan the PR; it does not replace the issue's own
acceptance criteria, which remain the contract.

### #17 [E01-F01] Workflow maps, glossary, configurable taxonomy

- **Deliverables**: `docs/prd/00-overview.md`, `docs/prd/glossary.md`, `docs/prd/workflows/<category>.md` (Mermaid
  flowcharts for happy path and exceptions per category), `docs/prd/state-transitions.md` (table: transition,
  actor, preconditions, outputs, audit event, exception behaviour), `docs/prd/raci.md`,
  `docs/prd/configurable-vs-fixed.md`, `docs/prd/assumptions-and-open-decisions.md`.
- **Method**: Claude drafts from the issues and this plan; owner runs the workshops using the drafts as the
  agenda; decisions are recorded in the open-decisions file and folded back into the documents.
- **Exception catalogue to map**: duplicate customer, missing material, changed measurements, rejected QC,
  rework, late order, damaged label, cancelled order, refund, unpaid dispatch attempt, negative feedback.
- **Evidence**: one end-to-end walkthrough per category recorded in `docs/prd/walkthroughs.md`; requirement
  traceability table linking each original request to workflow and owning module.

### #18 [E01-F02] Modular architecture, ownership, data model, ADRs

- **Deliverables**: `docs/architecture/context.md`, `container.md`, `components.md`, `deployment.md`,
  `sequences/` (order confirmation, barcode handoff, invoice posting + payment, stock reservation + consumption),
  `docs/architecture/module-ownership.md` (Section 4.3 expanded), `docs/architecture/invariants.md`,
  `docs/architecture/conventions.md` (money, time, identifiers, concurrency, API versioning, migration
  compatibility), ADRs in `docs/adr/` using MADR format:
  ADR-0001 modular monolith and extraction criteria; ADR-0002 .NET 10 LTS and Minimal APIs; ADR-0003 React PWA;
  ADR-0004 PostgreSQL schema-per-module; ADR-0005 object storage and signed access; ADR-0006 BFF cookie session;
  ADR-0007 branch-aware single tenancy; ADR-0008 transactional outbox and background workers; ADR-0009 configurable
  taxonomy as versioned data; ADR-0010 deployment portability (containers, compose baseline, Kubernetes-ready);
  ADR-0011 reporting read models; ADR-0012 integration adapters and ports.
- **Architecture-test rule list** (implemented in #20): module `Domain` references only Platform.Abstractions;
  `Application` never references another module's `Infrastructure`; only `Contracts` cross modules; no `DbContext`
  of one module maps tables of another schema; hosts reference modules only through registration extensions.
- **Evidence**: review record of the four representative flows against the diagrams; failure-mode notes for
  database, object storage and worker outages.

### #19 [E01-F03] NFRs, SLOs, data policy, Definition of Done

- **Deliverables**: `docs/nfr/support-matrix.md` (devices, browsers, OS versions, orientations, camera/scanner/
  printer capabilities and fallbacks), `docs/nfr/capacity-and-performance.md` (users, branches, orders, scans,
  stock transactions, images, reports, concurrent billing; budgets for LCP/INP/CLS, API p95, bundle sizes),
  `docs/nfr/slo.md` (availability, latency, error rate, job lag, notification latency, RPO, RTO, backup retention,
  restore-test cadence), `docs/nfr/data-classification.md` (classes, purpose, consent, access, retention,
  backup treatment for DB, media, logs, audit, exports, backups), `docs/nfr/accessibility-localisation.md`,
  `docs/nfr/security-operations-targets.md` (patching and vulnerability SLAs, incident targets),
  `docs/process/definition-of-ready.md`, `definition-of-done.md`, `release-gates.md`, `waivers.md`,
  `docs/nfr/traceability.md` (NFR → test/monitor/evidence → owner).
- **Proposed starting targets** (to be confirmed): availability 99.5% monthly; API p95 < 400 ms for reads and
  < 800 ms for commands at the stated load; scan round-trip p95 < 1 s on 4G; outbox lag < 30 s p95; RPO ≤ 15 min
  (WAL archiving), RTO ≤ 4 h; backups retained 35 days plus monthly for 12 months; critical vulnerability fix
  ≤ 7 days, high ≤ 30 days.

### #20 [E02-F01] Scaffold solution, module boundaries, local environment

- **Deliverables**: solution per Section 4.2 with empty module skeletons and registration extensions; PWA
  skeleton (Vite, TypeScript strict, ESLint, Prettier, Vitest, Storybook placeholder); `infra/compose/`
  (`docker-compose.yml` for PostgreSQL 16, MinIO, ClamAV, MailHog/Mailpit, OpenTelemetry collector + Grafana
  stack optional); `.env.example` files with no secrets; `Tailor360.Cli` with `migrate`, `init-reference-data` (idempotent, production-safe: roles, permissions,
  default catalog, document sequences, an initial owner account created from a one-time secret) and `seed-synthetic`
  (development and test only; refuses unconditionally when `ASPNETCORE_ENVIRONMENT=Production`, with no override flag); health endpoints
  `/health/live`, `/health/ready`, `/health/startup`; `docs/dev/setup.md` (Windows/WSL, macOS, Linux),
  `troubleshooting.md`, `ports.md`, `commands.md`; one-command scripts (`./scripts/dev up|test|reset|run`).
- **Architecture tests** from #18's rule list, including a deliberately failing example kept as a negative test.
- **Evidence**: clean-clone build log from two environments; tests run twice; secret scan output.
- **Environment prerequisite**: Claude Code sessions need the .NET 10 SDK, Node 22 and Docker; see Section 12.

### #21 [E02-F02] Persistence conventions, migrations, outbox, configuration, flags

- **Platform.Persistence**: base `ModuleDbContext` applying conventions (snake_case, `timestamptz`, `numeric`
  money, `xmin` concurrency, audit columns, schema name); migration runner with advisory lock and startup
  validation (`pending migrations → refuse to serve`); `outbox_messages`, `inbox_messages`, `idempotency_keys`,
  `sequences`, `audit_events` (append-only, trigger-protected), `feature_flags`, `feature_flag_evaluations`
  (sampled audit).
- **Worker**: outbox dispatcher (`FOR UPDATE SKIP LOCKED`, batch, exponential backoff with jitter, max attempts,
  dead letter), inbox de-duplication for handlers, operator replay endpoint (`admin.outbox.replay` permission).
- **Configuration**: typed options with `ValidateDataAnnotations().ValidateOnStart()`; secret sources: environment
  variables and Docker/Kubernetes secrets; optional Vault/Key Vault provider behind an interface.
- **Flags**: organisation/branch scope, owner-only mutation with reason, cached evaluation with change
  notification, safe default off.
- **Seed**: two separate commands. `init-reference-data` creates the reference data every environment needs
  (roles, permissions, default catalog, sequences, initial owner) idempotently and is safe in production.
  `seed-synthetic` creates deterministic synthetic branches, users and sample business data with fixed IDs for
  development and automated tests only and refuses unconditionally in production (no override flag).
- **Health**: migration state, outbox backlog/age, failed jobs, flag store, database connectivity.
- **Tests**: outbox commit/rollback atomicity, duplicate delivery no double effect, poison message to dead letter
  and replay, concurrent update conflict, config missing → startup failure, migration from empty DB and from the
  previous release snapshot (snapshot stored under `tests/fixtures/db-snapshots/`).

### #22 [E02-F03] CI quality gates, repository governance, Claude Code workflow

- **`CLAUDE.md`** (concise): architecture summary, commands (`dotnet build/test`, `pnpm test`, `./scripts/dev`),
  module boundary rules, security rules (authorisation, validation, idempotency, audit, no secrets/PII in logs),
  testing rules, Git workflow (one issue/one branch/one PR, Conventional Commits with `Refs #NN`), Definition of
  Done; path-scoped rules in `src/Modules/CLAUDE.md`, `clients/pwa/CLAUDE.md`, `infra/CLAUDE.md`.
- **Templates**: issue templates (feature, bug, security), PR template mirroring the evidence checklist, ADR
  template, threat-model template, release evidence checklist; `CODEOWNERS`; branch protection documentation.
- **CI (`.github/workflows/ci.yml`)**: restore/build (.NET + pnpm), formatting (`dotnet format --verify-no-changes`,
  Prettier), lint (ESLint, analyzers as errors), unit/integration/architecture tests with Testcontainers, migration
  check (apply to empty DB + snapshot), PWA build and Vitest, dependency review, gitleaks, CodeQL, Trivy on images
  and IaC, SBOM (Syft) upload; least-privilege `permissions:` blocks; third-party actions pinned by SHA;
  Dependabot with grouped updates; security exception process documented.
- **Evidence**: runs on deliberately broken branches (format, test, architecture, secret, vulnerability); baseline
  CI duration and optimisation budget.

### #23 [E03-F01] Authentication, sessions, MFA, recovery

- **Identity module**: ASP.NET Core Identity user store in schema `identity`; password policy (length ≥ 12, breached
  password check via local k-anonymity list adapter optional); Argon2id hasher; TOTP authenticator enrolment with
  recovery codes; passkeys (WebAuthn) for phishing-resistant login; MFA required for Owner, Admin, Cashier and
  any role with `billing.*`/`admin.*` permissions (configurable per role).
- **Session/BFF**: cookie auth with server-side ticket store (`identity.sessions`: id, user, device label, IP,
  user agent, created, last seen, absolute expiry, revoked); rotation on login, MFA and role change; sliding
  inactivity timeout (default 30 min) and absolute timeout (default 12 h); anti-forgery token endpoint and
  header validation; `SameSite=Lax; Secure; HttpOnly`; security headers baseline.
- **Abuse controls**: per-account and per-IP throttling, progressive lockout, generic error messages, optional
  CAPTCHA adapter, anti-enumeration on recovery (same response timing and message).
- **Recovery**: email-based verified reset with single-use expiring token; admin-initiated reset requires step-up.
- **Audit**: login success/failure, MFA enrol/challenge, recovery, session create/revoke, suspicious activity.
- **Endpoints**: `POST /api/v1/auth/login`, `/mfa/challenge`, `/passkeys/*`, `/logout`, `/logout-all`,
  `GET /me`, `GET/DELETE /sessions`, `POST /recovery/request`, `/recovery/confirm`, `GET /antiforgery`.
- **Tests**: session lifecycle, lockout, CSRF rejection, fixation (new session id after login), replay after
  revocation (denied within SLO), enumeration timing; Playwright browser tests for cookies/headers/logout.

### #24 [E03-F02] RBAC, branch scopes, authorisation regression tests

- **Permission catalogue** in `Platform.Security` as a strongly typed list grouped by module; roles Owner, Admin,
  Reception, Measurement Staff, Tailor Master, Tailor, Inventory, Cashier, Delivery, Auditor with default
  grants in seed data and an admin-editable role → permission map (custom roles allowed).
- **Policies**: `PermissionRequirement`, `BranchScopeRequirement` (user branch assignments vs resource branch),
  `ResourceOwnershipRequirement` (tailor sees assigned jobs only); endpoint filters that load the resource's
  branch before the handler runs; deny-by-default for every endpoint (an architecture test fails if an endpoint
  lacks an explicit policy or `AllowAnonymous` with justification attribute).
- **Background context**: `SystemPrincipal` with explicit scope for workers/exports; media and download endpoints
  re-check authorisation on every request.
- **Regression suite**: generated matrix test (role × endpoint × own-branch/other-branch) from the OpenAPI
  document; IDOR tests with foreign identifiers; export and job authorisation tests; audit of denied privileged
  actions with sampling to avoid log floods.

### #25 [E03-F03] Audited administration for users, branches, permissions, flags

- **Screens (desktop-first, tablet-capable)**: users (invite/create, activate, suspend, branch assignment, role
  assignment, MFA status, revoke sessions), branches (code, name, timezone, address, GST registration reference,
  contact, status, data-scope rules), roles/permissions, feature flags and module/menu toggles (super-user only),
  audit history drawer (before/after, actor, reason, correlation ID, effective time).
- **Controls**: step-up (re-authenticate with MFA within 5 minutes) for role/permission/billing/security changes;
  mandatory reason; dual confirmation for Owner-level changes; no deletion of referenced identities/branches
  (deactivate); safe search/filter/pagination; export restricted to Auditor/Owner.
- **Tests**: authorisation for every admin action, concurrent role/flag updates (optimistic concurrency),
  suspension revokes sessions immediately, historical references stable after deactivation; UAT script for
  onboarding, transfer, suspension, emergency revocation.

### #26 [E04-F01] Customer profiles, consent, search, deduplication, timeline

- **Model** (`customers` schema): `customers` (customer_number `C-<branch>-000001`, name, normalised_name,
  primary_phone, alternate_phone, phone_normalised (E.164), email, address, language, preferences, notes,
  status, visibility branches), `consent_records` (purpose, version, source, granted/withdrawn, actor, time),
  `duplicate_candidates` (score, reasons, decision), `customer_merges` (survivor, merged, actor, reason).
- **Search**: exact (phone, customer number) and fuzzy (trigram on normalised name, phonetic) with permission
  filtering; duplicate scoring at create time (phone match, name similarity, address similarity) with explanation;
  merge requires `customers.merge` permission and keeps source IDs as aliases.
- **Timeline**: read model composed from module contracts (measurements, orders, invoices, payments, delivery,
  feedback, alterations, consent, notes) with permission-aware entries.
- **Screens**: search-first "Find or create customer" (phone keypad on mobile), create/edit with concurrency
  handling, detail with timeline tabs (tablet master-detail).
- **Tests**: normalisation/validation/duplicate scoring unit tests; merge, concurrency, branch scope, timeline
  ordering integration tests; phone/tablet UAT screenshots.

### #27 [E04-F02] Configurable measurement-template administration

- **Model**: `measurement_templates` (category/service link, name), `template_versions` (status draft/published/
  retired, effective dates, reason, actor), `template_fields` (key, label, group, display order, canonical unit
  millimetre, display units allowed, precision, required, min/max/warning ranges, conditional rules expressed as a
  small JSON rule language, help text, diagram media reference).
- **Rules**: published versions immutable; clone to draft; retirement blocked while active orders reference it
  unless a successor exists; preview and test-data validation before publish; audit with reason.
- **Seed**: initial templates for Blouse (Pattern, Aari work), Salwar, Lehenga, Gown, Kids with the field sets
  proposed in `docs/prd/measurement-templates.md` (drafted from the scaffold's category configuration: length,
  chest, waist, shoulder, armhole, neck depths, sleeve length/round, apex, hip, bottom lengths, skirt length,
  flare, and so on) for business review.
- **Tests**: property tests for cm/inch conversion and rounding round-trips; publish/retire/historical-render
  integration tests; admin authorisation tests.

### #28 [E04-F03] Responsive measurement capture, validation, versioning, reuse

- **Flow**: choose customer → choose category/service → wizard groups (from template) with progress, large numeric
  inputs (`inputmode="decimal"`), unit toggle, diagrams, inline warnings, accessible error summary; autosaved
  draft (server-side `measurement_drafts` + local encrypted copy); review with previous-version comparison
  (field-by-field diff, source date/version shown); confirm creates immutable `measurement_versions` row.
- **Server rules**: template version pinned at draft creation; if the template is republished mid-capture the
  draft shows a migration prompt; required/range/conditional validation server-authoritative; correction = new
  version with reason; concurrency on draft with `If-Match`.
- **Outputs**: `MeasurementVersionConfirmed` event; printable/view-only measurement sheet for authorised tailors
  (no unrelated customer data; access audited).
- **Tests**: validation and snapshot invariants; concurrent confirmation (only one version created); template
  change during capture; interrupted capture recovery without duplicates; real-device usability notes with the
  longest template.

### #29 [E05-F01] Configurable stitching-category and service catalog

- **Model** (`catalog` schema): `categories` (code, name, parent, display order, active dates, branch
  availability, feature flag), `service_types` (category, code, name, description, expected duration, links to
  measurement template, workflow definition, design option groups, price list item), `catalog_versions`
  (draft/published/retired snapshot of the hierarchy used by orders).
- **Rules**: no cycles, unique codes, dependency validation before publish, retirement blocked with in-progress
  orders, cache with version-based invalidation, permission-aware read API (`GET /catalog/current`).
- **Seed**: Blouse → Pattern, Aari work; Salwar; Lehenga; Gown; Kids.
- **Screens**: admin tree editor with preview and branch availability; demonstration "add a category without a
  deployment" recorded as evidence.
- **Tests**: hierarchy/lifecycle, cache invalidation, concurrent publish.

### #30 [E05-F02] Visual shape and design catalog with garment-level selections

- **Model**: `design_option_groups` (category links, name, selection mode single/multiple, required, display
  order), `design_options` (label, illustration media, help text, price impact, time impact, active), `design_rules`
  (requires/excludes/conditional-note between options and categories), versioned with the catalog version.
- **Snapshot contract** (consumed by Orders): `GarmentDesignSnapshot` { catalog version, selected options with
  labels and illustration references, customer notes, tailor instructions, approvals, revision number }.
- **Screens**: responsive visual picker (thumbnail grid, zoom, selection summary, validation), job-card view with
  printable fallback; revision flow with reason and price/due-date impact review.
- **Tests**: rule-engine unit/property tests, snapshot immutability after catalog change, concurrent revision
  tests, tablet usability review.

### #31 [E05-F03] Secure customer-material and reference-image lifecycle

- **Model** (`media` schema): `media_objects` (purpose MATERIAL/REFERENCE/DIAGRAM/QC_EVIDENCE/DELIVERY_EVIDENCE,
  owner branch, links customer/order/job, classification, consent reference, retention date, status
  uploading/quarantined/ready/rejected/deleted, checksum, size, mime, dimensions), `media_derivatives`,
  `media_access_log`, `retention_holds`.
- **Pipeline** per Section 4.4: allowlist (JPEG, PNG, WebP, HEIC → converted), decoded signature validation,
  quarantine bucket, ClamAV scan, metadata strip and re-encode, thumbnails, ready; random object keys; signed URLs
  (≤ 5 min) issued only after policy check; no PII in filenames.
- **Client**: camera capture and gallery upload with progress, retry, cancel, preview, caption; deletion
  request flow.
- **Operations**: retention job, legal/business hold, orphan cleanup, storage health metrics; backup treatment
  documented.
- **Tests**: adversarial corpus (polyglot, spoofed extension, oversized, EICAR), URL copy after expiry/revocation
  denied, EXIF removed, tailor cross-job access denied, outage/retry/orphan cleanup, large-image performance on
  device.

### #32 [E06-F01] Estimates, multi-garment orders, immutable job cards (split 32a/32b)

- **Model** (`orders` schema): `estimates`, `orders` (order_number `O-<branch>-<FY>-000001`, customer, branch,
  status draft/confirmed/in_production/ready/delivered/closed/cancelled, due date, priority, notes, totals,
  confirmed_at, idempotency), `garment_jobs` (job_number `J-…`, order, category/service version, measurement
  version, design snapshot, media links, price snapshot, due date, priority, lifecycle state), `order_revisions`.
- **Confirmation**: single transaction: validate catalog/template/workflow availability and required evidence
  (measurements, consents), snapshot inputs, allocate numbers, create job cards, write outbox events
  (`OrderConfirmed`, `GarmentJobCreated`) consumed by Custody (barcode identity), Orders workflow (instantiate),
  Inventory (reservations when configured). `Idempotency-Key` mandatory.
- **32b intake UI**: select/create customer → add garments (category, service, design, measurements capture or
  reuse, media, notes) → dates/priority → totals via pricing engine (#41) → validation summary → estimate
  print/share → confirm. Autosave drafts with expiry.
- **Tests**: confirmation rollback and duplicate request, snapshot regression after catalog/measurement change,
  role/branch restrictions on search/view/edit/print/export, phone/tablet UAT.

### #33 [E06-F02] Configurable production workflow, assignment, Tailor workboard

- **Model**: `workflow_definitions` → `workflow_versions` (phases with code, name, required roles, evidence,
  duration/SLA, optional/skippable, transitions graph, category mapping), `job_phases` (instantiated per job with
  server timestamps), `assignments` (job, phase, assignee, team, reason, history).
- **Seeded default workflow**: Intake → Cutting → Specialist work (Aari, conditional) → Stitching → Finishing →
  QC → Ready for delivery; exception states Rework, On hold.
- **Rules**: published graph validated (reachable end, no dead ends); one immutable workflow version per active
  job; transitions authorised by permission + role eligibility; concurrency via job version; events for queues,
  alerts, reports, inventory consumption.
- **Screens**: phone scanner-first work queue, tablet master-detail workboard, desktop planning board; filters
  assigned/unassigned/overdue/priority/blocked/due-soon; workload view per Tailor Master.
- **Tests**: graph property tests, invalid transitions, concurrent scans cannot double-complete, reassignment
  history, queue reconciliation and response-time targets.

### #34 [E06-F03] QC, rework, alteration, hold, cancellation, completion controls

- **Model**: `qc_checklist_templates` (per category, criteria, fit checks), `qc_results` (immutable, defects,
  evidence media, actor), `rework_tasks`, `alteration_requests` (before/after delivery, original job link, reason,
  changed measurement/design versions, price/due decisions, communication status), `holds`, `cancellations`.
- **Ready-for-delivery gate**: derived server-side from workflow complete + QC passed + custody reconciled +
  documentation; never set directly by clients.
- **Policies**: cancellation blocked in prohibited financial/stock/custody states (compensating flows required);
  hold/resume/reschedule/reopen with reason and approval.
- **Dashboards**: failed QC, repeated rework, overdue holds, pending alterations, cancelled jobs.
- **Tests**: state-machine tests for every exceptional path; integration with inventory/billing/notification
  stubs; UAT for QC failure, alteration and cancellation.

### #35 [E07-F01] Barcode identifiers, labels, reprint control

- **Model** (`custody` schema): `barcode_identities` (namespace, payload, entity type/id, status active/
  invalidated/superseded, created, reason), `label_prints` (identity, template version, printer/format, actor,
  time, reason, batch).
- **Format**: D9; check character validated client- and server-side; Code 128 primary, QR optional carrying the
  same payload; label templates (thermal 50×30 mm, A4 sheet) with job number, category cue, due cue, branch code,
  print version; PDF fallback; quiet-zone and resolution validation.
- **Controls**: generation on job confirmation (event handler), single/batch print with `custody.print_label`
  permission, reprint with reason invalidates the previous identity (old label resolves to "superseded"), audit.
- **Tests**: collision/property tests, checksum tests, reprint concurrency, authorisation; physical scan sheet
  results across devices and printers.

### #36 [E07-F02] Camera, hardware-scanner and manual-entry scanning experience

- **PWA**: `ScannerSource` abstraction (Section 4.4); camera flow (permission handling, rear camera, torch where
  supported, scan region overlay, haptic/audio feedback, debounce window, release on navigation); keyboard-wedge
  listener scoped to the scan screen; manual entry with reason (audited); confirmation card (job number, current
  state, expected action) before committing sensitive transitions; error handling for unknown/inactive/
  superseded/malformed/wrong-namespace codes.
- **Telemetry**: decode latency, failures, permission denials, fallback usage (no frames recorded).
- **Tests**: parser/normalisation/debounce unit tests; Playwright with injected scan events; real-device matrix
  results; accessibility and one-handed review.

### #37 [E07-F03] Custody transfers, phase scans, idempotency, reconciliation

- **Model**: `scan_events` (immutable: event id, idempotency key, job, action, from/to custodian (user/team/
  location), actor, device, branch, client time, server time, correlation, note, exception reason),
  `custody_transfers` (pending → accepted/rejected/expired), `reconciliation_cases`.
- **Rules**: explicit transfer-out and receive; phase/QC/ready/delivery-receipt/dispatch scans integrate with the
  workflow state machine; validation of expected custodian, location, workflow/QC/payment prerequisites and role;
  stale/conflicting events rejected with problem details; duplicates return the original outcome; dispatch scan
  requires QC passed + delivery custody + payment rule (from Billing contract).
- **Client**: bounded encrypted local retry queue for scan submissions only, with conflict surfacing.
- **Screens**: pending transfers, overdue acceptance, mismatches, timeline and search.
- **Tests**: state/property tests, concurrency/idempotency/offline replay, physical rehearsal from intake to
  dispatch.

---

## 9. Issue blueprints: E08–E15

### #38 [E08-F01] Inventory items, units, suppliers, locations, reorder rules

- **Model** (`inventory` schema): `stock_items` (sku, barcode `S-…` or supplier EAN, name, category, base unit,
  purchase/issue units with conversion factors, tracking method none/lot, tax metadata (HSN, GST rate reference),
  cost method reference, status, branch/location availability), `units`, `unit_conversions`, `suppliers`,
  `locations` (warehouse/store/bin, branch), `reorder_rules` (item × location: minimum, reorder point, target
  quantity, lead time, responsible role), `customer_material_custody` (order/job link, description, quantity,
  received/returned, never valued).
- **Rules**: unique SKU/barcode within scope, conversion graph acyclic and invertible, retire instead of delete,
  retired items visible in history but not purchasable/issuable.
- **Screens**: item admin with bulk CSV import preview and error report; supplier and location admin; reorder rule
  editor.
- **Tests**: unit-conversion property tests; import validation, concurrency, deactivation integration tests;
  initial catalogue business review.

### #39 [E08-F02] Immutable stock ledger for purchasing, reservation, consumption

- **Model**: `ledger_entries` (immutable, append-only: item, location, type opening/purchase_receipt/reservation/
  release/issue/consumption/return/transfer_out/transfer_in/wastage/adjustment, quantity in base unit signed,
  unit cost, references job/phase/purchase/stocktake, actor, reason, idempotency key, correlation), `balances`
  (materialised per item × location: on hand, reserved, available, in transit; rebuildable), `purchase_orders`,
  `purchase_receipts` (supplier, lines, cost, tax reference, lot, evidence), `reservations`.
- **Rules**: transfers post balanced pairs in one transaction; negative-stock policy configurable (block/allow with
  approval); reservation uses row-level locking (`SELECT … FOR UPDATE` on balance) to prevent oversubscription;
  corrections by compensating entries only (database trigger blocks UPDATE/DELETE on the ledger).
- **Integration**: consumption recorded against garment job and phase from the workboard; `StockConsumed` event.
- **Tests**: balance rebuild equals materialised balances (property test); high-concurrency reservation; duplicate
  requests; half-posted transfer impossible (transaction failure test); end-to-end purchase → reserve → consume →
  return → correct.

### #40 [E08-F03] Low-stock alerts, stocktake, variance approval, valuation reports

- **Alerts**: worker evaluates `available + open purchase quantity` against reorder rules on ledger events and
  hourly; alert state machine (raised → acknowledged/snoozed → escalated → cleared) deduplicated per item ×
  location; routed to Inventory role for the branch via Notifications; cleared automatically on replenishment.
- **Stocktake**: sessions per branch/location with freeze policy, count sheets and mobile entry (scanner-first),
  recount, variance explanation, approval by a different user above a threshold, posting as ledger adjustments
  linked to session evidence.
- **Reports**: on hand, available/reserved, movement, purchase, consumption, wastage, aging, low stock,
  replenishment; valuation using configurable method (weighted average default; FIFO optional) with immutable
  source costs and documented treatment of returns/adjustments; filters, exports, freshness indicator.
- **Tests**: threshold boundary and dedup; stocktake concurrency/approval/adjustment; report reconciliation at a
  cut-off; large-dataset performance.

### #41 [E09-F01] Configurable pricing, discounts, GST calculation engine

- **Model** (`billing` schema): `price_lists` → `price_list_versions` (effective dates, branch availability,
  items: service/product reference, base rate, inclusive/exclusive flag, allowed discount rules, surcharges for
  design/material/labour, approval thresholds), `gst_registrations` (branch, GSTIN, state code),
  `tax_configuration_versions` (tax codes, HSN/SAC, component rates CGST/SGST/IGST/cess, effective dates,
  place-of-supply rules), `calculation_snapshots`.
- **Engine**: pure, deterministic `PricingService.Calculate(request) → CalculationResult` with line components,
  document totals, rounding allocation and the configuration versions used; decimal arithmetic; rounding per
  D10; inclusive and exclusive pricing; intra- vs inter-state by place of supply; manual override requires
  `billing.override_price` permission, reason and audit and reports variance from catalogue.
- **Admin**: price/tax version editor with preview and test cases before publish; validation of missing tax
  configuration before order/invoice confirmation.
- **Tests**: accountant-supplied golden master (stored as JSON fixtures under `tests/fixtures/billing/`);
  property tests for rounding/allocation invariants; concurrent publish and historical reproduction.

### #42 [E09-F02] Estimates, GST invoices, numbering, PDFs, financial immutability

- **Model**: `invoices` (status draft/posted/cancelled, branch, financial year, number, customer snapshot,
  GST registration, place of supply, totals, tax components, source order/jobs, posted_at, hash of the posted
  artefact), `invoice_lines`, `invoice_tax_components`, `credit_notes`, `debit_notes`, `document_sequences`
  (branch × document type × financial year), `document_artifacts` (PDF version, checksum, storage key).
- **Rules**: draft editable; posting allocates the next number under a row lock in the same transaction and freezes
  content (database trigger blocks UPDATE on posted rows except status transitions defined by policy);
  cancellation/void policy and credit/debit notes as compensating documents; deletion prohibited when referenced.
- **PDF**: accessible invoice (tagged PDF where the renderer supports it) with business/customer details, lines,
  discounts, GST components, totals, balance, terms, human-readable reference and an `I-…` barcode/QR; print view
  in the PWA; download/print/email audited.
- **Lookup**: `GET /api/v1/billing/barcodes/{payload}` resolves under authorisation only.
- **Tests**: concurrent posting (no duplicate or skipped numbers), duplicate-post idempotency, immutability, PDF
  snapshot (Verify) and accessibility check, barcode retrieval authorisation; accountant review of samples.

### #43 [E09-F03] Advances, payments, receipts, cashier reconciliation, dispatch gate

- **Model**: `payments` (mode cash/card/UPI/bank/other, amount, external reference, status, payer, branch,
  cashier, idempotency key, provider reference), `payment_allocations` (payment → invoice), `advances` (unapplied
  amounts linked to customer/order), `refunds`/`reversals` (compensating, approval), `receipts` (numbered, `R-…`
  barcode), `cashier_sessions` (open/close, expected vs counted, variance reason/approval).
- **Rules**: deterministic allocation (oldest invoice first) with authorised manual allocation; duplicate
  requests/callbacks idempotent by key and provider reference; balance formula per Section 4.5; paid status
  contract `IPaymentStatusQuery.GetDispatchEligibility(orderId)` consumed by Custody and Delivery; configurable
  dispatch rule (full payment default; partial threshold or approved exception with reason and audit).
- **Screens**: take payment (phone/tablet), allocate advances, receipt print/share, cashier open/close and
  reconciliation, outstanding balances.
- **Tests**: allocation/balance property tests; concurrent payment, idempotency, reversal integration tests;
  cashier close and unpaid/paid dispatch end-to-end UAT.

### #44 [E10-F01] Reconciled sales, GST, payment and receivables reporting

- **Read models** (`reporting` schema): `sales_daily`, `invoice_register`, `gst_summary` (from persisted tax
  snapshots), `payments_register`, `receivables_aging`, `cashier_variance`, fed by outbox events with rebuild
  command and freshness timestamp; metric dictionary in `docs/reports/metrics.md` (definitions, cut-off and
  timezone rules, treatment of cancelled/voided/credited invoices, advances, partial payments).
- **Screens**: sales dashboard (daily/monthly/custom, by branch/category/cashier/payment mode/customer segment
  where permitted), invoice register, GST summary, payments/advances/refunds, outstanding and aging with
  drill-through to authorised documents; exports (PDF/CSV/XLSX) with generated-at, filters, totals and
  classification watermark; large ranges run asynchronously with secure delivery.
- **Reconciliation job**: compares report totals to transactional sources; alerts on mismatch/freshness breach.
- **Tests**: golden-data reconciliation; authorisation and export leak tests; timezone boundary; volume.

### #45 [E10-F02] Order pipeline, tailor workload, turnaround, quality analytics

- **Projections** from order/workflow/custody/QC events: pipeline by branch/category/phase, queue age, promised vs
  actual turnaround, overdue/blocked, assignment load, phase durations, QC pass/fail reasons, rework cycles,
  alteration rate; exclusions/denominators/held-time rules documented.
- **Screens**: owner/manager pipeline dashboard, Tailor Master workload/capacity, quality dashboard with context
  (no misleading staff ranking), drill-through to authorised job timelines; active vs historical distinction.
- **Operations**: projection lag/mismatch alerts; rebuild reproduces live results.
- **Tests**: replay/idempotency, reconciliation to source jobs, performance with production-sized history, ops
  UAT scenarios (overdue, hold, QC fail, reassignment).

### #46 [E10-F03] Inventory, wastage and estimated-profitability analytics with governed exports

- **Reports**: on hand/available/reserved/in transit, purchase, consumption, return, wastage, stocktake variance,
  aging, reorder status by branch/location/item/category at a cut-off with valuation method/version; estimated
  job profitability (invoiced/quoted revenue − allocated material cost − configured labour/overhead − discounts/
  credits − wastage) labelled "estimated" with assumptions shown.
- **Export service**: asynchronous jobs with encrypted temporary files, short expiry, authorised download,
  deletion; field-level export policy per role/branch; audit of actor, filters, row count, classification.
- **Tests**: ledger/valuation/profitability reconciliation; export authorisation, expiry and cleanup; load tests.

### #47 [E11-F01] Notification templates, consent, delivery tracking, provider adapters

- **Model** (`notifications` schema): `notification_intents` (event, audience, priority, template version,
  locale, channel preference, consent requirement, quiet hours, fallback policy, dedup key), `templates` →
  `template_versions` (channel, declared variables allowlist, body with safe rendering, status),
  `deliveries` (queued/accepted/delivered/failed/bounced/suppressed/acknowledged, provider reference, attempts),
  `in_app_notifications`.
- **Service**: outbox-driven, idempotent, retry/backoff, rate limits, dedup, dead letter, operator replay; consent
  and preference checks server-side; transactional messages allowed per policy; role/branch routing for low
  stock, overdue jobs, pending transfers, payment/delivery and feedback follow-up; in-app centre in the PWA.
- **Adapters** (Integration module): `IEmailSender`, `ISmsSender`, `IWhatsAppSender`, `IPushSender` with fakes;
  provider payload mapping and credentials outside domain modules.
- **Tests**: adapter contract and retry/idempotency; consent, quiet hours, dedup, fallback; provider-down drill.

### #48 [E11-F02] Payment-cleared delivery queue, customer status links, dispatch confirmation

- **Queue**: jobs past the ready-for-delivery gate grouped by order with due/ready age, balance, exceptions;
  delivery-team receive scan establishes custody; server-side dispatch eligibility (QC passed, custody, active
  order, holds resolved, payment rule from #43); partial readiness/delivery policy per configuration.
- **Dispatch**: scan with actor/time/branch, recipient confirmation appropriate to policy (name + OTP or signature
  without over-collection), receipt reference, optional evidence; idempotent; failed/returned delivery restores
  custody through compensating events; disputed handoff and lost-link reissue flows.
- **Customer status link**: random 128-bit token, purpose-bound, expiring, revocable, rate-limited, shows minimal
  progress and configured amount display; no enumerable identifiers.
- **Tests**: unpaid/paid/partial E2E; token enumeration/expiry/authorisation/rate-limit; delivery mobile UAT.

### #49 [E11-F03] Stitching feedback, alteration requests, service-recovery workflow

- **Flow**: delivery confirmation → feedback invitation (consent/channel policy) with single-purpose expiring
  token → short accessible form (overall, fit, stitching quality, design match, timeliness, comments, contact/
  alteration request) → optional one-time edit window → low rating or alteration request creates exactly one
  `service_recovery_cases` row (owner, due date, status, contact attempts, resolution, revised job link) with
  staff notification and escalation on overdue.
- **Restrictions**: feedback never mutates measurements/design/job history; free text visibility limited by
  role/branch; reporting on rating, response rate, themes, alteration rate, resolution time.
- **Tests**: token expiry/replay/rate limit; follow-up idempotency, assignment, escalation; journey UAT.

### #50 [E12-F01] Responsive design system and role-optimised layouts

- **Tokens**: colour (light/dark/high-contrast ready), type scale, spacing, elevation, motion (reduced-motion
  aware), focus rings, density, breakpoints/container queries.
- **Components**: navigation (bottom bar, side nav, tabs), forms (inputs, numeric measurement input, selects,
  toggles, date), camera/scanner, tables and cards (responsive switch), filters, drawers/dialogs, timeline,
  status badges, alerts/toasts, empty/error/loading, confirmation (with typed confirmation for destructive acts).
- **Layouts**: phone (bottom navigation, scanner-first floating action), tablet (master-detail for measurements,
  queues, job cards, inventory, billing), desktop (side navigation, dense tables/dashboards); safe areas, virtual
  keyboard handling, 200% zoom, variable text size, 44 px touch targets; no hover-only information.
- **Tooling**: Storybook with axe and visual regression (Playwright screenshots) baselines.
- **Tests**: automated component accessibility; device/orientation/keyboard/zoom matrix; role walkthroughs.

### #51 [E12-F02] Installable PWA, safe updates, bounded network resilience

- **Manifest** (icons, theme, start URL, display, screenshots, categories) and install guidance per platform;
  service worker (Workbox) with versioned precache, network-first API, explicit deny-list for protected
  responses; update detection with user-safe activation and `/api/version` compatibility check (minimum client
  version); cache migration/cleanup.
- **Drafts**: encrypted IndexedDB drafts bound to user/branch, expiring, cleared on logout/revocation.
- **Offline queue**: only approved idempotent operations (scan submissions); billing posting, payment
  reconciliation and prohibited transitions remain online-only; sync state, pending count, conflicts and recovery
  actions visible; background/foreground resume handling.
- **Tests**: install/update/rollback on Android, iPhone/iPad, desktop; offline/reconnect/duplicate/conflict E2E;
  cache/privacy inspection; quota exhaustion.

### #52 [E12-F03] WCAG accessibility, cross-browser fallbacks, mobile performance budgets

- **Support contract** in `docs/nfr/support-matrix.md`: latest two stable Chrome, Edge, Firefox, Safari; approved
  iOS/iPadOS Safari, Android Chrome, Samsung Internet; graceful behaviour outside it.
- **Automation**: Playwright projects (Chromium, Firefox, WebKit × phone/tablet/desktop × portrait/landscape) with
  deterministic synthetic data over the critical paths (customer search, measurement capture, design/media, order
  confirmation, barcode fallback, production/QC, inventory, billing, delivery, feedback); axe on every page;
  Lighthouse CI budgets (JS/CSS/image sizes, LCP, INP, CLS) on a throttled mobile profile.
- **Fallbacks**: capability detection for camera, barcode decoding, push, clipboard/share, printing, file capture,
  install prompts with documented alternatives.
- **Manual**: screen reader (VoiceOver, TalkBack, NVDA), keyboard, zoom/reflow, real-device profiling.

### #53 [E13-F01] Versioned API, BFF, OpenAPI, idempotency standards

- **Standards document** `docs/api/conventions.md`: same-origin BFF boundary; resource/command conventions; URI
  versioning `/api/v1`; deprecation policy (`Deprecation`/`Sunset` headers); HTTP semantics; problem details;
  cursor pagination, filtering, sorting, sparse fields; timezone and money formats; correlation IDs; request
  size/time limits; rate limits; cancellation.
- **Implementation**: OpenAPI generation with examples and security schemes; `oasdiff` breaking-change check in
  CI; consumer contract tests for the PWA client; idempotency middleware for the command allowlist; concurrency
  tokens (`ETag`/`If-Match`) across editable aggregates; policy-based authorisation audit of every endpoint.
- **Tests**: OpenAPI lint/diff; idempotency/concurrency/timeout; authorisation, rate limit, error-leak.

### #54 [E13-F02] Transactional integration events and signed webhook delivery

- **Model** (`integration` schema): `integration_events` (envelope: id, type/version, occurred at, organisation/
  branch, aggregate ref, correlation/causation, classification, minimal payload), `webhook_subscriptions`
  (endpoint verified by challenge, allowlisted events, branch scope, status, secret with rotation),
  `webhook_deliveries` (attempt, response class, next retry, state delivered/failed/dead-lettered).
- **Worker**: dispatch from outbox with retry/backoff/jitter, per-endpoint concurrency limits and circuit breaker;
  HMAC-SHA256 signature with timestamp header and documented replay window; SSRF protection (deny private
  ranges, DNS re-resolution check); redacted diagnostics; fake receiver for tests.
- **Tests**: commit/rollback/duplicate/ordering/replay; signature, rotation, SSRF, rate; slow/unavailable
  receivers.

### #55 [E13-F03] Replaceable payment, messaging, accounting and printing adapters

- **Ports**: `IPaymentGateway` (initiate, status, refund, callback verification), messaging senders (#47),
  `IAccountingExporter` (versioned mapping, batch identity, balanced totals, correction and re-export policy,
  Tally XML first), `IBarcodeRenderer`, `IPdfRenderer`, `IPrintBridge` (PDF hand-off or network/local bridge).
- **Adapters**: deterministic fakes/sandboxes for dev/test/UAT; real adapters (D20) with configuration/secret
  validation, capability discovery, timeouts, retries, idempotency, correlation, circuit breakers; provider
  errors normalised; sandbox mode cannot be enabled in production (startup guard).
- **Payment callbacks**: signature/source verification, replay window, amount/currency/reference match,
  idempotent reconciliation to payment records; reconciliation report of missing/duplicate/mismatched records.
- **Tests**: adapter contract suite gating enablement by feature flag; fault injection; callback security;
  accounting balanced totals and re-export.

### #56 [E14-F01] Threat modelling and ASVS-aligned security baseline

- **Documents**: `docs/security/threat-models/*.md` (DFDs and STRIDE per flow: auth, customer/measurement/media,
  order/workflow, custody, inventory, billing/payment, reports/exports, customer links, integrations,
  deployment), `docs/security/abuse-cases.md`, `docs/security/asvs-traceability.md` (ASVS L2 + selected L3),
  `docs/security/vulnerability-management.md` (triage, SLAs, disclosure, exceptions with expiry).
- **Controls**: central validation/encoding, security headers and CSP (nonce-based, no inline scripts), CSRF,
  rate/size limits, dependency protections, business-state authorisation checks (no client-driven state),
  upload hardening (#31) verified, secrets scanning; CI gates from #22 extended with severity SLAs.
- **Testing**: automated security regression suite (IDOR, escalation, workflow bypass via direct API, barcode
  replay, invoice/payment tampering, stock manipulation, malicious upload, export leakage, SSRF, credential
  abuse, DoS limits); independent penetration test before production and after material auth/payment/upload
  changes; remediation verified.

### #57 [E14-F02] Privacy lifecycle, immutable audit, encryption, secrets management

- **Data inventory** `docs/privacy/data-inventory.md` mapping each class (identity, contact, measurements, images,
  design, order, barcode, stock, financial, feedback, logs, audit, exports, backups) to owner, access policy,
  retention, backup behaviour; consent/purpose/source/version recording (Customers module); correction, export,
  restriction/deactivation, approved deletion workflows with legal/business holds.
- **Audit**: append-only schema (actor, subject, action, resource, before/after summary or safe diff, branch,
  time, reason, correlation, source, outcome), trigger-protected, daily hash chain, restricted viewer/export,
  retention independent of logs, gap detection job.
- **Encryption and secrets**: TLS everywhere, encrypted volumes/object storage, field-level protection where the
  threat model requires (e.g., recovery codes, provider secrets) with documented key ownership; secrets via
  environment/secret manager with rotation runbook and emergency revocation; verification tests for logout/
  revocation, export expiry, media deletion, link revocation, rotation.

### #58 [E14-F03] Observability, SLO alerting, performance resilience, incident response

- **Instrumentation**: OpenTelemetry across web, worker, database, outbox, media, scans, inventory, billing,
  reports, notifications, integrations; correlation/causation propagation; redaction processors.
- **Signals**: latency/errors/saturation, failed jobs, outbox lag, scan conflicts, stock reconciliation mismatch,
  numbering/payment mismatch, notification failures, backup age, report freshness; dashboards per role;
  alerts tied to SLOs and runbooks with grouping/dedup/severity.
- **Resilience**: timeouts, bounded retries with backoff, circuit breakers, bulkheads, queue limits, cancellation,
  safe degradation (e.g., scanning continues if notifications are down); load/stress/soak tests (k6) for intake,
  scan bursts, concurrent reservation/invoice/payment, uploads, large reports.
- **Incident process**: severity, ownership, escalation, communication, evidence preservation, recovery,
  post-incident review; game days (database slowdown, object storage outage, provider outage, stuck outbox,
  duplicate callback, failed deployment).

### #59 [E15-F01] Hardened environments, CI/CD, versioning, safe database releases

- **Images**: minimal, non-root, pinned, scanned, read-only filesystem, dropped capabilities; SBOM, provenance
  (SLSA-style attestation) and signature (cosign); build once, promote the same digest.
- **Environments**: dev/test/staging/production configuration as code (Terraform for cloud, Ansible for on-prem)
  with network boundaries, TLS, DNS, secrets, storage, database, observability; staging verification and manual
  approval gates; separation of deployment duties; protected production credentials.
- **Releases**: semantic versioning, build metadata, release notes, compatibility matrix (PWA/API/DB/worker);
  expand-migrate-contract with preflight checks, migration locks, pre-migration backup, monitored post-deploy
  verification; rolling or blue-green with readiness/liveness/startup probes, automatic halt and documented
  rollback/roll-forward.
- **Evidence**: provenance chain for a tagged artefact; failed-migration and failed-health-check rehearsals.

### #60 [E15-F02] Encrypted backups, PITR, disaster recovery, runbooks

- **Backups**: pgBackRest/WAL-G base + continuous WAL to encrypted object storage with separate credentials and
  immutability/retention; MinIO/S3 versioning and replication for media; configuration/keys/audit included in
  scope; off-site copies; monitoring of age/completeness/failures with alerts.
- **Restore automation**: scheduled restore into an isolated environment validating integrity, migration
  version, business invariants (ledger balances, invoice sequences) and media references; PITR to a chosen
  timestamp.
- **Runbooks** `docs/runbooks/`: accidental deletion, bad migration, corruption, lost object, site failure,
  secret compromise, ransomware-like event; owners, communications, decision points, DNS/certificate/provider
  dependencies, return-to-primary; scheduled DR exercises with recorded RPO/RTO.

### #61 [E15-F03] Automated QA, UAT, training, pilot rollout, go-live gates (split 61a/61b/61c)

- **61a**: risk-based test strategy `docs/qa/test-strategy.md`; deterministic fixtures for multiple branches,
  roles, categories, customers, measurements, images, orders, barcodes, stock, invoices, payments, delivery,
  feedback; legacy import validation flow (preview, rejection, reconciliation, idempotent rerun, rollback) or a
  documented "no source data" decision.
- **61b**: E2E scenarios per garment category, multi-garment order, barcode handoffs, QC rework, stock consumption,
  GST billing, partial/full payment, blocked/allowed dispatch, feedback/alteration; full regression with stored
  artefacts; production-like dress rehearsal including restore and rollback.
- **61c**: role-based UAT scripts and sign-off, accountant approval, training material and quick guides,
  support contacts and runbooks, controlled production-access onboarding, limited pilot with real devices/
  printers/scanners and daily reconciliation, go/no-go review, hypercare and post-launch KPI review.

---

## 10. Risks and mitigations

| Risk | Impact | Mitigation |
| --- | --- | --- |
| Claude Code environment lacks .NET SDK and blocked download egress | Backend issues cannot be built or tested in cloud sessions | Provision an environment image with .NET 10 SDK, Node 22, pnpm, Docker (or allow egress to Microsoft hosts); verify in #20 before other backend work |
| Business approvals (workshops, accountant, device matrix) delay dependent issues | Critical-path slip | Draft-first approach; time-boxed reviews; record provisional decisions with owner and expiry |
| Dependency cycle #41 ↔ #32 | Blocked start | Resolved in Section 6.2; confirm at #41 kickoff |
| Over-scoped issues (#32, #61) | Unreviewable PRs | Sub-issue splits proposed in blueprints |
| Licensing of rendering/imaging libraries | Compliance | D15 lists permissive alternatives; verify before adoption in #35/#42 |
| Real-device and printer testing cannot run in CI | Late discovery of scanning/printing problems | Device lab checklist from #19; physical rehearsal milestones in #35, #37, #61; emulation treated as supplementary |
| Configurable taxonomy increases complexity | Slower first release | Seed data gives a working default; admin UIs ship with each catalog issue; keep rule languages small |
| Offline/PWA state corrupting authoritative data | Financial/custody integrity | Offline queue limited to idempotent scans; server always authoritative; conflicts surfaced (#37, #51) |
| Multi-branch scoping mistakes | Data leakage | Deny-by-default policies, generated matrix tests (#24), architecture test for missing policy |
| Report/projection drift | Wrong business decisions | Reconciliation jobs and freshness indicators (#44–#46); projections never authoritative |

---

## 11. Decisions required from the business owner

1. **Backend platform confirmation** (D1): keep ASP.NET Core (.NET 10) as the roadmap states, and provide a Claude
   Code environment with the SDK; or switch to a Node.js/TypeScript backend that the current environment supports.
   The plan assumes the former.
2. **Hosting model** (D17): cloud (which provider) or on-premises; this fixes IaC tooling, backup targets and TLS.
3. **Providers** (D20): SMS/WhatsApp/email vendors, payment gateway (UPI/card), accounting export target.
4. **Payment rule for dispatch** (#43): full payment, partial threshold, or approved exceptions.
5. **Valuation method** (#40) and rounding/round-off conventions (#41) with the accountant.
6. **Branches at launch** and their timezones/GST registrations (#25).
7. **Device, browser and printer matrix** to support (#19, #52), including hardware scanners and label printers.
8. **Retention periods** for measurements, images, feedback free text, logs and backups (#19, #57).
9. **Label format** (thermal size, QR in addition to Code 128) (#35).
10. **Initial catalog, measurement templates and QC checklists** to be reviewed from the seeded drafts (#27, #29,
    #34).
11. **Amend issue #41's "Depends on"**: replace E06-F01 with the pricing contract defined in Section 6.2 (and add
    E09-F01 to #32's dependencies) so that wave 3 can start without a dependency cycle.

---

## 12. Executing this plan with Claude Code

- **Environment**: a Claude Code environment (or self-hosted runner) with .NET 10 SDK, Node 22 + pnpm, Docker
  (for Testcontainers and compose), PostgreSQL client tools, Playwright browsers, and network access to NuGet and
  npm. Record the image definition under `infra/dev-environment/` in #20 so that every session is reproducible.
- **Operating procedure (one issue per session)**: (1) read the issue and this plan's blueprint; (2) write a
  short plan in the PR description with the evidence checklist; (3) implement on `feat/eXX-fYY-<slug>` with
  Conventional Commits `Refs #NN`; (4) run the fast checks locally (`dotnet format`, `dotnet test`, `pnpm lint`,
  `pnpm test`, architecture tests); (5) open a ready-for-review PR linked to the issue; (6) address review and CI
  until green; (7) attach evidence; (8) close the issue on merge with evidence links.
- **Session boundaries**: sessions do not start a dependent issue until the dependency is merged; parallel
  sessions follow the wave/lane table to avoid overlapping modules.
- **Scope discipline**: no unrelated refactors; anything discovered out of scope becomes a new issue linked to the
  epic.
- **Repository instructions**: `CLAUDE.md` (from #22) is the single place for commands and rules; this plan is
  referenced, not duplicated.

---

## 13. Immediate next steps (first ten pull requests)

| # | PR | Issue | Notes |
| --- | --- | --- | --- |
| 1 | This plan (`docs/IMPLEMENTATION_PLAN.md`) | #1 | Owner reviews decisions in Section 11 |
| 2 | Workflow maps, glossary, RACI, exception catalogue | #17 | Drafted for the workshop |
| 3 | Architecture docs and ADR-0001…0012 | #18 | Includes architecture-test rule list |
| 4 | NFRs, SLOs, data classification, DoR/DoD, release gates | #19 | Proposed numeric targets for confirmation |
| 5 | Solution scaffold, compose environment, health checks, architecture tests | #20 | First backend PR; validates the environment |
| 6 | Persistence conventions, migrations, outbox, configuration, flags, seed | #21 | Platform foundation |
| 7 | CI gates, templates, CODEOWNERS, `CLAUDE.md` | #22 | Can run in parallel with #23 |
| 8 | Authentication, sessions, MFA, passkeys, recovery | #23 | Behind the BFF |
| 9 | Design system, layouts, Storybook | #50 | Parallel frontend lane |
| 10 | RBAC, branch scopes, authorisation regression suite | #24 | Unlocks all business modules |

Earlier in this session a Node.js/TypeScript scaffold (pnpm workspace with a shared domain package: category
measurement fields, GST calculation, barcode format, order state machine, permissions) was drafted before the
roadmap's ASP.NET Core direction was reviewed. It is kept out of the repository; its category field lists and GST
test cases are reused as seed-data and golden-test drafts in #27 and #41.
