import { parseDecimalString } from '../../i18n/parseNumber'
import type { PriceListItem, PriceListItemRequest } from '../../billing/priceListTypes'

/** What a price-list item prices, in the order every screen offers them (`PriceItemKind`). */
export const PRICE_ITEM_KINDS = ['Service', 'Surcharge', 'Material'] as const

export type PriceItemKindValue = (typeof PRICE_ITEM_KINDS)[number]

/**
 * A price-list item as the form holds it.
 *
 * `kind` and `active` start as `''` — neither is pre-selected. `AddPriceListItem` refuses an omitted
 * `active` rather than defaulting it ("a money-bearing flag is never defaulted", #41), and an omitted
 * `kind` is refused the same way (`billing.value-required`) because what a line prices is not this
 * product's to assume. `baseRate` and `unit` start blank: no rate and no unit is ever suggested, since
 * both are what a customer is charged.
 */
export interface PriceListItemDraft {
  readonly code: string
  readonly description: string
  readonly kind: '' | PriceItemKindValue
  readonly baseRate: string
  readonly unit: string
  readonly taxCode: string
  /** `''` (not chosen), `'true'` or `'false'`. */
  readonly active: string
  readonly reason: string
}

/** A blank draft, for adding an item with nothing carried over. */
export function blankPriceListItemDraft(): PriceListItemDraft {
  return {
    code: '',
    description: '',
    kind: '',
    baseRate: '',
    unit: '',
    taxCode: '',
    active: '',
    reason: '',
  }
}

/** A row as the form holds it, so an edit starts from what is stored. */
export function draftFromPriceListItem(item: PriceListItem): PriceListItemDraft {
  return {
    code: item.code,
    description: item.description,
    kind: (PRICE_ITEM_KINDS as readonly string[]).includes(item.kind)
      ? (item.kind as PriceItemKindValue)
      : '',
    baseRate: String(item.baseRate),
    unit: item.unit,
    taxCode: item.taxCode,
    active: item.active ? 'true' : 'false',
    reason: '',
  }
}

/** The request body, once `kind` and `active` have both been explicitly chosen. */
export function priceListItemRequestFrom(draft: PriceListItemDraft): PriceListItemRequest {
  return {
    code: draft.code.trim(),
    description: draft.description.trim(),
    kind: draft.kind === '' ? null : draft.kind,
    baseRate: parseDecimalString(draft.baseRate),
    unit: draft.unit.trim(),
    taxCode: draft.taxCode.trim(),
    active: draft.active === 'true',
    reason: draft.reason.trim() === '' ? null : draft.reason.trim(),
  }
}
