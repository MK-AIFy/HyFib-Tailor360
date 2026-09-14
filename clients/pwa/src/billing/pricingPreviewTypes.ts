/**
 * The pricing preview's own payloads (E09-F01-8b), pinned against the published contract in
 * `api/contract.ts` — `POST /api/v1/billing/pricing/preview` (#147), which stores nothing.
 *
 * Every decimal member is typed `number | string`, because the published schema types a decimal as
 * `{"type": ["number", "string"]}`. **No arithmetic is ever done on one**: every figure is rendered
 * exactly as the server sent it, through `formatMoney` or `formatPercent`. A client that re-added the
 * lines would be a second engine, and two engines disagreeing on a total is the defect this whole
 * feature exists to prevent (`docs/IMPLEMENTATION_PLAN.md` Section 6.2 note 1:
 * `Billing.Contracts.IPricingService` is the only calculator).
 */

/**
 * Price a request against a named price-list version, without storing the result.
 *
 * `taxConfigurationVersionId` is the tax configuration version to take rates from, or null for the
 * published one. `on` is the branch-local date, deciding which registration is in force.
 * `placeOfSupplyStateCode` is the two-digit state code of the place of supply.
 */
export interface PricingPreviewRequest {
  readonly priceListVersionId: string | null
  readonly taxConfigurationVersionId: string | null
  readonly branchId: string | null
  readonly on: string | null
  readonly placeOfSupplyStateCode: string | null
  readonly lines: readonly PricingLineRequest[] | null
}

/** One line to price. `lineKey` is the caller's own key — `billing.line-key-duplicated` is about it. */
export interface PricingLineRequest {
  readonly lineKey: string | null
  readonly itemCode: string | null
  readonly quantity: number | string | null
  readonly surchargeItemCodes: readonly string[] | null
  readonly discount: PricingDiscountRequest | null
  readonly override: PricingOverrideRequest | null
}

/** A discount on a line, naming one of the version's own rules. */
export interface PricingDiscountRequest {
  readonly ruleCode: string | null
  readonly value: number | string | null
  readonly reason: string | null
}

/** A rate in place of the catalogue rate. */
export interface PricingOverrideRequest {
  readonly rate: number | string | null
  readonly reason: string | null
}

/**
 * What the engine produced. Every amount is in `currency`, to paise.
 *
 * `scheme` is `IntraState` or `InterState`; `currency` is `Money.IndianRupee`. Everything the screen
 * must show is already here — nothing has to be derived.
 */
export interface PricingResult {
  readonly priceListVersionId: string
  readonly taxConfigurationVersionId: string
  readonly gstRegistrationId: string
  readonly scheme: string
  readonly taxInclusive: boolean
  readonly currency: string
  readonly calculatedAt: string
  readonly lines: readonly PricedLine[]
  readonly totals: PricedDocumentTotals
}

/**
 * One priced line.
 *
 * `variance` and `variancePercent` are read only for display: the engine's own unrounded comparison
 * against the version's threshold already decided `approvalExercised`, and the screen never re-derives
 * that decision from the rounded percentage it was shown (`PricingEngine.cs` line 171 — the comparison
 * is made unrounded, because the percentage here is rounded for reading, not for judging).
 */
export interface PricedLine {
  readonly lineKey: string
  readonly itemCode: string
  readonly description: string
  readonly quantity: number | string
  readonly catalogueRate: number | string
  readonly appliedRate: number | string
  readonly base: number | string
  readonly surcharges: readonly PricedSurcharge[]
  readonly discount: PricedDiscount | null
  readonly gross: number | string
  readonly taxableValue: number | string
  readonly taxCode: string
  readonly classification: string
  readonly taxCodeKind: string
  readonly taxes: readonly PricedTaxComponent[]
  readonly taxTotal: number | string
  readonly lineTotal: number | string
  readonly variance: number | string
  readonly variancePercent: number | string
  readonly approvalExercised: boolean
}

/** A surcharge on a line. */
export interface PricedSurcharge {
  readonly itemCode: string
  readonly description: string
  readonly rate: number | string
  readonly amount: number | string
}

/** A discount on a line, as priced. `kind` is `Percentage` or `Amount`. */
export interface PricedDiscount {
  readonly ruleCode: string
  readonly kind: string
  readonly value: number | string
  readonly amount: number | string
  readonly approvalExercised: boolean
}

/** One tax component of a line. `kind` is `Cgst`, `Sgst`, `Igst` or `Cess`. */
export interface PricedTaxComponent {
  readonly kind: string
  readonly ratePercent: number | string
  readonly amount: number | string
}

/** The document totals. `roundOff` is shown as its own line, never folded into `grandTotal`. */
export interface PricedDocumentTotals {
  readonly subtotal: number | string
  readonly discountTotal: number | string
  readonly taxableValue: number | string
  readonly centralTax: number | string
  readonly stateTax: number | string
  readonly integratedTax: number | string
  readonly cess: number | string
  readonly roundOff: number | string
  readonly grandTotal: number | string
}
