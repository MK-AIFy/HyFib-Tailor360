/**
 * The pricing administration payloads (#237): a branch's GST registration and the tax configuration
 * version list, pinned against the published contract in `api/contract.ts`.
 *
 * Every decimal member is typed `number | string`, because the published schema types a decimal as
 * `{"type": ["number", "string"]}` — `TaxConfigurationSummary.versionNumber` is the one here. No
 * arithmetic is ever done on one; amounts, rates and dates are rendered through `getFormatters()`.
 */

/** A branch's GST registration, as recorded — never deleted, only amended or given a last day. */
export interface GstRegistration {
  readonly gstRegistrationId: string
  readonly branchId: string
  readonly gstin: string
  readonly stateCode: string
  readonly legalName: string
  readonly tradeName: string | null
  readonly effectiveFrom: string
  readonly effectiveTo: string | null
  readonly createdAt: string
  readonly updatedAt: string
}

/**
 * What is sent to record or amend a registration.
 *
 * The body of both the `POST` and the `PUT`, and the `PUT` is whole-value: every field must be
 * re-sent, and a field left out is refused, not kept. `branchId` never changes on an amendment, but
 * the form still sends it — the branch is shown as a read-only fact, not an omitted one.
 */
export interface GstRegistrationRequest {
  readonly branchId: string
  readonly gstin: string | null
  readonly stateCode: string | null
  readonly legalName: string | null
  readonly tradeName: string | null
  readonly effectiveFrom: string | null
  readonly effectiveTo: string | null
  readonly reason: string | null
}

/** One version of the tax configuration, as the register lists it — newest first, read only here. */
export interface TaxConfigurationSummary {
  readonly taxConfigurationVersionId: string
  readonly versionNumber: number | string
  readonly name: string
  readonly notes: string | null
  readonly status: string
  readonly effectiveFrom: string
  readonly clonedFromVersionId: string | null
  readonly createdAt: string
  readonly publishedAt: string | null
  readonly retiredAt: string | null
}

/**
 * One thing a publish validation noticed.
 *
 * Declared here because two later siblings (E09-F01-5b, E09-F01-7b) consume it; nothing in this
 * issue renders one. The server's sentence is what is rendered — the code is never shown.
 */
export interface BillingFinding {
  readonly severity: string
  readonly code: string
  readonly message: string
  readonly target: string | null
}

/** What a publish validation found, for a price list or a tax configuration version. */
export interface BillingValidationReport {
  readonly versionId: string
  readonly canPublish: boolean
  readonly findings: readonly BillingFinding[]
}

/**
 * One tax component's rate, as a percentage — never a fraction (conventions section 1.1).
 *
 * `kind` is one of `Cgst`, `Sgst`, `Igst`, `Cess`; it stays a bare `string` rather than a narrowed
 * union because it is rendered through the message catalogue, never branched on.
 */
export interface TaxRate {
  readonly kind: string
  readonly ratePercent: number | string
}

/** One tax code of a tax configuration version. */
export interface TaxCode {
  readonly taxCodeId: string
  /** The concept a code stands for, stable across the code's own edits and across versions. */
  readonly taxCodeKey: string
  readonly code: string
  readonly description: string
  /** The HSN (goods) or SAC (services) classification, digits only. */
  readonly classification: string
  /** `Goods` or `Services`. */
  readonly kind: string
  /** Whether the code may be given to a price-list item. A retired code stays readable. */
  readonly active: boolean
  readonly rates: readonly TaxRate[]
}

/** One tax configuration version and every tax code in it. */
export interface TaxConfiguration {
  readonly version: TaxConfigurationSummary
  readonly taxCodes: readonly TaxCode[]
}

/** One component's rate, as sent. */
export interface TaxRateRequest {
  readonly kind: string | null
  readonly ratePercent: number | string | null
}

/** Add or replace a tax code. Whole-value: an omitted rate list means nil-rated, not unchanged. */
export interface TaxCodeRequest {
  readonly code: string | null
  readonly description: string | null
  readonly classification: string | null
  readonly kind: string | null
  readonly active: boolean
  readonly rates: readonly TaxRateRequest[] | null
  readonly reason: string | null
}

/** Start a tax configuration draft, empty or as a copy of an existing version. */
export interface CreateTaxConfigurationDraftRequest {
  readonly name: string | null
  readonly notes: string | null
  readonly effectiveFrom: string | null
  readonly cloneFromVersionId: string | null
}

/** Change a draft's own name, notes and first day. */
export interface DescribeTaxConfigurationRequest {
  readonly name: string | null
  readonly notes: string | null
  readonly effectiveFrom: string | null
  readonly reason: string | null
}

/**
 * What a publish answered with (E09-F01-5b): the version now published, the one it superseded (null
 * the first time a configuration is ever published), and every finding the publication returned —
 * warnings included. A successful publish can still carry a warning, which is why this is not just
 * `TaxConfiguration`.
 */
export interface TaxConfigurationPublication {
  readonly published: TaxConfiguration
  readonly supersededVersionId: string | null
  readonly findings: readonly BillingFinding[]
}
