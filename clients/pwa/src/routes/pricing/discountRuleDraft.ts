import { parseDecimalString } from '../../i18n/parseNumber'
import type { DiscountRule, DiscountRuleRequest } from '../../billing/priceListTypes'

/** How a discount rule states its value, in the order every screen offers them (`DiscountKind`). */
export const DISCOUNT_KINDS = ['Percentage', 'Amount'] as const

export type DiscountKindValue = (typeof DISCOUNT_KINDS)[number]

/**
 * A discount rule as the form holds it.
 *
 * `kind` and `active` start as `''` — neither is pre-selected, for the same reason
 * `PriceListItemDraft`'s own do: the server refuses either omitted (`billing.value-required`), and
 * what a rule discounts is not this product's to assume. Both bounds start blank: a discount
 * boundary is the owner's decision with the accountant, and this screen never suggests one. Neither
 * bound is ever compared to the other here — that check is the server's alone
 * (`billing.discount-bounds-not-ordered`).
 */
export interface DiscountRuleDraft {
  readonly code: string
  readonly description: string
  readonly kind: '' | DiscountKindValue
  readonly maximumWithoutApproval: string
  readonly maximum: string
  /** `''` (not chosen), `'true'` or `'false'`. */
  readonly active: string
  readonly reason: string
}

/** A blank draft, for adding a rule with nothing carried over. */
export function blankDiscountRuleDraft(): DiscountRuleDraft {
  return {
    code: '',
    description: '',
    kind: '',
    maximumWithoutApproval: '',
    maximum: '',
    active: '',
    reason: '',
  }
}

/** A row as the form holds it, so an edit starts from what is stored. */
export function draftFromDiscountRule(rule: DiscountRule): DiscountRuleDraft {
  return {
    code: rule.code,
    description: rule.description,
    kind: (DISCOUNT_KINDS as readonly string[]).includes(rule.kind)
      ? (rule.kind as DiscountKindValue)
      : '',
    maximumWithoutApproval: String(rule.maximumWithoutApproval),
    maximum: String(rule.maximum),
    active: rule.active ? 'true' : 'false',
    reason: '',
  }
}

/** The request body, once `kind` and `active` have both been explicitly chosen. */
export function discountRuleRequestFrom(draft: DiscountRuleDraft): DiscountRuleRequest {
  return {
    code: draft.code.trim(),
    description: draft.description.trim(),
    kind: draft.kind === '' ? null : draft.kind,
    maximumWithoutApproval: parseDecimalString(draft.maximumWithoutApproval),
    maximum: parseDecimalString(draft.maximum),
    active: draft.active === 'true',
    reason: draft.reason.trim() === '' ? null : draft.reason.trim(),
  }
}
