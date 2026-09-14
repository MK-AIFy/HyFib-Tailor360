import { BRANCH_ID } from './fixtures'
import type { PriceList, PriceListVersionSummary } from '../priceListTypes'

/**
 * Synthetic fixtures for the price-list register (#252). No real code, name, rate or threshold
 * appears here or in any story or test built on them.
 */

export const PRICE_LIST_ID = '0199dd00-0000-7000-8000-000000007001'
export const PRICE_LIST_VERSION_ID = '0199dd00-0000-7000-8000-000000007002'

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
