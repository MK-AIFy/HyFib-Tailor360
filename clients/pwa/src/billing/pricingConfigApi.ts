import { apiRequest, apiRequestVersioned } from '../auth/apiClient'
import type { VersionedResponse } from '../auth/apiClient'
import type {
  GstRegistration,
  GstRegistrationRequest,
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

/** Every tax configuration version, newest first. Drafting, editing and publishing are E09-F01-5/-5b. */
export async function listTaxConfigurationVersions(
  signal?: AbortSignal,
): Promise<readonly TaxConfigurationSummary[]> {
  return await apiRequest<readonly TaxConfigurationSummary[]>(
    `${BILLING}/tax-configuration/versions`,
    { ...(signal === undefined ? {} : { signal }) },
  )
}
