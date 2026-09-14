import { BRANCH_ID } from './fixtures'
import type {
  DiscountRule,
  PriceList,
  PriceListItem,
  PriceListPublication,
  PriceListVersion,
  PriceListVersionSummary,
} from '../priceListTypes'

/**
 * Synthetic fixtures for the price-list register (#252) and its version editor (E09-F01-7, #268). No
 * real code, name, rate or threshold appears here or in any story or test built on them.
 */

export const PRICE_LIST_ID = '0199dd00-0000-7000-8000-000000007001'
export const PRICE_LIST_VERSION_ID = '0199dd00-0000-7000-8000-000000007002'
export const PRICE_LIST_ITEM_ID = '0199dd00-0000-7000-8000-000000007003'
export const PRICE_LIST_ITEM_KEY = '0199dd00-0000-7000-8000-000000007004'
export const DISCOUNT_RULE_ID = '0199dd00-0000-7000-8000-000000007005'
export const DISCOUNT_RULE_KEY = '0199dd00-0000-7000-8000-000000007006'

/** One price list, by its immutable code. */
export function aPriceList(overrides: Partial<PriceList> = {}): PriceList {
  return {
    priceListId: PRICE_LIST_ID,
    code: 'PL_CBE01',
    name: 'Coimbatore price list',
    createdAt: '2026-03-20T05:00:00.000Z',
    updatedAt: '2026-03-20T05:00:00.000Z',
    ...overrides,
  }
}

/** One published version, pricing one branch, tax-exclusive with a ten percent override threshold. */
export function aPriceListVersionSummary(
  overrides: Partial<PriceListVersionSummary> = {},
): PriceListVersionSummary {
  return {
    priceListVersionId: PRICE_LIST_VERSION_ID,
    priceListId: PRICE_LIST_ID,
    versionNumber: 1,
    name: 'Rates from 1 April 2026',
    notes: null,
    status: 'Published',
    effectiveFrom: '2026-04-01',
    taxInclusive: false,
    roundOff: 'NearestRupee',
    overrideThresholdPercent: 10,
    branchIds: [BRANCH_ID],
    clonedFromVersionId: null,
    createdAt: '2026-03-20T05:00:00.000Z',
    publishedAt: '2026-03-25T05:00:00.000Z',
    retiredAt: null,
    ...overrides,
  }
}

/** One price-list item: a stitching service's base charge, active, per piece. */
export function aPriceListItem(overrides: Partial<PriceListItem> = {}): PriceListItem {
  return {
    priceListItemId: PRICE_LIST_ITEM_ID,
    priceListItemKey: PRICE_LIST_ITEM_KEY,
    code: 'STITCH_BLOUSE',
    description: 'Blouse stitching',
    kind: 'Service',
    baseRate: 505,
    unit: 'each',
    taxCode: 'STITCHING_5',
    active: true,
    ...overrides,
  }
}

/** One discount rule, read-only in this issue — a festive percentage discount within a fixed cap. */
export function aDiscountRule(overrides: Partial<DiscountRule> = {}): DiscountRule {
  return {
    discountRuleId: DISCOUNT_RULE_ID,
    discountRuleKey: DISCOUNT_RULE_KEY,
    code: 'FESTIVE10',
    description: 'Festive season discount',
    kind: 'Percentage',
    maximumWithoutApproval: 10,
    maximum: 20,
    active: true,
    ...overrides,
  }
}

/** One price-list version and everything in it — a draft carrying one item, no discount rule yet. */
export function aPriceListVersion(overrides: Partial<PriceListVersion> = {}): PriceListVersion {
  return {
    version: aPriceListVersionSummary({ status: 'Draft', publishedAt: null }),
    items: [aPriceListItem()],
    discountRules: [],
    ...overrides,
  }
}

/**
 * A successful publish's own answer (E09-F01-7b): the version now published, what it superseded, its
 * findings. `aBillingFinding` and `findingsProblem` are reused unchanged from `pricingConfigFixtures.ts`
 * rather than forked here — a finding is the same shape whichever module's publish returned it.
 */
export function aPriceListPublication(
  overrides: Partial<PriceListPublication> = {},
): PriceListPublication {
  return {
    published: aPriceListVersion({
      version: aPriceListVersionSummary({
        status: 'Published',
        publishedAt: '2026-04-01T05:00:00.000Z',
      }),
    }),
    supersededVersionId: null,
    findings: [],
    ...overrides,
  }
}
