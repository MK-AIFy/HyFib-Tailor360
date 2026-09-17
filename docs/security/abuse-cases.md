# The abuse-case catalogue

Plan Section 6.2 note 5 and `docs/process/work-breakdown.md` split #56 at the wave boundary: this sub-issue, the
first of #56a's threat-model programme, writes the catalogue every flow model instantiates from — so that all nine
of the flow models still to be written, and the eleven families the `#56b` regression suite tests against, use the
same identifiers instead of each inventing its own framing.

`docs/templates/threat-model.md` section 7 already names these eleven families in prose. This document gives each
one an identifier, an attacker, an attack pattern, a control pattern and — honestly, not aspirationally — what
defeats it today.

---

## 1. Status and ownership

| Field | Value |
| --- | --- |
| Status | Written. Superseded only by a renumbering, which section 3 below forbids |
| Security owner | **RG-OD-02** is open (`docs/process/release-gates.md` section 9): who holds the security owner role, and who deputises, is undecided. Until it closes, the Owner holds the role, with the technical reviewer — the same default `docs/nfr/traceability.md` line 67 already states. Every family's residual-risk ownership below reads "Owner (security owner once RG-OD-02 is decided)" |
| Severities and waiver durations | **RG-OD-01** is open: the severities this document cites from `docs/process/release-gates.md` section 4 are drafted, not confirmed. This document fixes none of its own |
| Role grants | **OD-13** is open: the twelve roles and their default grants this document cites from `docs/security/permission-matrix.md` are a documented default, not an approved one |

---

## 2. The eleven families

`ABF-01` to `ABF-11`, in the order issue #56 lists them. Numbered once, in this order, and frozen the moment this
document merges — see section 3.

| ID | Family | Attacker and motive | Attack pattern | Data classification target | Control pattern |
| --- | --- | --- | --- | --- | --- |
| **ABF-01** | Insecure direct object reference | A signed-in staff member, or a customer-link holder, with a valid session but no legitimate claim to a specific record | Take an identifier from one's own record — a customer, an order, a session, a passkey — and substitute a neighbouring UUID or an identifier known to belong to someone else, on an otherwise identical request | Whichever inventory subsection owns the named resource — most often [`../nfr/data-classification.md`](../nfr/data-classification.md) 5.2 (customer identity), 5.7 (orders and jobs) or 5.15 (staff identity and sessions) | ARCH-023 (a branch-scoped route with a parameter declares a resource scope), enforced by the resource-scope pipeline, which resolves ownership server-side and fails closed; ARCH-022 backstops a route that declares no scope at all |
| **ABF-02** | Privilege escalation | A signed-in member of staff, or an administrator, acting outside the permission, branch or step-up freshness their role actually grants | Call an endpoint whose permission the caller's role does not include; claim a branch outside the caller's assignment; reuse a session that has not satisfied the step-up freshness a sensitive action demands | 5.15 (staff identity, roles, assignments and sessions); the specific resource's own subsection for what the escalation would expose | ARCH-007 (deny by default), ARCH-018 (step-up declared wherever the permission catalogue marks `RequiresStepUp`), ARCH-022/ARCH-023, and the automated reconciliation of `docs/security/permission-matrix.md` against the live route table |
| **ABF-03** | Workflow bypass through a direct API call | A member of staff with legitimate access to the flow, calling an endpoint directly — a script, an edited captured request, a client the screen never offers — rather than following the sequence the workflow assumes | Send the state-changing request for a later step while the aggregate is still in an earlier one, betting that only the client enforces the sequence | 5.7 (estimates, orders and garment job data); 5.8 (barcode and custody events) where a phase transition is scan-driven | The workflow's own aggregate invariants ([`../architecture/invariants.md`](../architecture/invariants.md)), enforced in the domain layer: a transition guard that rejects an invalid `(current state, requested transition)` pair regardless of caller |
| **ABF-04** | Barcode replay | Someone holding a photograph, a printed duplicate, or a captured scan payload of a legitimate barcode | Replay a previously valid scan payload after the legitimate transfer already completed, or present a cloned label at a station other than the one holding the item | 5.8 (barcode identities, scans, labels and custody events) | The check-character format, which defeats a corrupted or forged payload; custody-event idempotency and the state machine of [`../architecture/invariants.md`](../architecture/invariants.md) section 4.8, under which a scan is only valid for a custody row's current expected next event |
| **ABF-05** | Invoice or payment tampering | A cashier, a branch manager, or someone holding a captured session, attempting to alter a posted invoice, reopen a closed cashier session, or apply a refund against money never received | Retry a `POST` that already succeeded, hoping for a second sequence number; post against a draft the order has since re-priced; race two reversals of the same payment concurrently | 5.10 (invoices, credit and debit notes); 5.11 (payments, advances, refunds and allocations); 5.12 (receipts and cashier sessions) | Idempotent posting keyed to the draft; re-pricing drift detection that refuses a stale post; the atomic-or-nothing ledger write of [`../architecture/invariants.md`](../architecture/invariants.md) sections 4.10 and 4.11 |
| **ABF-06** | Stock manipulation | An inventory clerk, or someone holding a captured session, posting a movement that does not correspond to a physical event | A fabricated receipt; an unlogged consumption; a reservation that oversells a location; racing two transfers of the same unit | 5.9 (inventory, suppliers and the stock ledger) | The compensating-correction pattern — never edit a posted ledger entry, only post its reverse; the balanced-pair invariant for transfers, under which in-transit never nets to non-zero; a second-approver step-up on a variance adjustment |
| **ABF-07** | Malicious upload | Anyone who can reach an upload endpoint with a valid session — a customer-link holder attaching a reference image, or a member of staff uploading a captured photo | Upload a file whose magic bytes, size or embedded content disagree with what the endpoint declares it accepts, before a human or a scanner has looked at it | 5.5 (customer material and reference images) | Quarantine until a malware scan passes, with decoding only in the worker under a bounded bulkhead (NFR-SE-11); no object is ever served by a direct URL — every one is streamed by an endpoint that re-authorises and logs the access (CLAUDE.md section 4, rule 9) |
| **ABF-08** | Export leakage | Someone with a legitimate but narrower reach than the export they are requesting — a branch-scoped clerk, a counter account without organisation reach, a session that has not completed step-up | Request an export for a customer or a branch outside the caller's reach; replay an export link after it should have expired; retain a generated file rather than let it be destroyed after its single read | 5.19 (reporting projections and exports); 5.2 for a customer-specific export | The same per-record checks as ABF-01, applied to the export's subject; an expiring, single-use export artefact destroyed on regeneration or expiry; a mandatory reason recorded on every request |
| **ABF-09** | Server-side request forgery | Someone who can influence a URL, hostname or webhook target the server will later fetch or call — a provider callback address, an adapter's configured endpoint, a notification link | Register or edit a target URL that resolves to a private, loopback, link-local or metadata address, or one that redirects there after the first hop is allowed | Not a data class in itself; the exposure is whatever internal service the forged request reaches, and 5.13 (notification intents and deliveries) for the channel most likely to carry one | `IOutboundHttp` as the single sanctioned egress point (ARCH-016), refusing private, loopback, link-local and metadata addresses, disabling redirects and capping response size |
| **ABF-10** | Credential abuse | Someone holding a stolen, phished, stuffed or otherwise compromised credential — a password, a session cookie, a passkey ceremony challenge, or a customer link | Use the compromised credential to act as its rightful holder for as long as it remains valid; present a bearer value that was never issued to the caller | 5.15 (staff identity, roles, assignments and sessions) for a staff credential; 5.14 (customer links and feedback) for a link | Hashed-at-rest bearer values — the raw value is never stored; anti-forgery and origin checks on every state-changing request; for a customer link specifically, NFR-SE-14's purpose-bound, expiring, revocable, rate-limited design |
| **ABF-11** | Denial of service | Anyone who can send requests — an external party with no session, or a signed-in caller running a script instead of the client the rate limits were designed around | Flood a single expensive endpoint from one address; spoof the forwarded-address header to evade per-address partitioning; hold open connections or leases without releasing them | Not a data class; the target is availability — most concretely of 5.15 (a lockout can itself be weaponised against a specific victim) and of the print and notification queues | ARCH-017 (exactly one rate-limit policy per endpoint, from the catalogue); partitioning keyed on a forwarded address only when the proxy that set it is trusted; bounded bulkheads around expensive background work |

### 2.1 What defeats each family today

The honest answer, not the aspirational one. A blank "nothing yet" is not permitted by the acceptance criteria this
document is held to — every row below names either a test that exists now, or the issue that will add one.

| Family | Defeated today by | If not yet: scheduled in |
| --- | --- | --- |
| ABF-01 | `tests/Tailor360.IntegrationTests/Authorization/IdentifierEditingTests.cs` (`RevokingAnotherAccountsSessionIsAnsweredAsRevokingOneThatDoesNotExist`, `RemovingAnotherAccountsPasskeyIsAnsweredAsRemovingOneThatDoesNotExist`, `ACustomerOfAnotherOrganisationIsReadableAndOneOfAnotherOrganisationIsNotEvenAcknowledged`, `TheDuplicateScreenAnswersAnotherOrganisationsRecordAsOneThatDoesNotExist`, `AMergeNamingAnotherOrganisationsRecordIsAnsweredAsOneThatDoesNotExist`); `tests/Tailor360.IntegrationTests/Security/ResourceScopePipelineTests.cs` (`AnswersAJobInAnotherBranchExactlyAsItAnswersAJobThatDoesNotExist`, `AnswersAMalformedIdentifierTheSameWayAgain`); the authorisation matrix's `unknown-resource` and `holder-other-branch` dimensions | — |
| ABF-02 | `tests/Tailor360.IntegrationTests/Authorization/AuthorisationMatrix.cs`, `EndpointInventory.cs` and `matrix.yaml` (the `holder-own-branch`, `holder-other-branch`, `reach-other-branch`, `flagged-without-second-factor` and `flagged-stale-step-up` dimensions, reconciled against every published route); `DenialAuditTests.cs` (`ARefusedWriteIsRecordedWithTheActorTheEndpointAndNothingElse`, `AWriteRefusedForNamingAnotherBranchIsRecorded`, `AWriteRefusedForTheCallersBranchScopeIsRecordedAsABranchRefusal`); `ResourceScopePipelineTests.RefusesEverybodyWhenTheHostForgotTheResolutionStep` | — |
| ABF-03 | Nothing yet — the order-workflow domain is mid-build (#232, #247, #253, #261, #280 and siblings) | #378 (the order-workflow and barcode-custody threat model) |
| ABF-04 | `tests/Tailor360.UnitTests/Platform/BarcodePayloadTests.cs` proves the payload format resists corruption and confusable substitution, but nothing exercises replay: `CustodyEndpoints` maps no routes yet | #37 / #8 (E07-F03, custody transfers) and #378 |
| ABF-05 | `tests/Tailor360.IntegrationTests/Billing/InvoicePostingTests.cs` (`TwoPostsOfOneDraftEndWithOneNumberAndOneRefusalAndNoGap`, `RefusesADriftedDraftAndEveryRouteToAHolderWithoutItsPermission`, `RefusesToPostADraftTheOrderHasMovedOnFromUntilItIsRePriced`, `TwentyConcurrentPostsAcrossTwoBranchesAreNumberedContiguouslyPerBranch`); `tests/Tailor360.IntegrationTests/Billing/RefundEndpointTests.cs` (`TwoReversalsAtOnceEndWithOneRecordAndOneConflict`, `AStrangerAClerkAndAClosedSessionAreEachRefused`) | — |
| ABF-06 | Nothing yet — `InventoryEndpoints` maps no routes | #9 / #264 / #277 (E08-F02/F03) and #378 |
| ABF-07 | Nothing yet — no upload endpoint exists (`MediaEndpoints` maps no routes) and no scanner adapter is referenced anywhere in `src/` | #6 (E05) and #366 |
| ABF-08 | `tests/Tailor360.IntegrationTests/Customers/CustomerExportEndpointTests.cs` (`AnExpiredExportIsRefusedAndItsCopyIsDestroyed`, `AnExportOfAnotherCustomerIsAnsweredAsOneThatDoesNotExist`, `ACounterAccountCannotExportACustomer`, `ASessionWithoutASecondFactorCannotExport`, `ACustomerOfAnotherOrganisationCannotBeExported`, `GeneratingAnExportDestroysTheEarlierCopy`); `tests/Tailor360.IntegrationTests/Security/ErrorLeakTests.cs` for the no-detail-on-failure backstop | #386 for the reports-and-exports instance |
| ABF-09 | Nothing yet — `IOutboundHttp` (`src/Platform/Tailor360.Platform.Abstractions/Ports/IOutboundHttp.cs`) has no implementation anywhere in `src/`, confirmed by `grep -rl IOutboundHttp src/` returning only the interface file and a mention in `src/Modules/CLAUDE.md`. `docs/nfr/traceability.md` NFR-SE-10 already records this honestly | #54, #55, and #394 |
| ABF-10 | `docs/security/threat-models/authentication.md` section 8 (`CTL-01` to `CTL-46`) for the staff-credential case; `tests/Tailor360.IntegrationTests/Security/CrossSiteDefenceTests.cs` (`AStateChangingRequestWithoutATokenIsRefused`, `ATokenWithoutItsCookieHalfIsRefused`, `ACrossSiteStateChangeIsRefusedBeforeTheTokenIsEvenConsidered`, `AStateChangeClaimingAnotherOriginIsRefused`) for session forgery; the customer-link case is not yet written | #366 for the customer-link instance |
| ABF-11 | `tests/Tailor360.IntegrationTests/Security/ForwardedHeaderTests.cs` (`ASpoofedForwardedAddressDoesNotEscapeThePerAddressLimit`, `AForwardedAddressFromAnUntrustedSourceIsIgnored`); authentication's own `CredentialThrottleTests`/`LockoutPolicyTests` for the account-lockout-as-denial-of-service variant; `ErrorLeakTests.AFailureStillCarriesTheSecurityHeaders` | #394 for the integration/deployment instance |

---

## 3. Identifier convention

Families are `ABF-nn` and live only in this document. A flow model's own abuse cases stay `AB-nn` in that file's
local space — as [`threat-models/authentication.md`](threat-models/authentication.md) already does — and each cites
the family it instantiates.

**Because seven later sessions depend on these identifiers, the numbering is frozen the moment this document
merges.** `ABF-01` to `ABF-11` are allocated in the order issue #56 lists them and are never renumbered, never
reordered and never reused — the same discipline `docs/templates/threat-model.md` already imposes on `TM-nnn`,
`AB-nn`, `CTL-nn` and `RR-nn`. A family that turns out to be two is a new `ABF-12`, not a split of an existing
number.

---

## 4. Coverage map

One subsection per flow of [`README.md`](README.md) section 3.2, pre-populated with the model file that will
answer each family — so that the sub-issue writing that model adds nothing to this document and no two model
authors edit the same row. `authentication.md` is not part of this map: it is already written, and section 5 below
maps it as a worked example instead. `authorisation.md` is included, because #32a is not yet merged.

Ten flows, as the acceptance criteria require: the nine this parent covers, plus authorisation.

### 4.1 Authorisation — `threat-models/authorisation.md` (not yet written), #32a, wave W3

| Family | Applicable | Notes |
| --- | --- | --- |
| ABF-01 | Yes | The flow's central subject |
| ABF-02 | Yes | The flow's central subject |
| ABF-03 | Not applicable, because workflow bypass is a business state-machine property this flow does not own | See order-workflow.md |
| ABF-04 | Not applicable | See barcode-custody.md |
| ABF-05 | Not applicable | See billing-payment.md |
| ABF-06 | Not applicable | See inventory.md |
| ABF-07 | Not applicable | See customer-and-media.md |
| ABF-08 | Not applicable directly; the access check an export relies on is this flow's own subject, answered above by ABF-01/ABF-02 | See reports-exports.md for the export-specific story |
| ABF-09 | Not applicable | See integrations.md |
| ABF-10 | Not applicable, because the credential or session itself is authentication.md's subject; this flow assumes a session already exists | See section 5 |
| ABF-11 | Not applicable directly; the generic rate-limit backstop (ARCH-017) is platform-wide | See integrations.md and deployment.md |

### 4.2 Customer data, measurements and media — `threat-models/customer-and-media.md` (not yet written), #366, wave W2

| Family | Applicable | Notes |
| --- | --- | --- |
| ABF-01 | Yes | — |
| ABF-02 | Yes | Generic; confirm no flow-specific gap when written |
| ABF-03 | Not applicable | See order-workflow.md |
| ABF-04 | Not applicable | See barcode-custody.md |
| ABF-05 | Not applicable | See billing-payment.md |
| ABF-06 | Not applicable | See inventory.md |
| ABF-07 | Yes | The flow's central subject for uploaded reference images |
| ABF-08 | Yes | The customer export is this flow's own subject, already partly proven — see section 2.1 |
| ABF-09 | Not applicable | See integrations.md |
| ABF-10 | Not applicable | See section 5 |
| ABF-11 | Not applicable directly | See integrations.md and deployment.md |

### 4.3 Customer links and feedback — `threat-models/customer-links.md` (not yet written), #366, wave W2

| Family | Applicable | Notes |
| --- | --- | --- |
| ABF-01 | Yes | A link naming the wrong customer or order is an IDOR case |
| ABF-02 | Not applicable, because a customer link is a bearer credential, not a role | The escalation risk here is ABF-10, below |
| ABF-03 | Not applicable | See order-workflow.md |
| ABF-04 | Not applicable | See barcode-custody.md |
| ABF-05 | Not applicable | See billing-payment.md |
| ABF-06 | Not applicable | See inventory.md |
| ABF-07 | Not applicable | See customer-and-media.md |
| ABF-08 | Not applicable | See customer-and-media.md and reports-exports.md |
| ABF-09 | Not applicable | See integrations.md |
| ABF-10 | Yes | The flow's central subject: the link itself is the credential, NFR-SE-14 |
| ABF-11 | Not applicable directly | See integrations.md and deployment.md |

### 4.4 Order and garment workflow — `threat-models/order-workflow.md` (not yet written), #378, wave W2

| Family | Applicable | Notes |
| --- | --- | --- |
| ABF-01 | Yes | — |
| ABF-02 | Yes | Generic |
| ABF-03 | Yes | The flow's central subject |
| ABF-04 | Not applicable directly; a workflow phase may be scan-driven, but the replay story is barcode-custody.md's | — |
| ABF-05 | Not applicable | See billing-payment.md |
| ABF-06 | Not applicable | See inventory.md |
| ABF-07 | Not applicable | See customer-and-media.md |
| ABF-08 | Not applicable | See reports-exports.md |
| ABF-09 | Not applicable | See integrations.md |
| ABF-10 | Not applicable | See section 5 |
| ABF-11 | Not applicable directly | See integrations.md and deployment.md |

### 4.5 Barcode identity and custody — `threat-models/barcode-custody.md` (not yet written), #378, wave W2

| Family | Applicable | Notes |
| --- | --- | --- |
| ABF-01 | Yes | — |
| ABF-02 | Yes | Generic |
| ABF-03 | Yes | A scan-driven phase transition can be bypassed the same way an API-called one can; shares the pattern with order-workflow.md |
| ABF-04 | Yes | The flow's central subject |
| ABF-05 | Not applicable | See billing-payment.md |
| ABF-06 | Not applicable directly; a custody event can trigger a stock movement, but the ledger-integrity story is inventory.md's | — |
| ABF-07 | Not applicable | See customer-and-media.md |
| ABF-08 | Not applicable | See reports-exports.md |
| ABF-09 | Not applicable | See integrations.md |
| ABF-10 | Not applicable | See section 5 |
| ABF-11 | Not applicable directly | See integrations.md and deployment.md |

### 4.6 Inventory and the stock ledger — `threat-models/inventory.md` (not yet written), #378, wave W2

| Family | Applicable | Notes |
| --- | --- | --- |
| ABF-01 | Yes | — |
| ABF-02 | Yes | Generic |
| ABF-03 | Not applicable directly; an approval-step bypass on a stock adjustment is this flow's own ABF-06 rather than a separate workflow story | — |
| ABF-04 | Not applicable | See barcode-custody.md |
| ABF-05 | Not applicable | See billing-payment.md |
| ABF-06 | Yes | The flow's central subject |
| ABF-07 | Not applicable | See customer-and-media.md |
| ABF-08 | Not applicable | See reports-exports.md |
| ABF-09 | Not applicable | See integrations.md |
| ABF-10 | Not applicable | See section 5 |
| ABF-11 | Not applicable directly | See integrations.md and deployment.md |

### 4.7 Billing, invoicing and payments — `threat-models/billing-payment.md` (not yet written), #386, wave W2

| Family | Applicable | Notes |
| --- | --- | --- |
| ABF-01 | Yes | — |
| ABF-02 | Yes | Generic |
| ABF-03 | Not applicable directly; posting-sequence integrity is this flow's own ABF-05 rather than a separate workflow-bypass story | — |
| ABF-04 | Not applicable | See barcode-custody.md |
| ABF-05 | Yes | The flow's central subject, already partly proven — see section 2.1 |
| ABF-06 | Not applicable | See inventory.md |
| ABF-07 | Not applicable | See customer-and-media.md |
| ABF-08 | Not applicable directly; a receivables or GST export is reports-exports.md's subject even when the underlying data is this flow's | — |
| ABF-09 | Not applicable | See integrations.md |
| ABF-10 | Not applicable | See section 5 |
| ABF-11 | Not applicable directly | See integrations.md and deployment.md |

### 4.8 Reports and exports — `threat-models/reports-exports.md` (not yet written), #386, wave W2

| Family | Applicable | Notes |
| --- | --- | --- |
| ABF-01 | Yes | — |
| ABF-02 | Yes | Generic |
| ABF-03 | Not applicable | See order-workflow.md |
| ABF-04 | Not applicable | See barcode-custody.md |
| ABF-05 | Not applicable | See billing-payment.md |
| ABF-06 | Not applicable | See inventory.md |
| ABF-07 | Not applicable | See customer-and-media.md |
| ABF-08 | Yes | The flow's central subject |
| ABF-09 | Not applicable | See integrations.md |
| ABF-10 | Not applicable | See section 5 |
| ABF-11 | Not applicable directly | See integrations.md and deployment.md |

### 4.9 Integration adapters — `threat-models/integrations.md` (not yet written), #394, wave W2

| Family | Applicable | Notes |
| --- | --- | --- |
| ABF-01 | Not applicable, because an adapter acts on behalf of the system rather than exposing a caller-editable identifier of its own | See the module whose data it moves |
| ABF-02 | Yes | Generic; confirm no adapter-specific gap, such as a webhook callback that skips its signature check |
| ABF-03 | Not applicable | See order-workflow.md |
| ABF-04 | Not applicable | See barcode-custody.md |
| ABF-05 | Not applicable | See billing-payment.md |
| ABF-06 | Not applicable | See inventory.md |
| ABF-07 | Not applicable | See customer-and-media.md |
| ABF-08 | Not applicable | See reports-exports.md |
| ABF-09 | Yes | The flow's central subject |
| ABF-10 | Not applicable, because a provider API key is a secret under `docs/platform/secrets.md`, not a session | See section 5 |
| ABF-11 | Yes | A provider's quota, a webhook retry storm, or a stalled outbound call exhausting a bulkhead is this flow's own story |

### 4.10 Deployment, secrets and the runtime — `threat-models/deployment.md` (not yet written), #394, wave W2

| Family | Applicable | Notes |
| --- | --- | --- |
| ABF-01 | Not applicable, because deployment has no caller-facing resource identifier | — |
| ABF-02 | Not applicable directly; deployment's privilege surface is infrastructure access, governed by `docs/platform/secrets.md` rather than an application permission | — |
| ABF-03 | Not applicable | See order-workflow.md |
| ABF-04 | Not applicable | See barcode-custody.md |
| ABF-05 | Not applicable | See billing-payment.md |
| ABF-06 | Not applicable | See inventory.md |
| ABF-07 | Not applicable | See customer-and-media.md |
| ABF-08 | Not applicable | See reports-exports.md |
| ABF-09 | Not applicable directly; the runtime hosts the outbound-HTTP port, but the abuse case itself belongs to integrations.md | — |
| ABF-10 | Not applicable | See section 5 |
| ABF-11 | Yes | The flow's central subject: exhausting the runtime itself — connections, disk, the backup window |

---

## 5. Worked example: authentication.md today

[`threat-models/authentication.md`](threat-models/authentication.md) is the one model already written (#23,
reviewed 2026-09-05). Its section 7 allocates its own file-local `AB-01` to `AB-13` — **thirteen** abuse cases
today, not the nine this sub-issue's own drafting issue described, because three (`AB-11` to `AB-13`) were added
after that text was written. This table maps all thirteen onto the families above, without renumbering anything in
`authentication.md` itself, exactly as section 3 requires.

| `AB-nn` | Abuse case | Family |
| --- | --- | --- |
| AB-01 | Second-factor bypass with the password alone | ABF-02 — a half-signed-in session escalating what it may do |
| AB-02 | Enrol your own authenticator | ABF-02 |
| AB-03 | Register your own passkey | ABF-02 |
| AB-04 | Strip the account of its factors | ABF-02 |
| AB-05 | Credential stuffing | ABF-10, and — through the lockout it triggers against the targeted accounts — ABF-11 |
| AB-06 | Enumerate the staff directory | ABF-10 |
| AB-07 | Recovery-token interception | ABF-10 |
| AB-08 | Multi-factor fatigue | ABF-10 |
| AB-09 | Session fixation | ABF-10 — a session ticket is a "Credentials and secrets" asset, per the threat-model template's own class list |
| AB-10 | Step-up bypass | ABF-02 |
| AB-11 | Administrator reset misuse | ABF-02 |
| AB-12 | Cross-site sign-in | ABF-10 |
| AB-13 | Replay a revoked ticket | ABF-10 |

No `AB-nn` in this file answers ABF-01, ABF-03 through ABF-09: authentication has no branch-owned resource
identifier of its own to misdirect (ABF-01 is authorisation.md's subject, per section 4.1), and the remaining
families are outside this flow's scope entirely. That is the recorded negative section 4 above asks for, applied to
the one flow that already has a finished model to check it against.

---

## 6. Related documents

| Document | Why it matters here |
| --- | --- |
| [`README.md`](README.md) | Where this document is indexed, and section 3.2, which this document's coverage map keeps in step with |
| [`../templates/threat-model.md`](../templates/threat-model.md) | Section 7, which now cites the family a flow's own `AB-nn` instantiates |
| [`threat-models/authentication.md`](threat-models/authentication.md) | The one flow already modelled, and the worked example in section 5 |
| [`../nfr/data-classification.md`](../nfr/data-classification.md) | The data inventory each family's target column cites |
| [`../nfr/traceability.md`](../nfr/traceability.md) | Section 8, the `NFR-SE-nn` rows several control patterns above are evidenced against |
| [`../architecture/architecture-rules.md`](../architecture/architecture-rules.md) | The `ARCH-nnn` rules several control patterns cite |
| [`../architecture/invariants.md`](../architecture/invariants.md) | The aggregate invariants ABF-03, ABF-04, ABF-05 and ABF-06 cite |
| [`../process/definition-of-ready.md`](../process/definition-of-ready.md) | **DOR-05**, which this coverage map exists to satisfy |
| [`../process/definition-of-done.md`](../process/definition-of-done.md) | **DoD 6**, which a pull request touching a flow must satisfy against that flow's model |

Refs #56, #346
