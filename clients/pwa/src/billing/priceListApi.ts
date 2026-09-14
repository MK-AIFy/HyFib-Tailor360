import { apiRequest, apiRequestVersioned } from '../auth/apiClient'
import type { VersionedResponse } from '../auth/apiClient'
import type {
  CreatePriceListRequest,
  PriceList,
  PriceListVersionRequest,
  PriceListVersionSummary,
  RenamePriceListRequest,
} from './priceListTypes'

/**
 * The price-list register (#252): every price list the organisation has, a create and a rename, one
 * list's versions, and the act that starts a version's draft. Every write is on
 * `BILLING_PERMISSIONS.managePriceLists`, organisation scope, and carries an `Idempotency-Key` the
 * caller mints and holds across a retry.
 *
 * Items, discount rules, the validation report and publication are E09-F01-7 and E09-F01-7b, so
 * nothing here calls `AddPriceListItem`, `PublishPriceListVersion` or their four siblings.
 */

const BILLING = '/api/v1/billing'

/** Every price list the organisation has, by code. */
export async function listPriceLists(signal?: AbortSignal): Promise<readonly PriceList[]> {
  return await apiRequest<readonly PriceList[]>(`${BILLING}/price-lists`, {
    ...(signal === undefined ? {} : { signal }),
  })
}

/** Creates a price list. The code is never asked for again — see `renamePriceList`. */
export async function createPriceList(input: {
  readonly body: CreatePriceListRequest
  readonly idempotencyKey: string
}): Promise<PriceList> {
  return await apiRequest<PriceList>(`${BILLING}/price-lists`, {
    method: 'POST',
    body: input.body,
    idempotencyKey: input.idempotencyKey,
  })
}

/** Reads one price list, with the version a rename is made against. */
export async function readPriceList(
  priceListId: string,
  signal?: AbortSignal,
): Promise<VersionedResponse<PriceList>> {
  return await apiRequestVersioned<PriceList>(`${BILLING}/price-lists/${priceListId}`, {
    ...(signal === undefined ? {} : { signal }),
  })
}

/**
 * Renames a price list. The code is immutable — seeds and exports refer to it — so a rename is the
 * only change a price list itself ever takes.
 */
export async function renamePriceList(input: {
  readonly priceListId: string
  readonly body: RenamePriceListRequest
  readonly version: string
  readonly idempotencyKey: string
}): Promise<VersionedResponse<PriceList>> {
  return await apiRequestVersioned<PriceList>(`${BILLING}/price-lists/${input.priceListId}`, {
    method: 'PUT',
    body: input.body,
    ifMatch: input.version,
    idempotencyKey: input.idempotencyKey,
  })
}

/** Every version of one price list, newest first. */
export async function listPriceListVersions(
  priceListId: string,
  signal?: AbortSignal,
): Promise<readonly PriceListVersionSummary[]> {
  return await apiRequest<readonly PriceListVersionSummary[]>(
    `${BILLING}/price-lists/${priceListId}/versions`,
    { ...(signal === undefined ? {} : { signal }) },
  )
}

/** The `201`'s own body: a version's items and discount rules are E09-F01-7's to read, not this issue's. */
interface PriceListVersionResponse {
  readonly version: PriceListVersionSummary
}

/**
 * Starts a version's draft, empty or cloned from another version of the same list.
 *
 * The response carries the new version's own `ETag` — the token its own next edit is made against —
 * which this issue holds and returns but does not yet use: editing a version is E09-F01-7.
 */
export async function createPriceListDraft(input: {
  readonly priceListId: string
  readonly body: PriceListVersionRequest
  readonly idempotencyKey: string
}): Promise<VersionedResponse<PriceListVersionSummary>> {
  const response = await apiRequestVersioned<PriceListVersionResponse>(
    `${BILLING}/price-lists/${input.priceListId}/versions`,
    {
      method: 'POST',
      body: input.body,
      idempotencyKey: input.idempotencyKey,
    },
  )
  return { value: response.value.version, version: response.version }
}
