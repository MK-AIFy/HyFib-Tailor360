import { apiRequest, apiRequestVersioned } from '../auth/apiClient'
import type { VersionedResponse } from '../auth/apiClient'
import type { MeasurementTemplate, TemplateValidation } from './types'

/**
 * Every call the measurement-template screens make, named for what an administrator is doing.
 *
 * ## Why these are not in `adminApi.ts`
 *
 * They are not on the administration surface. Measurement templates belong to the Customers module,
 * and the routes say so: `/api/v1/customers/measurement-templates`, not `/api/v1/admin/…`. Only the
 * *audience* is administrative. Putting them beside the account and branch calls would suggest a
 * boundary the server does not have.
 *
 * ## What every command here carries
 *
 * **The version it is acting on**, in `If-Match`. Since #92 the server answers a missing precondition
 * with `428` rather than proceeding, so this is not defensive — a command without it does not run.
 * The value is the `ETag` of the read the screen rendered, which is the template's, not the
 * version's: an `If-Match` on any of these is a precondition on the whole template.
 *
 * **A retry key**, in `Idempotency-Key`, minted when the person commits to the action and held while
 * it may retry. Minting one here would make it fresh per call, which is the one thing it must not be.
 *
 * The two routes that only *create* — a template, and a draft version — carry the retry key alone.
 * There is nothing yet for them to be stale about, which is the same answer
 * `docs/architecture/conventions.md` section 4.2 gives for an append.
 *
 * ## And what four of them demand of the session
 *
 * Returning, approving, publishing and retiring require step-up: a second factor answered within the
 * last few minutes. A screen does not check that — the server does, and answers `403` with
 * `security.step-up-required` — but it is why an administrator who has been reading a version for
 * twenty minutes is asked to re-authenticate when they finally publish, and why that is right.
 */

const TEMPLATES = '/api/v1/customers/measurement-templates'

/** Reads every template, with the state of each of its versions. Fields are not carried. */
export async function listMeasurementTemplates(
  signal?: AbortSignal,
): Promise<readonly MeasurementTemplate[]> {
  return await apiRequest<readonly MeasurementTemplate[]>(TEMPLATES, {
    ...(signal === undefined ? {} : { signal }),
  })
}

/** Reads one template and every version of it, with their fields, and the version an edit presents. */
export async function readMeasurementTemplate(
  templateId: string,
  signal?: AbortSignal,
): Promise<VersionedResponse<MeasurementTemplate>> {
  return await apiRequestVersioned<MeasurementTemplate>(`${TEMPLATES}/${templateId}`, {
    ...(signal === undefined ? {} : { signal }),
  })
}

/** Runs publish validation without changing anything. Reports every finding, not the first. */
export async function validateTemplateVersion(input: {
  readonly templateId: string
  readonly versionId: string
  readonly signal?: AbortSignal
}): Promise<TemplateValidation> {
  return await apiRequest<TemplateValidation>(
    `${TEMPLATES}/${input.templateId}/versions/${input.versionId}/validation`,
    { ...(input.signal === undefined ? {} : { signal: input.signal }) },
  )
}

/**
 * The five acts that move a version through its life, keyed by their route segment.
 *
 * The order is the order they happen in, which is also the order the screen offers them. `return` is
 * the one that goes backwards, and it is deliberately not called "reject": the version comes back to
 * its author to be changed, and its approval comes back with it.
 */
export const TEMPLATE_LIFECYCLE_ACTIONS = [
  'submit',
  'return',
  'approve',
  'publish',
  'retire',
] as const

export type TemplateLifecycleAction = (typeof TEMPLATE_LIFECYCLE_ACTIONS)[number]

/**
 * Which of them the server demands a written reason for.
 *
 * The three that change what a tailor is asked to measure, or unsay a review. Submitting and
 * approving do not: submitting is the author saying they have finished, and approving is agreement
 * with a version already on screen. The server refuses the other three without one, so the screen
 * collects it in the confirmation rather than discovering the refusal afterwards.
 */
export const TEMPLATE_ACTIONS_NEEDING_REASON: readonly TemplateLifecycleAction[] = [
  'return',
  'publish',
  'retire',
]

/**
 * Which of them the server gates on `catalog.templates.publish` rather than on `catalog.templates.edit`.
 *
 * Submitting is the author saying they have finished, so it needs only the key that let them draft.
 * The other four are the second administrator's acts, and `MeasurementTemplateEndpoints.MapLifecycle`
 * demands the publishing key for each — so a screen that offers them to somebody holding only the
 * edit key is offering four controls that each end in a 403.
 */
export const TEMPLATE_ACTIONS_NEEDING_PUBLISH: readonly TemplateLifecycleAction[] = [
  'return',
  'approve',
  'publish',
  'retire',
]

/** Applies one of the five acts to a version. */
export async function commandTemplateVersion(input: {
  readonly templateId: string
  readonly versionId: string
  readonly action: TemplateLifecycleAction
  readonly reason: string | null
  readonly version: string
  readonly idempotencyKey: string
}): Promise<VersionedResponse<MeasurementTemplate>> {
  return await apiRequestVersioned<MeasurementTemplate>(
    `${TEMPLATES}/${input.templateId}/versions/${input.versionId}/${input.action}`,
    {
      method: 'POST',
      body: { reason: input.reason },
      ifMatch: input.version,
      idempotencyKey: input.idempotencyKey,
    },
  )
}

/**
 * Starts a draft version, empty or copied from an existing one.
 *
 * Cloning is how a published version is changed: it copies the fields with fresh identities and the
 * same keys, so what a screen offers against a published version is this rather than an edit that
 * the database would refuse.
 */
export async function startTemplateDraft(input: {
  readonly templateId: string
  readonly name: string
  readonly notes: string | null
  readonly defaultDisplayUnit: string
  readonly cloneFromVersionId: string | null
  readonly idempotencyKey: string
}): Promise<VersionedResponse<MeasurementTemplate>> {
  return await apiRequestVersioned<MeasurementTemplate>(
    `${TEMPLATES}/${input.templateId}/versions`,
    {
      method: 'POST',
      body: {
        name: input.name,
        notes: input.notes,
        defaultDisplayUnit: input.defaultDisplayUnit,
        cloneFromVersionId: input.cloneFromVersionId,
      },
      idempotencyKey: input.idempotencyKey,
    },
  )
}
