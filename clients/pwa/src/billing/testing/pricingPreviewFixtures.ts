import type {
  PricedDiscount,
  PricedDocumentTotals,
  PricedLine,
  PricedSurcharge,
  PricedTaxComponent,
  PricingResult,
} from '../pricingPreviewTypes'
import type { PricingCaseShapeId } from '../../routes/pricing/pricingCaseShapes'

/**
 * Synthetic figures for the pricing preview (E09-F01-8b), shaped after
 * `tests/fixtures/billing/pricing-golden-master.json` — the same *shapes* the case buttons name, never
 * its item codes, tax codes or rates: this fixture's codes and amounts are its own, invented for this
 * client, and never a shop's.
 */

export const PRICE_LIST_VERSION_ID = '0199dd00-0000-7000-8000-000000008001'
export const TAX_CONFIGURATION_VERSION_ID = '0199dd00-0000-7000-8000-000000008002'
export const GST_REGISTRATION_ID = '0199dd00-0000-7000-8000-000000008003'
export const CALCULATED_AT = '2026-04-02T05:30:00.000Z'

export function aPricedTaxComponent(
  overrides: Partial<PricedTaxComponent> = {},
): PricedTaxComponent {
  return { kind: 'Cgst', ratePercent: 2.5, amount: 11.25, ...overrides }
}

export function aPricedSurcharge(overrides: Partial<PricedSurcharge> = {}): PricedSurcharge {
  return {
    itemCode: 'SUR_FINISH',
    description: 'Finishing surcharge',
    rate: 40,
    amount: 40,
    ...overrides,
  }
}

export function aPricedDiscount(overrides: Partial<PricedDiscount> = {}): PricedDiscount {
  return {
    ruleCode: 'DISC_SEASONAL',
    kind: 'Percentage',
    value: 5,
    amount: 22.5,
    approvalExercised: false,
    ...overrides,
  }
}

export function aPricedLine(overrides: Partial<PricedLine> = {}): PricedLine {
  return {
    lineKey: 'g1',
    itemCode: 'SVC_STITCH_BASIC',
    description: 'Basic stitching service',
    quantity: 1,
    catalogueRate: 450,
    appliedRate: 450,
    base: 450,
    surcharges: [],
    discount: null,
    gross: 450,
    taxableValue: 450,
    taxCode: 'TAX_SERVICE_5',
    classification: '998821',
    taxCodeKind: 'Services',
    taxes: [aPricedTaxComponent({ kind: 'Cgst' }), aPricedTaxComponent({ kind: 'Sgst' })],
    taxTotal: 22.5,
    lineTotal: 472.5,
    variance: 0,
    variancePercent: 0,
    approvalExercised: false,
    ...overrides,
  }
}

export function aPricedDocumentTotals(
  overrides: Partial<PricedDocumentTotals> = {},
): PricedDocumentTotals {
  return {
    subtotal: 450,
    discountTotal: 0,
    taxableValue: 450,
    centralTax: 11.25,
    stateTax: 11.25,
    integratedTax: 0,
    cess: 0,
    roundOff: 0,
    grandTotal: 472.5,
    ...overrides,
  }
}

export function aPricingResult(overrides: Partial<PricingResult> = {}): PricingResult {
  return {
    priceListVersionId: PRICE_LIST_VERSION_ID,
    taxConfigurationVersionId: TAX_CONFIGURATION_VERSION_ID,
    gstRegistrationId: GST_REGISTRATION_ID,
    scheme: 'IntraState',
    taxInclusive: false,
    currency: 'INR',
    calculatedAt: CALCULATED_AT,
    lines: [aPricedLine()],
    totals: aPricedDocumentTotals(),
    ...overrides,
  }
}

/**
 * One illustrative result per case shape (`pricingCaseShapes.ts`), for stubbing a story or a test.
 *
 * `overrideBeyondThreshold` shows the *permitted* path — `approvalExercised: true`, the variance the
 * exception carries — because the refusal the same case can also meet is not a result at all; a test
 * for that stubs `problemResponse`/`storyProblem` directly, the way `PricingPreviewRoute.test.tsx`'s
 * own `RefusesAnOverrideBeyondTheThresholdWithoutThePermission` does.
 */
export const PRICING_CASE_SHAPE_RESULTS: Readonly<Record<PricingCaseShapeId, PricingResult>> = {
  intraStateExclusive: aPricingResult(),

  interState: aPricingResult({
    scheme: 'InterState',
    lines: [
      aPricedLine({
        taxes: [aPricedTaxComponent({ kind: 'Igst', ratePercent: 5, amount: 22.5 })],
      }),
    ],
    totals: aPricedDocumentTotals({ centralTax: 0, stateTax: 0, integratedTax: 22.5 }),
  }),

  inclusive: aPricingResult({
    taxInclusive: true,
    lines: [
      aPricedLine({
        gross: 472.5,
        base: 450,
        appliedRate: 472.5,
        catalogueRate: 472.5,
        taxableValue: 450,
      }),
    ],
    totals: aPricedDocumentTotals({ subtotal: 472.5 }),
  }),

  percentageDiscount: aPricingResult({
    lines: [
      aPricedLine({
        discount: aPricedDiscount({
          ruleCode: 'DISC_SEASONAL',
          kind: 'Percentage',
          value: 5,
          amount: 22.5,
        }),
        gross: 450,
        taxableValue: 427.5,
        taxes: [
          aPricedTaxComponent({ kind: 'Cgst', amount: 10.69 }),
          aPricedTaxComponent({ kind: 'Sgst', amount: 10.69 }),
        ],
        taxTotal: 21.38,
        lineTotal: 448.88,
      }),
    ],
    totals: aPricedDocumentTotals({
      discountTotal: 22.5,
      taxableValue: 427.5,
      centralTax: 10.69,
      stateTax: 10.69,
      roundOff: 0.12,
      grandTotal: 449,
    }),
  }),

  amountDiscount: aPricingResult({
    lines: [
      aPricedLine({
        discount: aPricedDiscount({
          ruleCode: 'DISC_GOODWILL',
          kind: 'Amount',
          value: 50,
          amount: 50,
        }),
        taxableValue: 400,
        taxes: [
          aPricedTaxComponent({ kind: 'Cgst', amount: 10 }),
          aPricedTaxComponent({ kind: 'Sgst', amount: 10 }),
        ],
        taxTotal: 20,
        lineTotal: 420,
      }),
    ],
    totals: aPricedDocumentTotals({
      discountTotal: 50,
      taxableValue: 400,
      centralTax: 10,
      stateTax: 10,
      grandTotal: 420,
    }),
  }),

  surcharge: aPricingResult({
    lines: [
      aPricedLine({
        surcharges: [aPricedSurcharge()],
        gross: 490,
        taxableValue: 490,
        taxes: [
          aPricedTaxComponent({ kind: 'Cgst', amount: 12.25 }),
          aPricedTaxComponent({ kind: 'Sgst', amount: 12.25 }),
        ],
        taxTotal: 24.5,
        lineTotal: 514.5,
      }),
    ],
    totals: aPricedDocumentTotals({
      subtotal: 490,
      taxableValue: 490,
      centralTax: 12.25,
      stateTax: 12.25,
      grandTotal: 514.5,
    }),
  }),

  quantityAboveOne: aPricingResult({
    lines: [
      aPricedLine({
        quantity: 2,
        base: 900,
        gross: 900,
        taxableValue: 900,
        taxes: [
          aPricedTaxComponent({ kind: 'Cgst', amount: 22.5 }),
          aPricedTaxComponent({ kind: 'Sgst', amount: 22.5 }),
        ],
        taxTotal: 45,
        lineTotal: 945,
      }),
    ],
    totals: aPricedDocumentTotals({
      subtotal: 900,
      taxableValue: 900,
      centralTax: 22.5,
      stateTax: 22.5,
      grandTotal: 945,
    }),
  }),

  overrideWithinThreshold: aPricingResult({
    lines: [
      aPricedLine({
        appliedRate: 470,
        gross: 470,
        taxableValue: 470,
        taxes: [
          aPricedTaxComponent({ kind: 'Cgst', amount: 11.75 }),
          aPricedTaxComponent({ kind: 'Sgst', amount: 11.75 }),
        ],
        taxTotal: 23.5,
        lineTotal: 493.5,
        variance: 20,
        variancePercent: 4.44,
        approvalExercised: false,
      }),
    ],
    totals: aPricedDocumentTotals({
      subtotal: 470,
      taxableValue: 470,
      centralTax: 11.75,
      stateTax: 11.75,
      roundOff: 0.5,
      grandTotal: 494,
    }),
  }),

  overrideBeyondThreshold: aPricingResult({
    lines: [
      aPricedLine({
        appliedRate: 580,
        gross: 580,
        taxableValue: 580,
        taxes: [
          aPricedTaxComponent({ kind: 'Cgst', amount: 14.5 }),
          aPricedTaxComponent({ kind: 'Sgst', amount: 14.5 }),
        ],
        taxTotal: 29,
        lineTotal: 609,
        variance: 130,
        variancePercent: 28.89,
        approvalExercised: true,
      }),
    ],
    totals: aPricedDocumentTotals({
      subtotal: 580,
      taxableValue: 580,
      centralTax: 14.5,
      stateTax: 14.5,
      grandTotal: 609,
    }),
  }),

  nilRated: aPricingResult({
    lines: [
      aPricedLine({
        itemCode: 'SVC_EXEMPT',
        description: 'An exempt service',
        catalogueRate: 250,
        appliedRate: 250,
        base: 250,
        gross: 250,
        taxableValue: 250,
        taxCode: 'TAX_NIL',
        classification: '9988',
        taxes: [],
        taxTotal: 0,
        lineTotal: 250,
      }),
    ],
    totals: aPricedDocumentTotals({
      subtotal: 250,
      taxableValue: 250,
      centralTax: 0,
      stateTax: 0,
      grandTotal: 250,
    }),
  }),

  roundOffTwoLines: aPricingResult({
    lines: [
      aPricedLine({
        lineKey: 'g1',
        itemCode: 'ITEM_341',
        description: 'A service at 341',
        catalogueRate: 341,
        appliedRate: 341,
        base: 341,
        gross: 341,
        taxableValue: 341,
        taxes: [
          aPricedTaxComponent({ kind: 'Cgst', amount: 8.53 }),
          aPricedTaxComponent({ kind: 'Sgst', amount: 8.53 }),
        ],
        taxTotal: 17.06,
        lineTotal: 358.06,
      }),
      aPricedLine({
        lineKey: 'g2',
        itemCode: 'ITEM_331',
        description: 'A service at 331',
        catalogueRate: 331,
        appliedRate: 331,
        base: 331,
        gross: 331,
        taxableValue: 331,
        taxes: [
          aPricedTaxComponent({ kind: 'Cgst', amount: 8.28 }),
          aPricedTaxComponent({ kind: 'Sgst', amount: 8.28 }),
        ],
        taxTotal: 16.56,
        lineTotal: 347.56,
      }),
    ],
    totals: aPricedDocumentTotals({
      subtotal: 672,
      taxableValue: 672,
      centralTax: 16.81,
      stateTax: 16.81,
      roundOff: 0.38,
      grandTotal: 706,
    }),
  }),
}
