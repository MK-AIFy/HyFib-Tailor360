import { BRANCH_ID } from './fixtures'
import type { BillingFinding, GstRegistration, TaxConfigurationSummary } from '../pricingAdminTypes'

/**
 * Synthetic fixtures for the pricing administration screens (#237). No real GSTIN, legal name or
 * branch appears here or in any story or test built on them.
 */

export const GST_REGISTRATION_ID = '0199dd00-0000-7000-8000-000000006001'
export const TAX_CONFIGURATION_VERSION_ID = '0199dd00-0000-7000-8000-000000006002'

/** One branch's GST registration, in force since the start of the financial year shown elsewhere. */
export function aGstRegistration(overrides: Partial<GstRegistration> = {}): GstRegistration {
  return {
    gstRegistrationId: GST_REGISTRATION_ID,
    branchId: BRANCH_ID,
    gstin: '33AAACH7409R1Z8',
    stateCode: '33',
    legalName: 'Example Tailors Private Limited',
    tradeName: 'Example Tailors',
    effectiveFrom: '2026-04-01',
    effectiveTo: null,
    createdAt: '2026-04-01T05:00:00.000Z',
    updatedAt: '2026-04-01T05:00:00.000Z',
    ...overrides,
  }
}

/** One tax configuration version, published and in force. */
export function aTaxConfigurationSummary(
  overrides: Partial<TaxConfigurationSummary> = {},
): TaxConfigurationSummary {
  return {
    taxConfigurationVersionId: TAX_CONFIGURATION_VERSION_ID,
    versionNumber: 1,
    name: 'Version 1',
    notes: null,
    status: 'Published',
    effectiveFrom: '2026-04-01',
    clonedFromVersionId: null,
    createdAt: '2026-03-20T05:00:00.000Z',
    publishedAt: '2026-03-25T05:00:00.000Z',
    retiredAt: null,
    ...overrides,
  }
}

/** One finding a publish validation reported. Declared for two later siblings; unused by this issue. */
export function aBillingFinding(overrides: Partial<BillingFinding> = {}): BillingFinding {
  return {
    severity: 'Error',
    code: 'billing.configuration-missing',
    message: "The 'GST5' tax code has no CGST component configured.",
    target: 'taxCodes[GST5]',
    ...overrides,
  }
}
