/**
 * The price-list register payloads (#252): a price list and its versions' conventions, pinned
 * against the published contract in `api/contract.ts`.
 *
 * Every decimal member is typed `number | string`, because the published schema types a decimal as
 * `{"type": ["number", "string"]}` — `overrideThresholdPercent` and `versionNumber` are the two here.
 * No arithmetic is ever done on one; amounts, rates and dates are rendered through `getFormatters()`.
 *
 * Items, discount rules, the validation report and publication are E09-F01-7 and E09-F01-7b; nothing
 * here types a price-list item or a discount rule.
 */

/** A price list, as the register lists it — a code and a name, never deleted. */
export interface PriceList {
  readonly priceListId: string
  readonly code: string
  readonly name: string
  readonly createdAt: string
  readonly updatedAt: string
}

/**
 * What is sent to create a price list.
 *
 * The code is asked for here and nowhere else: once a list exists, seeds and exports refer to it, so
 * `RenamePriceListRequest` offers no way to change it.
 */
export interface CreatePriceListRequest {
  readonly code: string | null
  readonly name: string | null
  readonly reason: string | null
}

/** What is sent to rename a price list. The code is immutable, so only the name is offered. */
export interface RenamePriceListRequest {
  readonly name: string | null
  readonly reason: string | null
}

/** One version of a price list, as the register lists it — newest first, read only here. */
export interface PriceListVersionSummary {
  readonly priceListVersionId: string
  readonly priceListId: string
  readonly versionNumber: number | string
  readonly name: string
  readonly notes: string | null
  readonly status: string
  readonly effectiveFrom: string
  readonly taxInclusive: boolean
  readonly roundOff: string
  readonly overrideThresholdPercent: number | string
  readonly branchIds: readonly string[]
  readonly clonedFromVersionId: string | null
  readonly createdAt: string
  readonly publishedAt: string | null
  readonly retiredAt: string | null
}

/**
 * What is sent to start a version's draft.
 *
 * The whole convention set, and no member is defaulted: an omitted `taxInclusive` or `branchIds` is
 * refused with `billing.value-required`, because each is a money-bearing choice this client must
 * never make on somebody's behalf. `saysTaxInclusive` and `saysBranchIds` are how the client tells the
 * server "this is the real answer" — the only way to say `branchIds: []` on purpose (a draft pricing
 * nowhere yet) rather than "not answered". Every other member has no such flag: a blank `roundOff` or
 * `effectiveFrom` is unambiguous, because null is never itself a valid convention.
 */
export interface PriceListVersionRequest {
  readonly name: string | null
  readonly notes: string | null
  readonly effectiveFrom: string | null
  readonly taxInclusive: boolean | null
  readonly roundOff: string | null
  readonly overrideThresholdPercent: number | string | null
  readonly branchIds: readonly string[] | null
  readonly cloneFromVersionId: string | null
  readonly reason: string | null
  readonly saysTaxInclusive: boolean
  readonly saysBranchIds: boolean
}
