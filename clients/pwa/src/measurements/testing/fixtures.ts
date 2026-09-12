import { aTemplateField, aTemplateVersion } from '../../admin/testing/fixtures'
import type { OrderableCatalog, OrderableService } from '../../catalog/types'
import type { CustomerCard } from '../../customers/types'
import type {
  MeasurementCaptureTemplate,
  MeasurementCheck,
  MeasurementDraft,
  MeasurementVersion,
} from '../types'

/**
 * Synthetic fixtures for the capture screens. No real name, telephone number or measurement
 * appears here or in any story built on them; the numbers are round ones a tape would read.
 */

export const DRAFT_ID = '0199cc00-0000-7000-8000-0000000000a1'
export const CUSTOMER_ID = '0199cc00-0000-7000-8000-0000000000c1'
export const TEMPLATE_ID = '0199bb00-0000-7000-8000-0000000000f1'
export const VERSION_ID = '0199bb00-0000-7000-8000-0000000000e2'

/** A blouse version with two steps: a measured chest and a chosen closure in the bodice, a sleeve length. */
export function aPublishedCaptureVersion() {
  return aTemplateVersion({
    templateVersionId: VERSION_ID,
    versionNumber: 2,
    name: 'Version 2',
    status: 'Published',
    isApproved: true,
    publishedAt: '2026-09-05T09:15:00.000Z',
    fields: [
      aTemplateField(),
      aTemplateField({
        templateFieldId: '0199bb00-0000-7000-8000-0000000000d2',
        key: 'closure',
        label: 'Closure',
        groupName: 'Bodice',
        displayOrder: 1,
        canonicalUnit: 'None',
        displayUnits: [],
        inchFraction: 0,
        centimetreDecimals: 0,
        minimumMillimetres: 0,
        maximumMillimetres: 0,
        warnBelowMillimetres: null,
        warnAboveMillimetres: null,
        helpText: 'Where the blouse fastens.',
        diagramReference: null,
        diagramAlt: null,
        diagramKey: null,
        optionCodes: ['front_hooks', 'back_hooks'],
        options: [
          { code: 'front_hooks', label: 'Front hooks', labelTamil: null, displayOrder: 0 },
          { code: 'back_hooks', label: 'Back hooks', labelTamil: null, displayOrder: 1 },
        ],
      }),
      aTemplateField({
        templateFieldId: '0199bb00-0000-7000-8000-0000000000d3',
        key: 'sleeve_length',
        label: 'Sleeve length',
        groupName: 'Sleeve',
        displayOrder: 0,
        minimumMillimetres: 50,
        maximumMillimetres: 800,
        warnBelowMillimetres: null,
        warnAboveMillimetres: null,
        helpText: 'Shoulder point to the finished hem.',
        diagramReference: null,
        diagramAlt: null,
        diagramKey: null,
        isRequired: false,
      }),
    ],
  })
}

export function aCaptureTemplate(
  overrides: Partial<MeasurementCaptureTemplate> = {},
): MeasurementCaptureTemplate {
  return {
    measurementDraftId: DRAFT_ID,
    measurementTemplateId: TEMPLATE_ID,
    code: 'MT_BLOUSE_PATTERN',
    name: 'Blouse, pattern work',
    version: aPublishedCaptureVersion(),
    ...overrides,
  }
}

export function aMeasurementDraft(overrides: Partial<MeasurementDraft> = {}): MeasurementDraft {
  return {
    measurementDraftId: DRAFT_ID,
    customerId: CUSTOMER_ID,
    branchId: '0199aa00-0000-7000-8000-0000000000aa',
    measurementTemplateId: TEMPLATE_ID,
    templateVersionId: VERSION_ID,
    reusedFromVersionId: null,
    startedAt: '2026-09-11T04:00:00.000Z',
    updatedAt: '2026-09-11T04:00:00.000Z',
    expiresAt: '2026-09-14T04:00:00.000Z',
    consumedAt: null,
    values: [],
    ...overrides,
  }
}

export function aMeasurementCheck(overrides: Partial<MeasurementCheck> = {}): MeasurementCheck {
  return { measurementDraftId: DRAFT_ID, confirmable: true, findings: [], ...overrides }
}

export function aMeasurementVersion(
  overrides: Partial<MeasurementVersion> = {},
): MeasurementVersion {
  return {
    measurementVersionId: '0199cc00-0000-7000-8000-0000000000b1',
    customerId: CUSTOMER_ID,
    branchId: '0199aa00-0000-7000-8000-0000000000aa',
    measurementTemplateId: TEMPLATE_ID,
    templateVersionId: VERSION_ID,
    versionNumber: 1,
    takenAt: '2026-09-11T04:20:00.000Z',
    takenBy: '0199aa00-0000-7000-8000-000000000001',
    reason: null,
    reusedFromVersionId: null,
    correctsVersionId: null,
    values: [
      {
        key: 'chest_bust',
        millimetres: 927.1,
        enteredUnit: 'Inch',
        choice: null,
        acknowledged: false,
      },
    ],
    ...overrides,
  }
}

export function aCustomerCard(overrides: Partial<CustomerCard> = {}): CustomerCard {
  return {
    customerId: CUSTOMER_ID,
    customerNumber: 'C-000123',
    displayName: 'Asha Example',
    nativeName: null,
    maskedPhone: '••••••4321',
    owningBranchId: '0199aa00-0000-7000-8000-0000000000aa',
    visibleToCaller: true,
    status: 'Active',
    lastSeenAt: '2026-09-10T10:00:00.000Z',
    ...overrides,
  }
}

export function anOrderableService(overrides: Partial<OrderableService> = {}): OrderableService {
  return {
    serviceTypeId: '0199dd00-0000-7000-8000-0000000000s1',
    serviceCode: 'PATTERN',
    serviceName: 'Pattern work',
    categoryId: '0199dd00-0000-7000-8000-0000000000c1',
    categoryCode: 'BLOUSE',
    categoryName: 'Blouse',
    qualifiedReference: 'BLOUSE.PATTERN',
    measurementTemplateId: TEMPLATE_ID,
    workflowDefinitionId: null,
    designOptionGroupIds: [],
    priceListItemCode: null,
    qcChecklistTemplateId: null,
    expectedDurationDays: 7,
    intakeWarning: null,
    ...overrides,
  }
}

export function anOrderableCatalog(overrides: Partial<OrderableCatalog> = {}): OrderableCatalog {
  return {
    branchId: '0199aa00-0000-7000-8000-0000000000aa',
    catalogVersionId: '0199dd00-0000-7000-8000-0000000000v1',
    services: [anOrderableService()],
    ...overrides,
  }
}
