import { apiRequest, apiRequestVersioned } from '../auth/apiClient'
import type { VersionedResponse } from '../auth/apiClient'
import type {
  BillingValidationReport,
  CreateTaxConfigurationDraftRequest,
  DescribeTaxConfigurationRequest,
  GstRegistration,
  GstRegistrationRequest,
  TaxCode,
  TaxCodeRequest,
  TaxConfiguration,
  TaxConfigurationPublication,
  TaxConfigurationSummary,
} from './pricingAdminTypes'

/**
 * The pricing administration foundation (#237): the GST registration register and the tax
 * configuration version list. Every write is on `BILLING_PERMISSIONS.managePriceLists`, organisation
 * scope, and carries an `Idempotency-Key` the caller mints and holds across a retry.
 *
 * This is the first of eight client slices against #41's pricing surface. It reads two endpoint
 * groups only — `gst-registrations` and `tax-configuration/versions` — and adds no server call of
 * its own: every route it calls is already published and unchanged by this issue.
 *
 * E09-F01-5 appends the six tax configuration routes a draft is edited through: starting one,
 * reading one version, describing it and adding, editing and removing a tax code. Every write against
 * an existing version carries `If-Match`, and every sub-resource write's `VersionedResponse` carries
 * the version's *own* tag, moved by the child write — never the child's own concurrency token, because
 * a tax code has none of its own.
 */

const BILLING = '/api/v1/billing'

/** The organisation's GST registrations, by branch and first day. */
export async function listGstRegistrations(
  signal?: AbortSignal,
): Promise<readonly GstRegistration[]> {
  return await apiRequest<readonly GstRegistration[]>(`${BILLING}/gst-registrations`, {
    ...(signal === undefined ? {} : { signal }),
  })
}

/** Reads one registration, with the version an amendment must present. */
export async function readGstRegistration(
  registrationId: string,
  signal?: AbortSignal,
): Promise<VersionedResponse<GstRegistration>> {
  return await apiRequestVersioned<GstRegistration>(
    `${BILLING}/gst-registrations/${registrationId}`,
    { ...(signal === undefined ? {} : { signal }) },
  )
}

/**
 * Records a branch's GST registration. At most one registration of a branch is in force on any day;
 * an overlap is refused with `billing.registration-overlaps`.
 */
export async function recordGstRegistration(input: {
  readonly body: GstRegistrationRequest
  readonly idempotencyKey: string
}): Promise<GstRegistration> {
  return await apiRequest<GstRegistration>(`${BILLING}/gst-registrations`, {
    method: 'POST',
    body: input.body,
    idempotencyKey: input.idempotencyKey,
  })
}

/**
 * Amends a registration's dates, names and number. The branch never changes, but the body is
 * whole-value, so the caller re-sends every field, including the one that is read-only on screen.
 *
 * A registration is never deleted: a branch re-registered under a new number ends this one with an
 * `effectiveTo` the day before and records the new one as a separate registration.
 */
export async function amendGstRegistration(input: {
  readonly registrationId: string
  readonly body: GstRegistrationRequest
  readonly version: string
  readonly idempotencyKey: string
}): Promise<VersionedResponse<GstRegistration>> {
  return await apiRequestVersioned<GstRegistration>(
    `${BILLING}/gst-registrations/${input.registrationId}`,
    {
      method: 'PUT',
      body: input.body,
      ifMatch: input.version,
      idempotencyKey: input.idempotencyKey,
    },
  )
}

/** Every tax configuration version, newest first. Publishing a version is E09-F01-5b. */
export async function listTaxConfigurationVersions(
  signal?: AbortSignal,
): Promise<readonly TaxConfigurationSummary[]> {
  return await apiRequest<readonly TaxConfigurationSummary[]>(
    `${BILLING}/tax-configuration/versions`,
    { ...(signal === undefined ? {} : { signal }) },
  )
}

/**
 * Starts a draft tax configuration version, empty or cloned from an existing one.
 *
 * Cloning the published version is the ordinary way to change what is in force: a published version
 * is immutable, so a rate change is a clone, an edit here and a publication in E09-F01-5b.
 */
export async function createTaxConfigurationDraft(input: {
  readonly body: CreateTaxConfigurationDraftRequest
  readonly idempotencyKey: string
}): Promise<TaxConfiguration> {
  return await apiRequest<TaxConfiguration>(`${BILLING}/tax-configuration/versions`, {
    method: 'POST',
    body: input.body,
    idempotencyKey: input.idempotencyKey,
  })
}

/** Reads one tax configuration version and its codes, with the tag the next write must present. */
export async function readTaxConfigurationVersion(
  versionId: string,
  signal?: AbortSignal,
): Promise<VersionedResponse<TaxConfiguration>> {
  return await apiRequestVersioned<TaxConfiguration>(
    `${BILLING}/tax-configuration/versions/${versionId}`,
    { ...(signal === undefined ? {} : { signal }) },
  )
}

/** Changes a draft's name, notes and effective date. Refused on a published or retired version. */
export async function describeTaxConfigurationVersion(input: {
  readonly versionId: string
  readonly body: DescribeTaxConfigurationRequest
  readonly version: string
  readonly idempotencyKey: string
}): Promise<VersionedResponse<TaxConfiguration>> {
  return await apiRequestVersioned<TaxConfiguration>(
    `${BILLING}/tax-configuration/versions/${input.versionId}`,
    {
      method: 'PUT',
      body: input.body,
      ifMatch: input.version,
      idempotencyKey: input.idempotencyKey,
    },
  )
}

/**
 * Adds a tax code to a draft.
 *
 * The version's row moved with its child: the tag the caller sent is stale, and the one on this
 * response is what the next write against the version — including the next tax code write — must
 * present.
 */
export async function addTaxCode(input: {
  readonly versionId: string
  readonly code: TaxCodeRequest
  readonly version: string
  readonly idempotencyKey: string
}): Promise<VersionedResponse<TaxCode>> {
  return await apiRequestVersioned<TaxCode>(
    `${BILLING}/tax-configuration/versions/${input.versionId}/tax-codes`,
    {
      method: 'POST',
      body: input.code,
      ifMatch: input.version,
      idempotencyKey: input.idempotencyKey,
    },
  )
}

/** Replaces what a draft says about a tax code. Whole-value: an omitted rate list is nil-rated. */
export async function editTaxCode(input: {
  readonly versionId: string
  readonly taxCodeId: string
  readonly code: TaxCodeRequest
  readonly version: string
  readonly idempotencyKey: string
}): Promise<VersionedResponse<TaxCode>> {
  return await apiRequestVersioned<TaxCode>(
    `${BILLING}/tax-configuration/versions/${input.versionId}/tax-codes/${input.taxCodeId}`,
    {
      method: 'PUT',
      body: input.code,
      ifMatch: input.version,
      idempotencyKey: input.idempotencyKey,
    },
  )
}

/**
 * Removes a tax code from a draft, with the reason the trail records.
 *
 * Answers `204`, and the version's moved tag still comes back on it — a caller that discarded the
 * tag on an empty body would hold a precondition that is no longer current for its very next write.
 */
export async function removeTaxCode(input: {
  readonly versionId: string
  readonly taxCodeId: string
  readonly reason: string | null
  readonly version: string
  readonly idempotencyKey: string
}): Promise<VersionedResponse<void>> {
  return await apiRequestVersioned<void>(
    `${BILLING}/tax-configuration/versions/${input.versionId}/tax-codes/${input.taxCodeId}/delete`,
    {
      method: 'POST',
      body: { reason: input.reason },
      ifMatch: input.version,
      idempotencyKey: input.idempotencyKey,
    },
  )
}

/**
 * Runs the publication checks against a version without publishing it (E09-F01-5b). Every check this
 * module runs is an error today, but the report's shape carries a warning too, for the day one exists.
 */
export async function validateTaxConfigurationVersion(
  versionId: string,
  signal?: AbortSignal,
): Promise<BillingValidationReport> {
  return await apiRequest<BillingValidationReport>(
    `${BILLING}/tax-configuration/versions/${versionId}/validation`,
    { ...(signal === undefined ? {} : { signal }) },
  )
}

/**
 * Publishes a draft: step-up, a mandatory reason, and the race two administrators lose
 * (`billing.publish-conflict`). Refused with `billing.publish-validation-failed` and every finding,
 * warnings included, when the publication checks are not satisfied.
 */
export async function publishTaxConfigurationVersion(input: {
  readonly versionId: string
  readonly reason: string | null
  readonly version: string
  readonly idempotencyKey: string
}): Promise<VersionedResponse<TaxConfigurationPublication>> {
  return await apiRequestVersioned<TaxConfigurationPublication>(
    `${BILLING}/tax-configuration/versions/${input.versionId}/publish`,
    {
      method: 'POST',
      body: { reason: input.reason },
      ifMatch: input.version,
      idempotencyKey: input.idempotencyKey,
    },
  )
}
