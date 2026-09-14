import type { DiscountRule, PriceListItem } from '../../billing/priceListTypes'
import type { MessageKey } from '../../i18n/en-IN'
import type { PricingPreviewFormDraft, PricingPreviewLineDraft } from './pricingPreviewDraft'
import { blankPricingPreviewLineDraft } from './pricingPreviewDraft'

/**
 * The accountant's case shapes (E09-F01-8b): eleven one-press scenarios, named and cited from
 * `tests/fixtures/billing/pricing-golden-master.json`, so the accountant's examples can be walked
 * through in front of them before a publish.
 *
 * **A case fills the form; it computes no expected figure.** The golden master's own item codes,
 * tax codes and rates are fixture data for a database that does not exist here — this reuses only the
 * *shapes* it names (an inter-state line, a percentage discount, an override beyond the threshold),
 * applied to whichever items the administrator has already chosen on this version, or — when nothing
 * has been added yet — to the version's own first priceable item, so the empty state's "start with a
 * case" is literally true. Every discount and override still names a real rule or rate from the
 * version being previewed; nothing here invents a code the server would refuse.
 */
export const PRICING_CASE_SHAPE_IDS = [
  'intraStateExclusive',
  'interState',
  'inclusive',
  'percentageDiscount',
  'amountDiscount',
  'surcharge',
  'quantityAboveOne',
  'overrideWithinThreshold',
  'overrideBeyondThreshold',
  'nilRated',
  'roundOffTwoLines',
] as const

export type PricingCaseShapeId = (typeof PRICING_CASE_SHAPE_IDS)[number]

/** The label and hint for one case, both message keys so the button and its help text localise. */
export interface PricingCaseShapeDescription {
  readonly id: PricingCaseShapeId
  readonly labelId: MessageKey
  readonly hintId: MessageKey
}

export const PRICING_CASE_SHAPES: readonly PricingCaseShapeDescription[] = [
  {
    id: 'intraStateExclusive',
    labelId: 'pricing.preview.case.intraStateExclusive',
    hintId: 'pricing.preview.case.intraStateExclusive.hint',
  },
  {
    id: 'interState',
    labelId: 'pricing.preview.case.interState',
    hintId: 'pricing.preview.case.interState.hint',
  },
  {
    id: 'inclusive',
    labelId: 'pricing.preview.case.inclusive',
    hintId: 'pricing.preview.case.inclusive.hint',
  },
  {
    id: 'percentageDiscount',
    labelId: 'pricing.preview.case.percentageDiscount',
    hintId: 'pricing.preview.case.percentageDiscount.hint',
  },
  {
    id: 'amountDiscount',
    labelId: 'pricing.preview.case.amountDiscount',
    hintId: 'pricing.preview.case.amountDiscount.hint',
  },
  {
    id: 'surcharge',
    labelId: 'pricing.preview.case.surcharge',
    hintId: 'pricing.preview.case.surcharge.hint',
  },
  {
    id: 'quantityAboveOne',
    labelId: 'pricing.preview.case.quantityAboveOne',
    hintId: 'pricing.preview.case.quantityAboveOne.hint',
  },
  {
    id: 'overrideWithinThreshold',
    labelId: 'pricing.preview.case.overrideWithinThreshold',
    hintId: 'pricing.preview.case.overrideWithinThreshold.hint',
  },
  {
    id: 'overrideBeyondThreshold',
    labelId: 'pricing.preview.case.overrideBeyondThreshold',
    hintId: 'pricing.preview.case.overrideBeyondThreshold.hint',
  },
  {
    id: 'nilRated',
    labelId: 'pricing.preview.case.nilRated',
    hintId: 'pricing.preview.case.nilRated.hint',
  },
  {
    id: 'roundOffTwoLines',
    labelId: 'pricing.preview.case.roundOffTwoLines',
    hintId: 'pricing.preview.case.roundOffTwoLines.hint',
  },
]

/** What a case shape reads from the chosen version to fill the form realistically. */
export interface PricingCaseShapeContext {
  readonly items: readonly PriceListItem[]
  readonly discountRules: readonly DiscountRule[]
  readonly overrideThresholdPercent: number | string
  /** Already localised: the reason an override case fills in, for the person to review before running. */
  readonly overrideReason: string
}

function baseline(line: PricingPreviewLineDraft): PricingPreviewLineDraft {
  return {
    ...line,
    quantity: '1',
    discountRuleCode: '',
    discountValue: '',
    discountReason: '',
    override: false,
    overrideRate: '',
    overrideReason: '',
  }
}

function withDiscount(
  line: PricingPreviewLineDraft,
  context: PricingCaseShapeContext,
  kind: 'Percentage' | 'Amount',
): PricingPreviewLineDraft {
  const rule = context.discountRules.find(
    (candidate) => candidate.active && candidate.kind === kind,
  )
  if (rule === undefined) {
    return line
  }
  return {
    ...line,
    discountRuleCode: rule.code,
    discountValue: String(rule.maximumWithoutApproval),
    discountReason: '',
  }
}

function withSurcharge(
  line: PricingPreviewLineDraft,
  context: PricingCaseShapeContext,
): PricingPreviewLineDraft {
  const surchargeItem = context.items.find(
    (item) => item.active && item.kind === 'Surcharge' && item.code !== line.itemCode,
  )
  if (surchargeItem === undefined || line.surchargeItemCodes.includes(surchargeItem.code)) {
    return line
  }
  return { ...line, surchargeItemCodes: [...line.surchargeItemCodes, surchargeItem.code] }
}

/** A rate suggested from the item's own catalogue rate and the version's threshold — a starting point. */
function suggestedOverrideRate(
  baseRate: number | string,
  thresholdPercent: number | string,
  multiplierOfThreshold: number,
): string {
  const base = Number(baseRate)
  const threshold = Number(thresholdPercent)
  if (!Number.isFinite(base) || !Number.isFinite(threshold)) {
    return ''
  }
  const rate = base * (1 + (threshold * multiplierOfThreshold) / 100)
  return rate.toFixed(2)
}

function withOverride(
  line: PricingPreviewLineDraft,
  context: PricingCaseShapeContext,
  multiplierOfThreshold: number,
): PricingPreviewLineDraft {
  const item = context.items.find((candidate) => candidate.code === line.itemCode)
  if (item === undefined) {
    return line
  }
  const rate = suggestedOverrideRate(
    item.baseRate,
    context.overrideThresholdPercent,
    multiplierOfThreshold,
  )
  if (rate === '') {
    return line
  }
  return { ...line, override: true, overrideRate: rate, overrideReason: context.overrideReason }
}

const LINE_SHAPE: Readonly<
  Record<
    PricingCaseShapeId,
    (line: PricingPreviewLineDraft, context: PricingCaseShapeContext) => PricingPreviewLineDraft
  >
> = {
  intraStateExclusive: (line) => baseline(line),
  interState: (line) => baseline(line),
  inclusive: (line) => baseline(line),
  percentageDiscount: (line, context) => withDiscount(baseline(line), context, 'Percentage'),
  amountDiscount: (line, context) => withDiscount(baseline(line), context, 'Amount'),
  surcharge: (line, context) => withSurcharge(baseline(line), context),
  quantityAboveOne: (line) => ({ ...baseline(line), quantity: '2' }),
  overrideWithinThreshold: (line, context) => withOverride(baseline(line), context, 0.5),
  overrideBeyondThreshold: (line, context) => withOverride(baseline(line), context, 1.5),
  nilRated: (line) => baseline(line),
  roundOffTwoLines: (line) => baseline(line),
}

/** A state code different from `current`, as a reviewable starting point for the inter-state case. */
function differentStateCode(current: string): string {
  return current.trim() === '29' ? '27' : '29'
}

function defaultItemCode(items: readonly PriceListItem[]): string {
  return (
    items.find((item) => item.active && item.kind === 'Service')?.code ??
    items.find((item) => item.active)?.code ??
    ''
  )
}

/**
 * Fills the form for one case shape.
 *
 * Operates on the lines already on the form; when there are none, it seeds exactly one from the
 * version's own first priceable item, which is what lets the empty state point at these buttons as the
 * way to start. `roundOffTwoLines` alone also ensures a second line, duplicating the first with a fresh
 * key when only one is present — the shape the two round-off golden-master cases together are about:
 * a line rounds on its own, and a document totals several.
 */
export function applyPricingCaseShape(
  id: PricingCaseShapeId,
  draft: PricingPreviewFormDraft,
  context: PricingCaseShapeContext,
): PricingPreviewFormDraft {
  const seeded =
    draft.lines.length > 0
      ? draft.lines
      : (() => {
          const code = defaultItemCode(context.items)
          return code === '' ? [] : [blankPricingPreviewLineDraft(code)]
        })()

  let lines = seeded.map((line) => LINE_SHAPE[id](line, context))

  if (id === 'roundOffTwoLines' && lines.length === 1) {
    const [only] = lines
    if (only !== undefined) {
      lines = [...lines, { ...only, lineKey: crypto.randomUUID() }]
    }
  }

  return {
    ...draft,
    lines,
    placeOfSupplyStateCode:
      id === 'interState'
        ? differentStateCode(draft.placeOfSupplyStateCode)
        : draft.placeOfSupplyStateCode,
  }
}
