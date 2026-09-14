import type { BillingFinding } from './pricingAdminTypes'

/**
 * The price-list register payloads (#252) and the version editor's own payloads (E09-F01-7): a price
 * list, its versions' conventions, and one version's items and discount rules — pinned against the
 * published contract in `api/contract.ts`.
 *
 * Every decimal member is typed `number | string`, because the published schema types a decimal as
 * `{"type": ["number", "string"]}` — `overrideThresholdPercent`, `versionNumber`, `baseRate`,
 * `maximumWithoutApproval` and `maximum` are the ones here. No arithmetic is ever done on one; amounts,
 * rates and dates are rendered through `getFormatters()`.
 *
 * The validation report (`BillingValidationReport`, declared in `pricingAdminTypes.ts`) and
 * `PriceListPublication` below are E09-F01-7b's. `DiscountRule` and `DiscountRuleRequest` are the
 * discount-rule editor's own (E09-F01-8).
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

/* The version editor: its items and discount rules (E09-F01-7). ---------------------------------- */

/** One price-list item: a service's base charge, a surcharge or a material. */
export interface PriceListItem {
  readonly priceListItemId: string
  /** Stable across the item's own edits, the way `taxCodeKey` is stable across a tax code's. */
  readonly priceListItemKey: string
  readonly code: string
  readonly description: string
  /** `Service`, `Surcharge` or `Material`. */
  readonly kind: string
  readonly baseRate: number | string
  readonly unit: string
  /** A tax code of the published tax configuration — checked at publication, not at save. */
  readonly taxCode: string
  /** Whether the item may be priced. A retired item stays readable. */
  readonly active: boolean
}

/**
 * What is sent to add or replace an item. Whole-value: an omitted field is refused, not kept, and
 * `active` is refused when omitted rather than defaulted — a money-bearing flag is never assumed.
 */
export interface PriceListItemRequest {
  readonly code: string | null
  readonly description: string | null
  readonly kind: string | null
  readonly baseRate: number | string | null
  readonly unit: string | null
  readonly taxCode: string | null
  readonly active: boolean | null
  readonly reason: string | null
}

/**
 * One discount rule of a version, as the editor reads it — writable on a draft, read-only on a
 * published or retired one (E09-F01-8): what a counter may take off a line, and what nobody may
 * exceed.
 */
export interface DiscountRule {
  readonly discountRuleId: string
  readonly discountRuleKey: string
  readonly code: string
  readonly description: string
  /** `Percentage` or `Amount`. */
  readonly kind: string
  readonly maximumWithoutApproval: number | string
  readonly maximum: number | string
  readonly active: boolean
}

/**
 * What is sent to add or replace a discount rule. Whole-value: an omitted field is refused, not
 * kept, and `active` is refused when omitted rather than defaulted — a money-bearing flag is never
 * assumed. Neither bound is compared to the other here: the ordering rule
 * (`billing.discount-bounds-not-ordered`) is the server's alone.
 */
export interface DiscountRuleRequest {
  readonly code: string | null
  readonly description: string | null
  readonly kind: string | null
  readonly maximumWithoutApproval: number | string | null
  readonly maximum: number | string | null
  readonly active: boolean | null
  readonly reason: string | null
}

/** A price-list version and everything in it — its conventions, its items and its discount rules. */
export interface PriceListVersion {
  readonly version: PriceListVersionSummary
  readonly items: readonly PriceListItem[]
  readonly discountRules: readonly DiscountRule[]
}

/* Validating and publishing a version (E09-F01-7b). ----------------------------------------------- */

/**
 * What a publish answered with: the version now published, the one it superseded (null the first time
 * a list's branches are ever priced), and every finding the publication returned — warnings included.
 * A successful publish can still carry a warning (OD-19's default for an unpriced branch), which is
 * why this is not just `PriceListVersion`.
 */
export interface PriceListPublication {
  readonly published: PriceListVersion
  readonly supersededVersionId: string | null
  readonly findings: readonly BillingFinding[]
}
