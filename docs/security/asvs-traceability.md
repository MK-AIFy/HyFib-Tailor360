# ASVS traceability sheet

Plan Section 7 lists "ASVS traceability skeleton with owners" as a #56a deliverable, and Section 8 fixes the
sheet's eventual columns: requirement → control → issue/PR → test → evidence → residual risk → owner → review
date. This document is the **skeleton** those eight columns build on: which requirement applies, to which flow,
under whose ownership, and whether it is covered, scheduled, not applicable or accepted — not yet the control,
test and evidence cells themselves. Section 4 states exactly what the later audit adds and forbids it from
redesigning what is here.

---

## 1. Version, level and licence

| Field | Value |
| --- | --- |
| Standard | OWASP Application Security Verification Standard, version **4.0.3** |
| Level in force | **Level 2**, plus named Level 3 requirements where a flow handles money, custody or credentials. This skeleton populates the Level 2 baseline only — 253 requirements across fourteen chapters. Section 4 states that identifying which Level 3 requirements apply to the billing, custody and credential flows is the W5 audit's job, not this one's |
| Why 4.0.3, not 5.0 | 4.0.3's numbering (`Vx.y.z`) is what every other published ASVS mapping this project is likely to be read against uses today. ASVS 5.0 renumbers by chapter and requirement; moving to it later is a deliberate re-keying exercise, not a patch, and this section is where that decision would be recorded when it happens |
| Source of the requirement list | The OWASP ASVS project's own machine-readable requirement list for 4.0.3, cross-checked against the fourteen chapter totals it publishes (`V1`38, `V2`48, `V3`17, `V4`9, `V5`30, `V6`13, `V7`12, `V8`15, `V9`7, `V10`5, `V11`8, `V12`15, `V13`13, `V14`23 — 253 in total) |
| Licence attribution | ASVS is © the OWASP Foundation, published under **CC BY-SA 4.0**. This document cites each requirement by its number and paraphrases its intent in one clause; it does not reproduce the standard's text, and a reader who needs the exact wording is pointed at the standard itself, not at a copy of it here |
| Status values | Exactly four, defined here so no row invents a fifth: **covered** (a control exists and, once the W5 audit runs, a named test proves it — this skeleton cites the evidence that already exists, not a promise); **scheduled** (with the issue that will supply it); **not applicable** (with the reason, most often a technology or a product decision this system does not have); **accepted** (with the exception-register entry that carries it — unused throughout this document, because `docs/security/exceptions.md` does not exist yet; see section 4) |
| Document owner | **RG-OD-02** is open — see [`abuse-cases.md`](abuse-cases.md) section 1 for the identical default. Until it closes, the Owner holds this document, with the technical reviewer, per `docs/nfr/traceability.md` line 67 |
| Review cadence | At every wave exit gate, and whenever a chapter's covering issue merges |
| Open decision this document raises | Which ASVS version and level is in force was undecided until this document recorded it as **OD-26** — see section 6 |

---

## 2. Control owner per chapter

**RG-OD-02** (who holds the security owner role) is open. Until it closes, every chapter below is held by the
**Owner, with the technical reviewer** — the same default `docs/nfr/traceability.md` line 67 states and
[`abuse-cases.md`](abuse-cases.md) section 1 already records for the abuse-case catalogue. The per-chapter tables
in section 5 therefore all read "Owner" in their own owner column; this section is where that is said once rather
than in every one of 253 rows.

---

## 3. Cross-reference: chapter → NFR-SE-nn → RG-nn

One row per chapter. Every `NFR-SE-01` to `NFR-SE-14` row of `docs/nfr/traceability.md` section 8 appears at least
once below, and every chapter cites at least one `NFR-SE-nn` row and at least one `RG-nn` gate of
`docs/process/release-gates.md` section 4.

| Chapter | `NFR-SE-nn` rows | `RG-nn` gates |
| --- | --- | --- |
| V1 Architecture, Design and Threat Modeling | NFR-SE-01, NFR-SE-12 | RG-02, RG-14 |
| V2 Authentication | NFR-SE-04, NFR-SE-05 | RG-02, RG-03 |
| V3 Session Management | NFR-SE-03, NFR-SE-05, NFR-SE-14 | RG-03 |
| V4 Access Control | NFR-SE-01, NFR-SE-02, NFR-SE-04 | RG-02, RG-03, RG-04 |
| V5 Validation, Sanitization and Encoding | NFR-SE-10 | RG-02, RG-03 |
| V6 Stored Cryptography | NFR-SE-09 | RG-09 |
| V7 Error Handling and Logging | NFR-SE-09 | RG-09 |
| V8 Data Protection | NFR-SE-13, NFR-SE-14 | RG-02, RG-03 |
| V9 Communication | NFR-SE-05 | RG-03, RG-05 |
| V10 Malicious Code | NFR-SE-11 | RG-03 |
| V11 Business Logic | NFR-SE-02 | RG-03 |
| V12 Files and Resources | NFR-SE-10, NFR-SE-11 | RG-02, RG-03 |
| V13 API and Web Service | NFR-SE-01 | RG-02, RG-04 |
| V14 Configuration | NFR-SE-06, NFR-SE-07, NFR-SE-08, NFR-SE-09 | RG-05, RG-06, RG-08, RG-09, RG-10, RG-11 |

Completeness check (evidence in the pull request): the count of `NFR-SE-nn` rows in `docs/nfr/traceability.md`
section 8 is 14; the count of distinct `NFR-SE-nn` references in the table above is also 14, and every one of
`NFR-SE-01` to `NFR-SE-14` appears at least once.

---

## 4. The contract for the W5 audit

This skeleton is not free to be redesigned by the audit that fills it. The W5 half of #56 adds, per requirement
row in section 5:

- A **control** column: the `ARCH-nnn` rule, the platform component, or the flow's own `CTL-nn` that answers the
  requirement — replacing this skeleton's "covering issue or reason" cell for every row currently marked
  `covered` or `scheduled`.
- A **test** column: the named test that fails if the control is removed, or the issue that will add one — this
  is the same discipline `docs/templates/threat-model.md` section 8 already imposes on a flow model's own
  controls, applied here across chapters instead of within one flow.
- An **evidence** column: where the test's own run output or the scan's own result lives.
- A **residual** column: for a requirement that stays only partly answered, the residual risk it becomes and who
  accepts it — the same `RR-nn` discipline as a flow model's section 9, cross-referenced rather than duplicated.
- The **selected Level 3 requirements** section 1 promises for the billing, custody and credential flows: which
  of the twenty ASVS 4.0.3 Level-3-only requirements apply, and to which flow, decided then rather than guessed
  now.
- Once `docs/security/exceptions.md` exists (its own sub-issue), the **accepted** status becomes usable; until
  then no row in section 5 uses it.

What the audit may **not** do: renumber a requirement, invent a fifth status, or mark a row `covered` without the
test the new column names. A `covered` row in section 5 today cites the evidence that already exists — the audit's
job is to attach the missing test where one is not yet named, not to relitigate whether the citation is honest.

---

## 5. The fourteen chapters

Owner is "Owner" throughout, per section 2. Review date is "W2 exit gate" throughout — the point at which this
skeleton itself, not yet the per-requirement audit, is checked. Level is "2" throughout, per section 1's scope
boundary.

### 5.1 V1 — Architecture, Design and Threat Modeling (38 requirements)

| Requirement | Flow model(s) | Status |
| --- | --- | --- |
| **V1.1.1** — a secure development lifecycle addresses security at every stage | Platform-wide | Covered — CLAUDE.md sections 3–8 are that lifecycle: module boundaries, security rules, testing discipline, the git workflow and the Definition of Done, each enforced by a named test or a required check |
| **V1.1.2** — threat modelling happens for every design change | Platform-wide | Scheduled — `docs/process/definition-of-ready.md` DOR-05 requires a flow to have a model before work starts, but nothing yet requires an *existing* model to be re-read when its flow changes; scheduled with the W5 audit |
| **V1.1.3** — user stories carry explicit negative security constraints | Platform-wide | Scheduled — no work-item template field for this yet; #56b |
| **V1.1.4** — trust boundaries, components and data flows are documented and justified | authentication.md | Covered for authentication (section 4's data flow diagram and trust boundaries); scheduled for the other nine flows, each with #366/#378/#386/#394 |
| **V1.1.5** — the high-level architecture and remote services are analysed | Platform-wide | Covered — `docs/architecture/` container and component documents, referenced by every model's section 1 |
| **V1.1.6** — security controls are centralised, vetted and reusable | Platform-wide | Covered — the `Platform.*` projects are exactly this: one authorisation pipeline, one audit filter, one rate-limit catalogue, each with an `ARCH-nnn` test |
| **V1.1.7** — a secure coding checklist or policy is available to every developer | Platform-wide | Covered — CLAUDE.md, checked into the repository root, read by every contributor and every coding agent |
| **V1.2.1** — application components run under unique, low-privilege operating-system accounts | Platform-wide | Scheduled — `infra/` container user configuration; #394 |
| **V1.2.2** — inter-component communication, including APIs, is authenticated with least privilege | Platform-wide | Not applicable to in-process calls — this is a modular monolith (ADR-0001); a module never calls another module's `Infrastructure` over a network boundary (ARCH-003/ARCH-004). The database connection itself is a secret, covered by V1.6.2 below |
| **V1.2.3** — a single vetted authentication mechanism is used | authentication.md | Covered — ARCH-019 (no endpoint accepts more than one scheme), tested by `AuthenticationSchemeTests` |
| **V1.2.4** — every authentication pathway enforces consistent strength controls | authentication.md | Covered — one `SignInHandler`, one `MultiFactorSignInHandler`, no parallel weaker path |
| **V1.4.1** — trusted enforcement points check access on every call | authorisation.md | Covered — the resource-scope pipeline fails closed (`ResourceScopePipelineTests.RefusesEverybodyWhenTheHostForgotTheResolutionStep`) |
| **V1.4.4** — a single, well-vetted access control mechanism is used | authorisation.md | Covered — `docs/security/permission-matrix.md`, reconciled against the live route table by `AuthorisationMatrixTests` |
| **V1.4.5** — attribute- or feature-based access control checks the resource's own attributes, not just its type | authorisation.md | Covered — ARCH-023, resource scope on a route parameter |
| **V1.5.1** — input and output handling rules are defined by data type, content and applicable laws | Platform-wide | Scheduled — `docs/api/conventions.md` covers request/response shape; a data-type-by-data-type handling rule set does not yet exist as its own document; #56b |
| **V1.5.2** — serialisation with untrusted clients avoids native formats prone to deserialisation attack | Platform-wide | Covered — every endpoint's payload is a `System.Text.Json`-bound record in an `Api` project (ARCH-013); no binary serialiser is exposed to a client |
| **V1.5.3** — input validation runs on a trusted service layer, not only the client | Platform-wide | Covered — validation runs in the `Application` layer server-side; the client's own validation is a convenience, never trusted |
| **V1.5.4** — output encoding happens close to the interpreter that consumes it | Platform-wide | Covered — `System.Text.Json` encodes at serialisation; the React client encodes at render via JSX, never through raw HTML injection |
| **V1.6.1** — an explicit key-management policy exists, including a key lifecycle | Platform-wide | Scheduled — `docs/platform/secrets.md` covers secret delivery, not a key lifecycle policy; #442 (secret and key rotation) |
| **V1.6.2** — consumers of cryptographic services protect key material via a vault or an equivalent | Platform-wide | Covered for delivery — `docs/platform/secrets.md`'s `/run/secrets` mount; scheduled for rotation — #442 |
| **V1.6.3** — keys and passwords are replaceable under a defined re-encryption process | Platform-wide | Scheduled — #442 |
| **V1.6.4** — client-side secrets are treated as insecure and communications are re-verified server-side | authentication.md | Covered — no bearer token is stored client-side (NFR-SE-05); the session cookie is opaque and re-verified on every request |
| **V1.7.1** — a common logging format and approach is used system-wide | Platform-wide | Covered — `IAuditWriter`, `LogRedaction` and the structured logging convention are shared platform components, not per-module inventions |
| **V1.7.2** — logs are securely transmitted to a remote system for analysis | deployment.md | Scheduled — #448 (stand up the self-hosted observability stack) |
| **V1.8.1** — sensitive data is identified and classified into protection levels | Platform-wide | Covered — `docs/nfr/data-classification.md`, seven classes, one section per data area |
| **V1.8.2** — each protection level has an associated set of handling requirements | Platform-wide | Covered — the same document's per-class handling rules |
| **V1.9.1** — communication between components is encrypted where they cross a network | deployment.md | Not applicable in-process (see V1.2.2); covered at the edge — TLS terminates at the reverse proxy per `infra/caddy/Caddyfile` |
| **V1.9.2** — each side of a communication link verifies the other's authenticity | deployment.md | Scheduled — certificate/identity verification between the proxy and an upstream provider is not yet documented as a control; #394 |
| **V1.10.1** — a source-control system requires reviewed, accountable check-ins | Platform-wide | Covered — CLAUDE.md section 6: squash merge after a CODEOWNERS review and a green required check |
| **V1.11.1** — every application component is defined and documented by its business or security function | Platform-wide | Covered — `docs/architecture/module-ownership.md` |
| **V1.11.2** — high-value business logic flows are identified and threat-modelled | Platform-wide | Covered for authentication; scheduled for the other nine flows — see [`abuse-cases.md`](abuse-cases.md) section 4 |
| **V1.12.2** — user-uploaded files served back to a user carry a safe disposition | customer-and-media.md | Scheduled — no upload endpoint exists yet; #6 |
| **V1.14.1** — components of differing trust levels are segregated by well-defined controls | deployment.md | Covered — the compose network segregates the database and object storage from the public edge; only the reverse proxy is published |
| **V1.14.2** — binaries are deployed over trusted, verified connections with signatures | deployment.md | Scheduled — #461 (attest every release artefact: SBOM, cosign signature, build provenance) |
| **V1.14.3** — the build pipeline warns of out-of-date or insecure components | deployment.md | Covered — the CI `sbom` job and the dependency-review-action step of `.github/workflows/ci.yml` |
| **V1.14.4** — the build pipeline verifies secure deployment configuration | deployment.md | Scheduled — #460 (build the three release images, digest-pinned bases, tag-driven build workflow) |
| **V1.14.5** — deployments are sandboxed, containerised or network-isolated | deployment.md | Covered — every host runs as its own container on the compose network |
| **V1.14.6** — no unsupported, insecure or deprecated client-side technology is used | Platform-wide | Covered — `docs/nfr/support-matrix.md` names the supported browser floor; no deprecated plugin technology (Flash, Silverlight, NSAPI) is used |

### 5.2 V2 — Authentication (48 requirements)

Every row cites [`threat-models/authentication.md`](threat-models/authentication.md)'s own `CTL-nn` identifiers.
Section 4's contract is what turns each citation below into a full control/test/evidence row later; this table
only says which `CTL-nn` already answers which requirement.

| Requirement | Status |
| --- | --- |
| **V2.1.1** — a minimum password length of 12 characters | Covered — `Argon2idPasswordHasher`'s policy, CTL-08 |
| **V2.1.2** — passwords of at least 64 characters are permitted | Covered — CTL-08's policy imposes no upper truncation below 128 |
| **V2.1.3** — password truncation is not performed | Covered — same |
| **V2.1.4** — any printable Unicode character, including spaces and emoji, is accepted | Scheduled — not asserted by a named test; #56b |
| **V2.1.5** — users can change their password | Covered — the recovery and change flows, CTL-26 |
| **V2.1.6** — a password change requires both the current and the new password | Scheduled — not asserted by a named test; #56b |
| **V2.1.7** — submitted passwords are checked against a breached-password corpus | Scheduled — CTL-04's "breached-password check" is named in the threat-model template's own worked example, not in `authentication.md`'s real control list; #56b |
| **V2.1.8** — a password-strength meter is offered | Scheduled — client-side UX, not yet built |
| **V2.1.9** — no composition rule restricts the character set | Covered — CTL-08's policy imposes none |
| **V2.1.10** — no periodic rotation or password-history requirement exists | Covered — no such requirement is implemented |
| **V2.1.11** — paste and password-manager autofill are permitted | Scheduled — not asserted by a named test |
| **V2.1.12** — a user may temporarily view the password they typed | Scheduled — a client affordance, not yet asserted |
| **V2.2.1** — anti-automation controls mitigate credential stuffing and brute force | Covered — CTL-09 (throttle), CTL-10 (lockout), proven by AB-05 |
| **V2.2.2** — weak authenticators such as SMS are limited to secondary use, with a stronger option offered | Not applicable — the system offers no SMS-based factor at all; only TOTP and passkeys, per `authentication.md` sections 7–8 |
| **V2.2.3** — a notification is sent after an authentication-detail change | Covered — CTL-37/CTL-38 audit trail entries; a user-facing notification beyond the audit trail is scheduled |
| **V2.3.1** — system-generated initial credentials are securely random, time-limited and single-use | Not applicable — this system has no administrator-issued initial password flow; every account is self-registered or administrator-created through #25's own flow, out of this chapter's scope |
| **V2.3.2** — user-provided authentication devices (U2F, FIDO) can be enrolled | Covered — passkey enrolment, CTL-02/CTL-34/CTL-35 |
| **V2.3.3** — renewal instructions for time-bound authenticators are sent with sufficient notice | Not applicable — no authenticator in this system expires on a schedule that requires renewal |
| **V2.4.1** — passwords are stored salted and resistant to offline attack | Covered — CTL-08, Argon2id |
| **V2.4.2** — the salt is at least 32 bits and chosen to avoid collisions | Covered — Argon2id's own per-password salt, CTL-08 |
| **V2.4.3** — a PBKDF2 iteration count is tuned to server capacity | Not applicable — this system uses Argon2id, not PBKDF2 |
| **V2.4.4** — a bcrypt work factor is tuned to server capacity | Not applicable — this system uses Argon2id, not bcrypt |
| **V2.4.5** — an additional keyed iteration (pepper) uses a secret held outside the database | Scheduled — Argon2id's parameters (CTL-08) do not currently include a server-side pepper; #56b |
| **V2.5.1** — an initial activation or recovery secret is never sent in clear text | Covered — CTL-23, a 256-bit token stored only as its digest |
| **V2.5.2** — password hints or knowledge-based secret questions are not present | Covered — no such feature exists |
| **V2.5.3** — credential recovery never reveals the current password | Covered — CTL-26, a reset changes the password and nothing else |
| **V2.5.4** — no shared or default account exists | Covered — every account is a named `StaffUser`; no seeded `root`/`admin`/`sa` credential ships |
| **V2.5.5** — a user is notified when an authentication factor changes | Covered — CTL-37/CTL-38 audit entries; see V2.2.3 for the same caveat on a user-facing notification |
| **V2.5.6** — recovery uses a secure mechanism such as time-based tokens | Covered — CTL-23/CTL-24, a one-hour, single-use, server-entropy token |
| **V2.5.7** — a lost multi-factor device requires identity-proofing evidence before recovery | Covered, with a named gap — CTL-04 and #25's mandatory reason and revocation on an administrator reset (AB-11); RR-06 records what is not covered |
| **V2.6.1** — lookup secrets (recovery codes) are single-use | Covered — CTL-28 |
| **V2.6.2** — lookup secrets carry at least 112 bits of entropy or an equivalent rate limit | Scheduled — recovery-code entropy is not asserted by a named test; #56b |
| **V2.6.3** — lookup secrets resist offline prediction | Covered — CTL-28, hashed storage |
| **V2.7.1** — clear-text out-of-band authenticators such as SMS are not offered | Not applicable — no out-of-band SMS/PSTN authenticator exists in this system |
| **V2.7.2** — an out-of-band request expires after ten minutes | Not applicable — same |
| **V2.7.3** — an out-of-band code is usable once and for its original request only | Not applicable — same |
| **V2.7.4** — the out-of-band channel is itself secure and independent | Not applicable — same |
| **V2.7.5** — the out-of-band verifier stores only a hashed code | Not applicable — same |
| **V2.7.6** — the initial out-of-band code has sufficient entropy | Not applicable — same |
| **V2.8.1** — time-based OTPs have a defined lifetime | Covered — CTL-29, TOTP step window |
| **V2.8.2** — symmetric OTP-verification keys are highly protected | Covered — CTL-36, wrapped with data protection |
| **V2.8.3** — approved cryptographic algorithms generate and verify OTPs | Covered — standard TOTP (HMAC-SHA1/RFC 6238) via the enrolment handler |
| **V2.8.4** — a time-based OTP is usable only once within its validity window | Covered — CTL-29 |
| **V2.8.5** — a re-used time-based OTP is logged and rejected | Covered — CTL-29 |
| **V2.8.6** — a lost physical OTP generator can be revoked | Not applicable — this system issues no physical OTP hardware token |
| **V2.9.1** — cryptographic verification keys are stored securely | Covered — CTL-36 |
| **V2.9.2** — a challenge nonce is at least 64 bits and unique | Covered — CTL-34, a 256-bit ceremony challenge |
| **V2.9.3** — approved cryptographic algorithms generate, seed and verify the challenge | Covered — WebAuthn/FIDO2 via `Fido2PasskeyCeremony`, CTL-34 |

### 5.3 V3 — Session Management (17 requirements)

| Requirement | Status |
| --- | --- |
| **V3.1.1** — session tokens never appear in a URL parameter | Covered — the session is a cookie only; no route or query string ever carries it |
| **V3.2.1** — a new session token is generated at authentication | Covered — CTL-11 |
| **V3.2.2** — session tokens carry at least 64 bits of entropy | Scheduled — token generation's entropy is not asserted by a named test; #56b |
| **V3.2.3** — session tokens are stored in the browser only via secure methods | Covered — the `__Host-` cookie, CTL-12; NFR-SE-05 |
| **V3.2.4** — session tokens are generated with approved cryptographic algorithms | Scheduled — same gap as V3.2.2 |
| **V3.3.1** — logout and expiry invalidate the token so it cannot be replayed | Covered — CTL-17, CTL-20 |
| **V3.3.3** — a user can terminate all other active sessions after a password change | Not applicable today — a reset changes the password only (CTL-26) and does not currently revoke other sessions; this is a real gap, recorded as **scheduled**, not silently accepted — #56b |
| **V3.3.4** — a user can view and end any or all of their own active sessions | Covered — `IdentifierEditingTests.RevokingAnotherAccountsSessionIsAnsweredAsRevokingOneThatDoesNotExist` proves a user can revoke their own session and no one else's |
| **V3.4.1** — session cookies carry the `Secure` attribute | Covered — CTL-12 |
| **V3.4.2** — session cookies carry the `HttpOnly` attribute | Covered — CTL-12, CTL-13 |
| **V3.4.3** — session cookies use `SameSite` to limit cross-site exposure | Covered — CTL-12, `SameSite=Lax` |
| **V3.4.4** — session cookies use the `__Host-` prefix | Covered — CTL-12 |
| **V3.4.5** — cookie scope does not leak to a sibling application under the same parent domain | Not applicable — this deployment publishes one application per host; no sibling application shares a parent domain |
| **V3.5.1** — a user can revoke OAuth tokens held by linked third-party applications | Not applicable — this system has no OAuth-linked third-party application surface |
| **V3.5.2** — session tokens are used rather than static API keys | Covered — every authenticated request carries the session cookie; no static API key exists for staff access |
| **V3.5.3** — stateless tokens (e.g. JWTs) are signed and encrypted against tampering | Not applicable — sessions are opaque, server-resolved identifiers (CTL-14), not stateless signed tokens |
| **V3.7.1** — a step-up action requires a full, fresh, or re-verified session | Covered — CTL-19, CTL-31, ARCH-018 |

### 5.4 V4 — Access Control (9 requirements)

| Requirement | Flow model(s) | Status |
| --- | --- | --- |
| **V4.1.1** — access control runs on a trusted service layer | authorisation.md | Covered — ARCH-007, the resource-scope pipeline |
| **V4.1.2** — attributes and policy data used in access decisions cannot be manipulated by the caller | authorisation.md | Covered — permission and branch scope are resolved from the server-issued session, never from a request body or header |
| **V4.1.3** — least privilege: a user reaches only what their role grants | authorisation.md | Covered — `docs/security/permission-matrix.md`, reconciled by `AuthorisationMatrixTests` and `RoleMatrixTests` |
| **V4.1.5** — access control fails securely, including on an exception | authorisation.md | Covered — `ResourceScopePipelineTests.RefusesEverybodyWhenTheHostForgotTheResolutionStep` |
| **V4.2.1** — sensitive data and APIs are protected against IDOR | authorisation.md | Covered — see [`abuse-cases.md`](abuse-cases.md) `ABF-01` and its "defeated today by" evidence |
| **V4.2.2** — a strong anti-CSRF mechanism protects authenticated state-changing requests | authentication.md | Covered — CTL-15, CTL-16, `CrossSiteDefenceTests` |
| **V4.3.1** — administrative interfaces require multi-factor authentication | authorisation.md | Covered — every permission the matrix marks `RequiresStepUp` enforces ARCH-018 |
| **V4.3.2** — directory browsing is disabled and metadata files are not served | deployment.md | Covered — `Tailor360.Web` serves only its declared static assets and API routes; no directory listing is enabled |
| **V4.3.3** — sensitive transactions carry step-up or adaptive authentication | authorisation.md | Covered — the same ARCH-018 mechanism as V4.3.1 |

### 5.5 V5 — Validation, Sanitization and Encoding (30 requirements)

| Requirement | Flow model(s) | Status |
| --- | --- | --- |
| **V5.1.1** — defences exist against HTTP parameter pollution | Platform-wide | Scheduled — not asserted by a named test; #56b |
| **V5.1.2** — mass-parameter-assignment attacks are prevented | Platform-wide | Covered — every endpoint binds a purpose-built `Api`-project request record (ARCH-013), never a domain entity, so an extra field has nothing to bind to |
| **V5.1.3** — all input is validated against a defined syntax | Platform-wide | Covered — model binding plus `Application`-layer validation; RFC 9457 field errors on failure, `docs/api/conventions.md` |
| **V5.1.4** — structured data is strongly typed and schema-validated | Platform-wide | Covered — same, via `System.Text.Json` record binding |
| **V5.1.5** — redirects only target an allow-listed destination | Platform-wide | Not applicable — this application issues no open redirect; the PWA shell fallback serves `index.html`, it does not redirect |
| **V5.2.1** — untrusted HTML input is sanitised by an HTML sanitiser | customer-and-media.md | Not applicable today — no flow accepts rich-text HTML input; scheduled if one is added |
| **V5.2.2** — unstructured data is sanitised for allowed characters and length | Platform-wide | Scheduled — no system-wide sanitisation policy document exists yet; #56b |
| **V5.2.3** — input passed to mail systems is sanitised against header injection | Platform-wide | Scheduled — `IdentityMailer`/`ChannelEmailDispatchQueue` are not yet asserted against header injection by a named test |
| **V5.2.4** — dynamic code execution (`eval` and equivalents) is avoided | Platform-wide | Covered — no dynamic code execution appears anywhere in `src/`, consistent with a statically typed .NET/TypeScript codebase |
| **V5.2.5** — template-injection attacks are defended against | Platform-wide | Not applicable — this system renders no server-side template from untrusted input; PDF rendering (QuestPDF) composes from typed data, not from a template string |
| **V5.2.6** — SSRF is defended against by validating outbound targets | integrations.md | Scheduled — see [`abuse-cases.md`](abuse-cases.md) `ABF-09`; `IOutboundHttp` has no implementation yet, matching `NFR-SE-10`'s "proof scheduled (#54, #55)" |
| **V5.2.7** — user-supplied SVG is sanitised, disabled or sandboxed | customer-and-media.md | Scheduled — no upload endpoint exists yet; #6 |
| **V5.2.8** — scriptable template markup in uploaded documents is sanitised or sandboxed | customer-and-media.md | Scheduled — same |
| **V5.3.1** — output encoding matches the interpreter and context | Platform-wide | Covered — see V1.5.4 |
| **V5.3.2** — output encoding preserves the caller's character set and locale | Platform-wide | Covered — UTF-8 throughout; `docs/nfr/support-matrix.md` |
| **V5.3.3** — context-aware output escaping defeats reflected, stored and DOM XSS | Platform-wide | Covered — React's JSX escapes by default; the enforcing CSP (`ContentSecurityPolicyTests`) is the defence-in-depth layer |
| **V5.3.4** — database queries are parameterised | Platform-wide | Covered — every module's `Infrastructure` project uses EF Core; no raw string-concatenated SQL exists |
| **V5.3.5** — where parameterisation is unavailable, context-specific output encoding is used | Platform-wide | Not applicable — no code path in this system builds a query outside EF Core |
| **V5.3.6** — JSON injection and JSON `eval` attacks are defended against | Platform-wide | Covered — `System.Text.Json` never uses `eval`-equivalent parsing |
| **V5.3.7** — LDAP injection is defended against | Platform-wide | Not applicable — this system has no LDAP integration |
| **V5.3.8** — OS command injection is defended against; OS calls use parameterised interfaces | Platform-wide | Covered — no code path in `src/` shells out to the operating system |
| **V5.3.9** — local or remote file inclusion is defended against | Platform-wide | Not applicable — no code path includes a file by a caller-supplied path; media storage is planned to go through authorised object-storage delivery only (ADR-0005) |
| **V5.3.10** — XPath or XML injection is defended against | Platform-wide | Not applicable — this system parses no XML |
| **V5.4.1** — memory-safe operations prevent buffer and pointer errors | Platform-wide | Covered — the codebase is managed .NET and TypeScript; no `unsafe` block exists in `src/` |
| **V5.4.2** — format strings never take hostile input | Platform-wide | Covered — .NET string interpolation and structured logging templates, never a caller-controlled format string |
| **V5.4.3** — integer overflow is prevented by range and sign checks | Platform-wide | Scheduled — not asserted by a named test system-wide; #56b |
| **V5.5.1** — serialised objects carry integrity checks or are encrypted | Platform-wide | Not applicable — this system serialises no object graph a client can round-trip; every payload is a flat JSON record |
| **V5.5.2** — XML parsers are configured to their most restrictive setting | Platform-wide | Not applicable — this system parses no XML |
| **V5.5.3** — deserialisation of untrusted data is avoided or protected | Platform-wide | Covered — `System.Text.Json` binds to typed records with no polymorphic type resolution enabled |
| **V5.5.4** — client-side JSON parsing uses `JSON.parse`, never `eval` | customer-and-media.md | Covered — the React client uses the browser's native `JSON.parse` throughout; no `eval`-based parser is used |

### 5.6 V6 — Stored Cryptography (13 requirements)

| Requirement | Flow model(s) | Status |
| --- | --- | --- |
| **V6.1.1** — regulated personal data is encrypted at rest | customer-and-media.md | Scheduled — `docs/nfr/data-classification.md` classifies personal data but column-level encryption at rest is not yet asserted by a named test; #366 |
| **V6.1.2** — regulated health data is encrypted at rest | customer-and-media.md | Not applicable — this system holds no medical or health data |
| **V6.1.3** — regulated financial data is encrypted at rest | billing-payment.md | Scheduled — same gap as V6.1.1, for the financial data class; #386 |
| **V6.2.1** — cryptographic modules fail securely against padding-oracle-style errors | Platform-wide | Covered — .NET's `System.Security.Cryptography` and ASP.NET Core Data Protection are the only cryptographic surfaces used; neither exposes a raw padding-oracle-prone primitive to application code |
| **V6.2.2** — industry-approved cryptographic algorithms and libraries are used | Platform-wide | Covered — Argon2id (CTL-08), ASP.NET Core Data Protection (CTL-36), WebAuthn/FIDO2 (CTL-34) |
| **V6.2.3** — IVs, cipher configuration and block modes are set securely by design, not by the caller | Platform-wide | Covered — Data Protection manages its own key ring and IV generation; no application code chooses a cipher mode directly |
| **V6.2.4** — cryptographic parameters can be reconfigured without a source-code change | Platform-wide | Scheduled — Argon2id's parameters are compiled constants in `Argon2idPasswordHasher`, not configuration; #56b |
| **V6.2.5** — known-insecure block and padding modes are avoided | Platform-wide | Covered — no ECB or PKCS#1 v1.5 usage exists in `src/` |
| **V6.2.6** — nonces and IVs are never reused with the same key | Platform-wide | Covered — Data Protection and the passkey ceremony's own nonce generation (CTL-34) are single-use by construction |
| **V6.3.1** — random values are generated with a cryptographically secure generator | Platform-wide | Covered — ARCH-015 (`Guid.NewGuid()` only inside the sanctioned identifier generator), and `RandomNumberGenerator` throughout the security-sensitive code this session has read (recovery tokens, ceremony challenges, CSP nonces) |
| **V6.3.2** — random GUIDs use a CSPRNG-backed v4 algorithm | Platform-wide | Covered — `IIdGenerator`, UUIDv7 built on cryptographically secure randomness (CLAUDE.md section 4, rule 8) |
| **V6.4.1** — a secrets-management solution such as a vault is used | Platform-wide | Covered — `docs/platform/secrets.md`, `/run/secrets` |
| **V6.4.2** — key material is isolated from the application via a security module | Platform-wide | Scheduled — no hardware or cloud KMS-backed isolation exists yet; keys currently live inside the mounted secret files, not a separate module; #442 |

### 5.7 V7 — Error Handling and Logging (12 requirements)

| Requirement | Flow model(s) | Status |
| --- | --- | --- |
| **V7.1.1** — credentials and payment details are never logged | Platform-wide | Covered — `LogRedaction`, `LogRedactionTests` |
| **V7.1.2** — other sensitive data defined by law or policy is never logged | Platform-wide | Covered — the same mechanism, extended per property name rather than trusted per call site (CLAUDE.md section 4, rule 7) |
| **V7.1.3** — security-relevant events, including sign-in success and failure, are logged | authentication.md | Covered — CTL-39, `HalfSignedInSessionTests.ASignInIsAttributedToTheAccountThatSignedInRatherThanToTheSystem` |
| **V7.1.4** — each log event carries enough context for investigation | Platform-wide | Covered — correlation and causation identifiers are carried system-wide (CLAUDE.md section 4, rule 7) |
| **V7.2.1** — authentication decisions are logged without the credential itself | authentication.md | Covered — CTL-40 |
| **V7.2.2** — access-control decisions, including every refusal, are logged | authorisation.md | Covered — `DenialAuditTests` |
| **V7.3.1** — logging components encode data to prevent log injection | Platform-wide | Scheduled — not asserted by a named test; structured logging (never string-concatenated) makes this unlikely but unproven; #56b |
| **V7.3.3** — security logs are protected from unauthorised access and modification | deployment.md | Scheduled — #448 (the self-hosted observability stack) is what will carry this control |
| **V7.3.4** — log time sources are synchronised, ideally to UTC | Platform-wide | Covered — ARCH-014 forces every timestamp through the clock abstraction, which is UTC by construction |
| **V7.4.1** — a generic message is shown on an unexpected or sensitive error | Platform-wide | Covered — `ErrorLeakTests.AnExceptionInAHandlerDisclosesNothing`, RFC 9457 problem details |
| **V7.4.2** — exception handling covers expected and unexpected conditions across the codebase | Platform-wide | Covered — `UseExceptionHandler`, `ErrorLeakTests.AMalformedRequestDisclosesNothing` |
| **V7.4.3** — a last-resort handler catches every unhandled exception | Platform-wide | Covered — the same exception-handling middleware; `ErrorLeakTests.AFailingResponseNamesNoServerSoftware` |

### 5.8 V8 — Data Protection (15 requirements)

| Requirement | Flow model(s) | Status |
| --- | --- | --- |
| **V8.1.1** — sensitive data is not cached in intermediate components such as load balancers | deployment.md | Scheduled — Caddy's own caching configuration is not yet asserted against this; #394 |
| **V8.1.2** — cached or temporary server-side copies of sensitive data are protected | Platform-wide | Scheduled — same |
| **V8.1.3** — a request minimises the number of hidden or replayable parameters | Platform-wide | Not applicable — this system's endpoints are typed JSON payloads, not hidden-field forms |
| **V8.1.4** — abnormal request volumes are detected and alertable | deployment.md | Scheduled — #453 (bounded failure, resilience policy catalogue) and #449 (dashboards as code) are the mechanisms this depends on |
| **V8.2.1** — sufficient anti-caching headers prevent browser caching of sensitive responses | Platform-wide | Scheduled — not asserted by a named test; #56b |
| **V8.2.2** — browser storage (`localStorage`, `sessionStorage`, IndexedDB, cookies) holds no sensitive data | authentication.md | Covered — NFR-SE-05; the session is an opaque `__Host-` cookie, never a token in script-readable storage |
| **V8.2.3** — authenticated client-side data is cleared on session end | authentication.md | Scheduled — not asserted by a named client-side test; #56b |
| **V8.3.1** — sensitive data travels in the request body or headers, never the query string | Platform-wide | Covered — CLAUDE.md section 4, rule 8; no route or query parameter carries personal data |
| **V8.3.2** — users can remove or export their own data on demand | customer-and-media.md | Covered — `CustomerExportEndpointTests`; see [`abuse-cases.md`](abuse-cases.md) `ABF-08` |
| **V8.3.3** — clear language describes how personal information is collected and used | customer-and-media.md | Scheduled — a consent/notice document is not yet written; `docs/security/threat-models/customer-and-media.md` (#366) |
| **V8.3.4** — all sensitive data is identified and its retention/protection controls documented | Platform-wide | Covered — `docs/nfr/data-classification.md` section 5, one subsection per data area |
| **V8.3.5** — access to sensitive data is audited without logging the data itself | customer-and-media.md | Covered — `CustomerExportEndpointTests`' export-access audit trail, and `IAuditWriter` platform-wide |
| **V8.3.6** — sensitive information in memory is overwritten once no longer needed | Platform-wide | Not applicable — .NET's managed memory model does not expose an application-level primitive for this; considered not applicable rather than silently covered |
| **V8.3.7** — sensitive information required to be encrypted uses approved algorithms | Platform-wide | Covered — see V6.2.2 |
| **V8.3.8** — sensitive personal data is subject to a documented retention classification | Platform-wide | Scheduled — **OD-08** (retention periods) is open; the data-classification document names where a period would apply but sets none, per CLAUDE.md section 8's rule against inventing one |

### 5.9 V9 — Communication (7 requirements)

| Requirement | Flow model(s) | Status |
| --- | --- | --- |
| **V9.1.1** — TLS is used for all client connectivity, with no insecure fallback | deployment.md | Covered — `infra/caddy/Caddyfile` terminates TLS at the edge; the compose network behind it is not internet-reachable |
| **V9.1.2** — only strong, up-to-date cipher suites are enabled | deployment.md | Scheduled — not asserted by a named test against the deployed proxy configuration; #394 |
| **V9.1.3** — only current TLS protocol versions (1.2, 1.3) are enabled | deployment.md | Scheduled — same |
| **V9.2.1** — connections use trusted TLS certificates | deployment.md | Scheduled — `infra/caddy/Caddyfile`'s ACME DNS-01 placeholder is not yet a live certificate; #394 |
| **V9.2.2** — encrypted connections cover every inbound and outbound path | integrations.md | Scheduled — outbound provider connections are not yet built (`IOutboundHttp` has no implementation); see `ABF-09` |
| **V9.2.3** — encrypted connections to external systems are backed by an approved certificate authority | integrations.md | Scheduled — same, once an adapter exists |
| **V9.2.4** — certificate revocation is checked, for example via OCSP stapling | deployment.md | Scheduled — not yet configured; #394 |

### 5.10 V10 — Malicious Code (5 requirements)

| Requirement | Flow model(s) | Status |
| --- | --- | --- |
| **V10.2.1** — no unauthorised "phone home" or data collection exists in the code or its dependencies | Platform-wide | Covered — the CI `sbom` job and dependency-review step give visibility into every third-party component; no telemetry SDK beyond this project's own is referenced in `src/` |
| **V10.2.2** — the application requests no unnecessary or excessive permission | customer-and-media.md | Covered — `Permissions-Policy: camera=(self), microphone=(), geolocation=(), payment=()` in `SecurityHeadersMiddleware` grants only the camera, for the scan screen, and nothing else |
| **V10.3.1** — an auto-update feature fetches updates over a secure channel with signature verification | Platform-wide | Not applicable — this system has no client or server auto-update feature; releases are deployed images, not self-updating binaries |
| **V10.3.2** — the application employs integrity protections such as code signing or subresource integrity | deployment.md | Scheduled — #461 (cosign signature and build provenance) |
| **V10.3.3** — the application is protected from subdomain takeover via stale DNS entries | deployment.md | Scheduled — not yet reviewed; #394 |

### 5.11 V11 — Business Logic (8 requirements)

| Requirement | Flow model(s) | Status |
| --- | --- | --- |
| **V11.1.1** — business logic steps process in the intended sequential order | order-workflow.md | Scheduled — see [`abuse-cases.md`](abuse-cases.md) `ABF-03`; the order-workflow domain is mid-build |
| **V11.1.2** — business logic steps require realistic timing between them | order-workflow.md | Scheduled — same |
| **V11.1.3** — business actions carry appropriate transaction limits, correctly enforced | billing-payment.md | Covered in part — `InvoicePostingTests`' idempotency and drift checks; a numeric transaction-value limit is not yet a documented control |
| **V11.1.4** — anti-automation controls limit excessive calls such as mass data exfiltration | reports-exports.md | Covered in part — export rate limiting is asserted by `CustomerExportEndpointTests`; a system-wide mass-exfiltration control is scheduled |
| **V11.1.5** — business-logic limits guard against likely business risks | inventory.md | Scheduled — see `ABF-06`; the inventory module is not yet built |
| **V11.1.6** — the application does not suffer TOCTOU or other race conditions | billing-payment.md | Covered — `InvoicePostingTests.TwentyConcurrentPostsAcrossTwoBranchesAreNumberedContiguouslyPerBranch`, `RefundEndpointTests.TwoReversalsAtOnceEndWithOneRecordAndOneConflict` |
| **V11.1.7** — the application monitors for unusual business-logic activity | Platform-wide | Scheduled — #449/#450 (dashboards as code) |
| **V11.1.8** — configurable alerting exists for automated attacks or unusual activity | Platform-wide | Scheduled — same |

### 5.12 V12 — Files and Resources (15 requirements)

None of the requirements below is answered today: `MediaEndpoints` maps no route, and no upload feature exists in
`src/`, matching [`abuse-cases.md`](abuse-cases.md) `ABF-07`'s own honest gap. Every row is **scheduled**, against
**#6** (E05) and the customer-and-media threat model, **#366**, rather than marked `not applicable` — the feature
is planned, not excluded.

| Requirement |
| --- |
| **V12.1.1** — file size limits prevent storage exhaustion |
| **V12.1.2** — compressed-file expansion ratios are checked before decompression |
| **V12.1.3** — a per-user file-count and size quota is enforced |
| **V12.2.1** — uploaded files are validated against their declared type by content, not just extension |
| **V12.3.1** — uploaded filenames are never used directly by the filesystem |
| **V12.3.2** — uploaded filename metadata cannot disclose or overwrite another file |
| **V12.3.3** — uploaded filename metadata cannot trigger execution |
| **V12.3.4** — the application is protected against Reflective File Download |
| **V12.3.5** — untrusted file metadata is never passed to an OS API directly |
| **V12.3.6** — the application never executes functionality sourced from an untrusted upload |
| **V12.4.1** — files from untrusted sources are stored outside the web root with limited permissions |
| **V12.4.2** — files from untrusted sources are scanned by an antivirus engine before serving |
| **V12.5.1** — the web tier serves only an explicit allow-list of file extensions |
| **V12.5.2** — direct requests to an uploaded file never execute as HTML or JavaScript |
| **V12.6.1** — the server uses an allow-list of resources it may fetch on the caller's behalf |

`V12.6.1` doubles as the SSRF control for uploads specifically; see also `V5.2.6` and `NFR-SE-10` for the same
control pattern applied to outbound calls generally.

### 5.13 V13 — API and Web Service (13 requirements)

| Requirement | Flow model(s) | Status |
| --- | --- | --- |
| **V13.1.1** — every component uses the same encoders and parsers | Platform-wide | Covered — `System.Text.Json` throughout the API surface; no second parser is used for the same content type |
| **V13.1.3** — API URLs never expose an API key or a session token | Platform-wide | Covered — every route in `docs/api/openapi.v1.json` carries identifiers only, never a credential |
| **V13.1.4** — authorisation is enforced at both the URI and the resource level | authorisation.md | Covered — ARCH-007 at the route, ARCH-023 at the resource |
| **V13.1.5** — requests with an unexpected or missing content type are rejected | Platform-wide | Scheduled — not asserted by a named test system-wide; #56b |
| **V13.2.1** — enabled HTTP methods are valid for the resource | Platform-wide | Covered — `MapHealthChecks`'s three orchestrator probes are the one documented `ANY`-method exception (`docs/architecture/architecture-rules.md` section 4); every other route declares its own verb |
| **V13.2.2** — JSON schema validation runs before input is accepted | Platform-wide | Covered — model binding against a typed record is exactly this |
| **V13.2.3** — cookie-based REST services are protected from CSRF | authentication.md | Covered — CTL-15, CTL-16 |
| **V13.2.5** — REST services check the incoming `Content-Type` explicitly | Platform-wide | Scheduled — same gap as V13.1.5 |
| **V13.2.6** — message headers and payload are protected from in-transit modification | deployment.md | Covered — TLS at the edge (V9.1.1); no additional message-level signing exists, and none is needed given the transport guarantee |
| **V13.3.1** — XSD schema validation and structural limits apply to XML documents | Platform-wide | Not applicable — this system publishes and accepts no XML |
| **V13.3.2** — XML payloads are signed via WS-Security | Platform-wide | Not applicable — same |
| **V13.4.1** — GraphQL queries are limited by an allow-list, depth or amount | Platform-wide | Not applicable — this system publishes no GraphQL endpoint |
| **V13.4.2** — GraphQL authorisation logic lives at the business-logic layer | Platform-wide | Not applicable — same |

### 5.14 V14 — Configuration (23 requirements)

| Requirement | Flow model(s) | Status |
| --- | --- | --- |
| **V14.1.1** — build and deployment are secure and repeatable | deployment.md | Covered — `.github/workflows/ci.yml`, `docker-compose.*.yml`; every image is built by the same pipeline |
| **V14.1.2** — compiler flags enable available overflow protections and warnings | Platform-wide | Covered — `Directory.Build.props`'s `TreatWarningsAsErrors` and nullable-reference-type enforcement across the solution |
| **V14.1.3** — server configuration is hardened per the platform's own recommendations | deployment.md | Scheduled — not yet reviewed against a named checklist; #394 |
| **V14.1.4** — the application and its dependencies can be redeployed via automated deployment | deployment.md | Covered — the compose stacks and `Tailor360.Cli` (`migrate`, `init-reference-data`, `seed-synthetic`) redeploy the whole system from empty |
| **V14.2.1** — all components are kept up to date, checked by an automated dependency checker | deployment.md | Covered — the CI dependency-review-action step and the Trivy container scan |
| **V14.2.2** — unneeded features, sample applications and stale configuration are removed | Platform-wide | Scheduled — not asserted by a named audit; #56b |
| **V14.2.3** — externally hosted assets use Subresource Integrity | Platform-wide | Not applicable — the PWA bundles its own assets; no third-party CDN script is loaded by the client |
| **V14.2.4** — third-party components come from trusted, maintained repositories | deployment.md | Covered — NuGet and npm registries only, version-pinned, reviewed by the dependency-review-action step |
| **V14.2.5** — a Software Bill of Materials is maintained | deployment.md | Covered — the CI `sbom` job produces a CycloneDX document on every build |
| **V14.2.6** — the attack surface is reduced by sandboxing third-party libraries | Platform-wide | Not applicable — no third-party library in this system runs in an isolated sandbox distinct from the application process; considered out of scope for a modular monolith of this size |
| **V14.3.2** — debug modes are disabled in production | deployment.md | Covered — `DOTNET_ENVIRONMENT=Production` in the shipped compose configuration; `ASPNETCORE_ENVIRONMENT` is never `Development` outside a developer's own machine |
| **V14.3.3** — HTTP responses do not expose detailed version information | Platform-wide | Covered — `SecurityHeadersMiddleware` strips the `Server` header at the proxy (`infra/caddy/Caddyfile`'s `-Server` directive) |
| **V14.4.1** — every HTTP response carries a `Content-Type` header with a safe character set | Platform-wide | Covered — ASP.NET Core's default JSON content negotiation always sets `charset=utf-8` |
| **V14.4.2** — API responses carry a `Content-Disposition: attachment` header where appropriate | Platform-wide | Not applicable — this API serves JSON to be consumed programmatically by its own client, not downloaded as a file, except the export endpoints, which set their own disposition — covered by `CustomerExportEndpointTests` |
| **V14.4.3** — a Content Security Policy response header mitigates XSS impact | Platform-wide | Covered — `SecurityHeadersMiddleware.PolicyFor`, `ContentSecurityPolicyTests` |
| **V14.4.4** — all responses carry `X-Content-Type-Options: nosniff` | Platform-wide | Covered — `SecurityHeadersMiddleware`, asserted by `WebPipelineTests.EveryResponseCarriesTheSecurityHeaders` |
| **V14.4.5** — a `Strict-Transport-Security` header is included on every response | deployment.md | **Scheduled — a real gap, flagged rather than assumed.** `infra/caddy/Caddyfile`'s own comment claims HSTS is "set by the application in `SecurityHeadersMiddleware`", but the middleware's actual header set (read in full for this document) does not include `Strict-Transport-Security` anywhere. The comment and the code disagree; this row records the code's answer, not the comment's, and the discrepancy is flagged in this pull request rather than silently reconciled |
| **V14.4.6** — a suitable `Referrer-Policy` header avoids leaking sensitive URL data | Platform-wide | Covered — `SecurityHeadersMiddleware`, `same-origin` |
| **V14.4.7** — the application cannot be framed by a third-party site by default | Platform-wide | Covered — `frame-ancestors 'none'` in the CSP |
| **V14.5.1** — the server accepts only the HTTP methods the API actually uses | Platform-wide | Covered — see V13.2.1 |
| **V14.5.2** — the `Origin` header is never used alone for authentication or authorisation decisions | authentication.md | Covered — CTL-16 checks `Origin`/`Sec-Fetch-Site` as a CSRF defence, never as an identity claim; identity comes from the session cookie |
| **V14.5.3** — CORS `Access-Control-Allow-Origin` uses a strict allow-list, never a wildcard with credentials | Platform-wide | Not applicable — this application serves the API and the client from the same origin; no CORS policy is configured because no cross-origin caller is intended |
| **V14.5.4** — headers added by a trusted proxy (bearer tokens, forwarded addresses) are authenticated by the receiver | deployment.md | Covered — `ForwardedHeaderTests.AForwardedAddressFromAnUntrustedSourceIsIgnored`; only a configured, trusted proxy's forwarded address is honoured |

---

## 6. Open decision recorded by this document

Filed in `docs/prd/assumptions-and-open-decisions.md` section 3, per the Scope requirement, rather than decided
silently here:

| Field | Value |
| --- | --- |
| Decision | **OD-26** — which ASVS version and level is in force |
| Raised by | This sub-issue, #349 |
| Owner | Business owner, with the technical reviewer, for confirmation with **RG-OD-02** |
| Default while open | ASVS **4.0.3**, **Level 2** plus named Level 3 requirements for money, custody and credential flows, exactly as section 1 states |
| Closes before | The W2 exit gate |

---

## 7. Related documents

| Document | Why it matters here |
| --- | --- |
| [`README.md`](README.md) | Where this document is indexed |
| [`abuse-cases.md`](abuse-cases.md) | The abuse-case catalogue this sheet's access-control and authentication rows cross-reference |
| [`threat-models/authentication.md`](threat-models/authentication.md) | Section 10, filled from this sheet — see the diff alongside this document |
| [`../nfr/traceability.md`](../nfr/traceability.md) | Section 8, the `NFR-SE-nn` rows section 3 cross-references |
| [`../process/release-gates.md`](../process/release-gates.md) | The `RG-nn` gates section 3 cross-references |
| [`../architecture/architecture-rules.md`](../architecture/architecture-rules.md) | The `ARCH-nnn` rules several rows cite |
| [`../nfr/data-classification.md`](../nfr/data-classification.md) | The data inventory several rows cite |
| [`../prd/assumptions-and-open-decisions.md`](../prd/assumptions-and-open-decisions.md) | Section 3, where section 6's open decision is recorded |

Refs #56, #349
