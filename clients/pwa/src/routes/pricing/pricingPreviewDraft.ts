import { parseDecimalString } from '../../i18n/parseNumber'
import type { PricingLineRequest, PricingPreviewRequest } from '../../billing/pricingPreviewTypes'

/**
 * The pricing preview form, as the screen holds it (E09-F01-8b).
 *
 * Every value is a string the person typed or chose, exactly the convention `PriceListItemDraft` and
 * `DiscountRuleDraft` already use — nothing here is a `number`, so nothing here is ever added,
 * multiplied or rounded on the way to the request. `taxConfigurationVersionId` left blank is the
 * payload's own documented meaning, "the published one", not "not answered": there is no separate
 * "says" flag for it, unlike a whole-value write.
 */
export interface PricingPreviewFormDraft {
  readonly priceListId: string
  readonly priceListVersionId: string
  readonly taxConfigurationVersionId: string
  readonly branchId: string
  readonly on: string
  readonly placeOfSupplyStateCode: string
  readonly lines: readonly PricingPreviewLineDraft[]
}

/**
 * One line, as the form holds it.
 *
 * `lineKey` is minted by the screen itself the moment a line is added and never re-typed — see
 * `blankPricingPreviewLineDraft` — which is what keeps two lines from ever sharing one by accident.
 */
export interface PricingPreviewLineDraft {
  readonly lineKey: string
  readonly itemCode: string
  readonly quantity: string
  readonly surchargeItemCodes: readonly string[]
  readonly discountRuleCode: string
  readonly discountValue: string
  readonly discountReason: string
  readonly override: boolean
  readonly overrideRate: string
  readonly overrideReason: string
}

/** A blank form: nothing chosen, no lines. */
export function blankPricingPreviewFormDraft(): PricingPreviewFormDraft {
  return {
    priceListId: '',
    priceListVersionId: '',
    taxConfigurationVersionId: '',
    branchId: '',
    on: '',
    placeOfSupplyStateCode: '',
    lines: [],
  }
}

/** A blank line, with a freshly minted key and a quantity of one. */
export function blankPricingPreviewLineDraft(itemCode = ''): PricingPreviewLineDraft {
  return {
    lineKey: crypto.randomUUID(),
    itemCode,
    quantity: '1',
    surchargeItemCodes: [],
    discountRuleCode: '',
    discountValue: '',
    discountReason: '',
    override: false,
    overrideRate: '',
    overrideReason: '',
  }
}

function lineRequestFrom(line: PricingPreviewLineDraft): PricingLineRequest {
  return {
    lineKey: line.lineKey,
    itemCode: line.itemCode === '' ? null : line.itemCode,
    quantity: parseDecimalString(line.quantity),
    surchargeItemCodes: line.surchargeItemCodes,
    discount:
      line.discountRuleCode === ''
        ? null
        : {
            ruleCode: line.discountRuleCode,
            value: parseDecimalString(line.discountValue),
            reason: line.discountReason.trim() === '' ? null : line.discountReason.trim(),
          },
    override: !line.override
      ? null
      : {
          rate: parseDecimalString(line.overrideRate),
          reason: line.overrideReason.trim() === '' ? null : line.overrideReason.trim(),
        },
  }
}

/** The request body. `priceListId` is the picker's own scaffolding and is never sent. */
export function pricingPreviewRequestFrom(draft: PricingPreviewFormDraft): PricingPreviewRequest {
  return {
    priceListVersionId: draft.priceListVersionId === '' ? null : draft.priceListVersionId,
    taxConfigurationVersionId:
      draft.taxConfigurationVersionId === '' ? null : draft.taxConfigurationVersionId,
    branchId: draft.branchId === '' ? null : draft.branchId,
    on: draft.on === '' ? null : draft.on,
    placeOfSupplyStateCode:
      draft.placeOfSupplyStateCode.trim() === '' ? null : draft.placeOfSupplyStateCode.trim(),
    lines: draft.lines.map(lineRequestFrom),
  }
}
