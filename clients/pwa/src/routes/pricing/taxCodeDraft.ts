import { parseDecimalString } from '../../i18n/parseNumber'
import type { TaxCode, TaxCodeRequest, TaxRateRequest } from '../../billing/pricingAdminTypes'
import type { MessageKey } from '../../i18n/en-IN'

/**
 * The four tax components a code's rates are entered against, in the fixed order every screen shows
 * them — which is also the order they are sent in, so a server field error naming an array index
 * (`rates[2].ratePercent`) can be matched back to the row that produced it.
 */
export const RATE_COMPONENTS = ['Cgst', 'Sgst', 'Igst', 'Cess'] as const

export type RateComponent = (typeof RATE_COMPONENTS)[number]

/** The message each component's row is labelled with, wherever one is shown. */
export const RATE_LABEL_KEY: Readonly<Record<RateComponent, MessageKey>> = {
  Cgst: 'pricing.taxCode.form.rate.Cgst',
  Sgst: 'pricing.taxCode.form.rate.Sgst',
  Igst: 'pricing.taxCode.form.rate.Igst',
  Cess: 'pricing.taxCode.form.rate.Cess',
}

/**
 * A tax code as the form holds it.
 *
 * `kind` and `active` are empty strings until chosen — never defaulted — because a statutory
 * classification and an active flag are the accountant's to decide, not this product's to assume
 * (#41's own wording). Each rate is the percentage as typed, blank when the code does not carry that
 * component; nothing here is pre-filled.
 */
export interface TaxCodeDraft {
  readonly code: string
  readonly description: string
  readonly classification: string
  readonly kind: string
  /** `''` (not chosen), `'true'` or `'false'`. */
  readonly active: string
  readonly rates: Readonly<Record<RateComponent, string>>
  readonly reason: string
}

const BLANK_RATES: Readonly<Record<RateComponent, string>> = {
  Cgst: '',
  Sgst: '',
  Igst: '',
  Cess: '',
}

export function blankTaxCodeDraft(): TaxCodeDraft {
  return {
    code: '',
    description: '',
    classification: '',
    kind: '',
    active: '',
    rates: BLANK_RATES,
    reason: '',
  }
}

/** A row as the form holds it, so an edit starts from what is stored. */
export function draftFromTaxCode(code: TaxCode): TaxCodeDraft {
  const rates: Record<RateComponent, string> = { ...BLANK_RATES }
  for (const rate of code.rates) {
    if ((RATE_COMPONENTS as readonly string[]).includes(rate.kind)) {
      rates[rate.kind as RateComponent] = String(rate.ratePercent)
    }
  }

  return {
    code: code.code,
    description: code.description,
    classification: code.classification,
    kind: code.kind,
    active: code.active ? 'true' : 'false',
    rates,
    reason: '',
  }
}

/**
 * The rates to send, in fixed component order, omitting every component left blank.
 *
 * Both the submit path and the field-error interpretation path call this on the same, unchanged
 * draft, so `rates[N]` in a server refusal and `ratesToSubmit(draft)[N]` always name the same row.
 */
export function ratesToSubmit(draft: TaxCodeDraft): readonly TaxRateRequest[] {
  return RATE_COMPONENTS.filter((kind) => draft.rates[kind].trim() !== '').map((kind) => ({
    kind,
    ratePercent: parseDecimalString(draft.rates[kind]) ?? draft.rates[kind],
  }))
}

/** The component a server field error's array index refers to, for the draft it was sent from. */
export function rateComponentAtIndex(
  draft: TaxCodeDraft,
  index: number,
): RateComponent | undefined {
  return ratesToSubmit(draft)[index]?.kind as RateComponent | undefined
}

/** The request body, once `kind` and `active` have both been explicitly chosen. */
export function taxCodeRequestFrom(draft: TaxCodeDraft): TaxCodeRequest {
  return {
    code: draft.code.trim(),
    description: draft.description.trim(),
    classification: draft.classification.trim(),
    kind: draft.kind === '' ? null : draft.kind,
    active: draft.active === 'true',
    rates: ratesToSubmit(draft),
    reason: draft.reason.trim() === '' ? null : draft.reason.trim(),
  }
}
