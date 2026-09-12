import type { TemplateVersion } from '../admin/types'

/**
 * The measurement-capture payloads (#121, #123), pinned against the published contract in
 * `api/contract.ts`.
 *
 * ## Millimetres in, the unit typed out
 *
 * Every value a screen *reads* is canonical millimetres beside the unit it was taken in, and every
 * value a screen *sends* is the number as typed beside its unit. The server converts, once. A client
 * that sent millimetres would be asserting a conversion of its own, and a client that rounds
 * differently from the server would store a value the server would never have produced — which is
 * a garment that does not fit, discovered at the fitting.
 */

/** One recorded value, as the server hands it back. */
export interface MeasurementValue {
  readonly key: string
  /** Canonical millimetres, or null for a chosen field. Decimals travel as strings on some paths. */
  readonly millimetres: number | string | null
  /** `Inch`, `Centimetre` or `Count`. */
  readonly enteredUnit: string
  readonly choice: string | null
  readonly acknowledged: boolean
}

/** A garment being measured. Shared within the branch; the tag travels in the `ETag`. */
export interface MeasurementDraft {
  readonly measurementDraftId: string
  readonly customerId: string
  readonly branchId: string
  readonly measurementTemplateId: string
  readonly templateVersionId: string
  readonly reusedFromVersionId: string | null
  readonly startedAt: string
  readonly updatedAt: string
  readonly expiresAt: string
  /** When it became a measurement, or null while it is still work in progress. */
  readonly consumedAt: string | null
  readonly values: readonly MeasurementValue[]
}

/** The template version a draft is pinned to, with the template's name beside it. */
export interface MeasurementCaptureTemplate {
  readonly measurementDraftId: string
  readonly measurementTemplateId: string
  readonly code: string
  readonly name: string
  readonly version: TemplateVersion
}

/** One thing standing between a draft and a confirmed measurement. Names the field, never the value. */
export interface MeasurementFinding {
  readonly code: string
  readonly field: string | null
  readonly message: string
}

/** What a draft would be refused for if it were confirmed now. */
export interface MeasurementCheck {
  readonly measurementDraftId: string
  readonly confirmable: boolean
  readonly findings: readonly MeasurementFinding[]
}

/** A confirmed measurement. Never edited. */
export interface MeasurementVersion {
  readonly measurementVersionId: string
  readonly customerId: string
  readonly branchId: string
  readonly measurementTemplateId: string
  readonly templateVersionId: string
  readonly versionNumber: number | string
  readonly takenAt: string
  readonly takenBy: string | null
  readonly reason: string | null
  readonly reusedFromVersionId: string | null
  readonly correctsVersionId: string | null
  readonly values: readonly MeasurementValue[]
}

/** What a caller sends for one field: the number as typed, with the unit beside it. */
export interface MeasurementValueRequest {
  readonly key: string
  readonly entered: number | null
  /** `Inch`, `Centimetre` or `Count`. Null for a chosen field. */
  readonly unit: string | null
  readonly choice: string | null
  readonly acknowledged: boolean
}

export interface StartMeasurementDraftRequest {
  readonly customerId: string
  readonly measurementTemplateId: string
  readonly reuseFromVersionId: string | null
}

/** Saves one wizard step. The values replace the step; they do not merge into it. */
export interface SaveMeasurementSectionRequest {
  readonly groupName: string
  readonly values: readonly MeasurementValueRequest[]
}

export interface ConfirmMeasurementsRequest {
  readonly reason: string | null
  readonly correctsVersionId: string | null
}
