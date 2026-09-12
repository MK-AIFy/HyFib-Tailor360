import { apiRequest, apiRequestVersioned } from '../auth/apiClient'
import type { VersionedResponse } from '../auth/apiClient'
import type {
  ConfirmMeasurementsRequest,
  MeasurementCaptureTemplate,
  MeasurementCheck,
  MeasurementDraft,
  MeasurementVersion,
  SaveMeasurementSectionRequest,
  StartMeasurementDraftRequest,
} from './types'

/**
 * Every call the capture wizard makes, named for what a person at the counter is doing.
 *
 * ## What every command here carries
 *
 * **A retry key**, in `Idempotency-Key`, minted when the person commits to the act and held until it
 * succeeds — across a lost answer, an in-place re-authentication and a refusal alike. Confirming is
 * the one act on this surface that must never happen twice, and the key is what turns a retry into
 * a replay of the first outcome rather than a second measurement.
 *
 * **The draft as it was last seen**, in `If-Match`, on a section save and on the confirmation.
 * Drafts are shared within a branch (`invariants.md` section 4.3), so the tag is how two people
 * measuring one garment between them avoid overwriting each other: a stale one answers
 * `409 measurements.draft-changed`, and the screen says so in words and offers the re-read.
 */

const DRAFTS = '/api/v1/customers/measurement-drafts'

/** Starts measuring, or hands back the draft already open for this customer and template. */
export async function startMeasurementDraft(input: {
  readonly body: StartMeasurementDraftRequest
  readonly idempotencyKey: string
}): Promise<VersionedResponse<MeasurementDraft>> {
  return await apiRequestVersioned<MeasurementDraft>(DRAFTS, {
    method: 'POST',
    body: input.body,
    idempotencyKey: input.idempotencyKey,
  })
}

/** Reads a draft with the tag the next save must present. */
export async function readMeasurementDraft(
  draftId: string,
  signal?: AbortSignal,
): Promise<VersionedResponse<MeasurementDraft>> {
  return await apiRequestVersioned<MeasurementDraft>(`${DRAFTS}/${draftId}`, {
    ...(signal === undefined ? {} : { signal }),
  })
}

/** Reads the template version a draft is pinned to, which is what the wizard renders through. */
export async function readMeasurementDraftTemplate(
  draftId: string,
  signal?: AbortSignal,
): Promise<MeasurementCaptureTemplate> {
  return await apiRequest<MeasurementCaptureTemplate>(`${DRAFTS}/${draftId}/template`, {
    ...(signal === undefined ? {} : { signal }),
  })
}

/** Saves one step. Replaces the step's values, which is what lets a value be cleared. */
export async function saveMeasurementSection(input: {
  readonly draftId: string
  readonly body: SaveMeasurementSectionRequest
  readonly version: string
  readonly idempotencyKey: string
}): Promise<VersionedResponse<MeasurementDraft>> {
  return await apiRequestVersioned<MeasurementDraft>(`${DRAFTS}/${input.draftId}/sections`, {
    method: 'POST',
    body: input.body,
    ifMatch: input.version,
    idempotencyKey: input.idempotencyKey,
  })
}

/** Asks what stands between the draft and a confirmed measurement. Changes nothing. */
export async function checkMeasurementDraft(
  draftId: string,
  signal?: AbortSignal,
): Promise<MeasurementCheck> {
  return await apiRequest<MeasurementCheck>(`${DRAFTS}/${draftId}/check`, {
    ...(signal === undefined ? {} : { signal }),
  })
}

/** Turns the draft into the immutable record of a measurement. */
export async function confirmMeasurements(input: {
  readonly draftId: string
  readonly body: ConfirmMeasurementsRequest
  readonly version: string
  readonly idempotencyKey: string
}): Promise<MeasurementVersion> {
  return await apiRequest<MeasurementVersion>(`${DRAFTS}/${input.draftId}/confirm`, {
    method: 'POST',
    body: input.body,
    ifMatch: input.version,
    idempotencyKey: input.idempotencyKey,
  })
}
