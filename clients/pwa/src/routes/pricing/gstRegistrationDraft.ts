import type { GstRegistration } from '../../billing/pricingAdminTypes'

/** What the form holds while a person is typing, before it is sent as a `GstRegistrationRequest`. */
export interface GstRegistrationDraft {
  readonly branchId: string
  readonly gstin: string
  readonly stateCode: string
  readonly legalName: string
  readonly tradeName: string
  readonly effectiveFrom: string
  readonly effectiveTo: string
  readonly reason: string
}

/** A blank draft, for recording a new registration. */
export function blankGstRegistrationDraft(): GstRegistrationDraft {
  return {
    branchId: '',
    gstin: '',
    stateCode: '',
    legalName: '',
    tradeName: '',
    effectiveFrom: '',
    effectiveTo: '',
    reason: '',
  }
}

/** The existing registration, as a draft to amend — every field re-sent, because the `PUT` is whole-value. */
export function draftFromGstRegistration(registration: GstRegistration): GstRegistrationDraft {
  return {
    branchId: registration.branchId,
    gstin: registration.gstin,
    stateCode: registration.stateCode,
    legalName: registration.legalName,
    tradeName: registration.tradeName ?? '',
    effectiveFrom: registration.effectiveFrom,
    effectiveTo: registration.effectiveTo ?? '',
    reason: '',
  }
}
