import { apiRequest, apiRequestVersioned } from '../auth/apiClient'
import type { VersionedResponse } from '../auth/apiClient'
import type {
  CreatePriceListRequest,
  PriceList,
  PriceListItem,
  PriceListItemRequest,
  PriceListVersion,
  PriceListVersionRequest,
  PriceListVersionSummary,
  RenamePriceListRequest,
} from './priceListTypes'

/**
 * The price-list register (#252) and its version editor (E09-F01-7): every price list the
 * organisation has, a create and a rename, one list's versions, the act that starts a version's
 * draft, and the five routes the editor reads and writes — the version itself, whole-value, and its
 * items. Every write is on `BILLING_PERMISSIONS.managePriceLists`, organisation scope, and carries an
 * `Idempotency-Key` the caller mints and holds across a retry.
 *
 * The validation report and publication (`ValidatePriceListVersion`, `PublishPriceListVersion`) and
 * the three discount-rule routes are E09-F01-7b's and E09-F01-8's; nothing here calls them.
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

/* The version editor: the version itself, and its items (E09-F01-7). ----------------------------- */

/**
 * Reads one price-list version and everything in it — its conventions, its items and its discount
 * rules, whatever its status — with the tag every change to it is made against.
 */
export async function readPriceListVersion(
  versionId: string,
  signal?: AbortSignal,
): Promise<VersionedResponse<PriceListVersion>> {
  return await apiRequestVersioned<PriceListVersion>(
    `${BILLING}/price-lists/versions/${versionId}`,
    { ...(signal === undefined ? {} : { signal }) },
  )
}

/**
 * Changes a draft's name, notes, effective date, conventions and branches — whole-value: every field
 * is re-sent, and a field left out is refused, not kept (`billing.value-required` on `taxInclusive` or
 * `branchIds`). Refused on a published or retired version (`billing.version-not-editable`).
 */
export async function describePriceListVersion(input: {
  readonly versionId: string
  readonly body: PriceListVersionRequest
  readonly version: string
  readonly idempotencyKey: string
}): Promise<VersionedResponse<PriceListVersion>> {
  return await apiRequestVersioned<PriceListVersion>(
    `${BILLING}/price-lists/versions/${input.versionId}`,
    {
      method: 'PUT',
      body: input.body,
      ifMatch: input.version,
      idempotencyKey: input.idempotencyKey,
    },
  )
}

/**
 * Adds an item to a draft — a service's base charge, a surcharge or a material.
 *
 * The version's row moved with its child: the tag the caller sent is stale, and the one on this
 * response is what the next write against the version — including the next item write — must present.
 */
export async function addPriceListItem(input: {
  readonly versionId: string
  readonly item: PriceListItemRequest
  readonly version: string
  readonly idempotencyKey: string
}): Promise<VersionedResponse<PriceListItem>> {
  return await apiRequestVersioned<PriceListItem>(
    `${BILLING}/price-lists/versions/${input.versionId}/items`,
    {
      method: 'POST',
      body: input.item,
      ifMatch: input.version,
      idempotencyKey: input.idempotencyKey,
    },
  )
}

/** Replaces what a draft says about an item, whole-value. */
export async function editPriceListItem(input: {
  readonly versionId: string
  readonly itemId: string
  readonly item: PriceListItemRequest
  readonly version: string
  readonly idempotencyKey: string
}): Promise<VersionedResponse<PriceListItem>> {
  return await apiRequestVersioned<PriceListItem>(
    `${BILLING}/price-lists/versions/${input.versionId}/items/${input.itemId}`,
    {
      method: 'PUT',
      body: input.item,
      ifMatch: input.version,
      idempotencyKey: input.idempotencyKey,
    },
  )
}

/**
 * Removes an item from a draft, with the reason the trail records.
 *
 * Answers `204`, and the version's moved tag still comes back on it — a caller that discarded the tag
 * on an empty body would hold a precondition that is no longer current for its very next write.
 */
export async function removePriceListItem(input: {
  readonly versionId: string
  readonly itemId: string
  readonly reason: string | null
  readonly version: string
  readonly idempotencyKey: string
}): Promise<VersionedResponse<void>> {
  return await apiRequestVersioned<void>(
    `${BILLING}/price-lists/versions/${input.versionId}/items/${input.itemId}/delete`,
    {
      method: 'POST',
      body: { reason: input.reason },
      ifMatch: input.version,
      idempotencyKey: input.idempotencyKey,
    },
  )
}
