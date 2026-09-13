import type {
  CatalogCategory,
  CatalogDesignGroup,
  CatalogDesignOperand,
  CatalogDesignOption,
  CatalogDesignRule,
  CatalogFinding,
  CatalogServiceType,
  CatalogValidationReport,
  CatalogVersion,
  CatalogVersionSummary,
} from '../types'

/**
 * Synthetic catalogue data.
 *
 * The codes and names are the ones the seeded catalogue uses, because a fixture whose category is
 * called "Category 1" lets a screen that renders the wrong field look correct. Nothing here is or
 * resembles real customer data — a catalogue carries none by construction.
 */

/** One branch identifier, and a second, so the subset rule has something to be about. */
export const COIMBATORE = '0199bb00-0000-7000-8000-0000000000a1'
export const ERODE = '0199bb00-0000-7000-8000-0000000000a2'

export function aCatalogVersionSummary(
  overrides: Partial<CatalogVersionSummary> = {},
): CatalogVersionSummary {
  return {
    catalogVersionId: '0199bb00-0000-7000-8000-0000000000c1',
    versionNumber: 1,
    name: 'Version 1',
    notes: null,
    status: 'Draft',
    createdAt: '2026-09-01T09:00:00.000Z',
    publishedAt: null,
    retiredAt: null,
    clonedFromVersionId: null,
    ...overrides,
  }
}

export function aCategory(overrides: Partial<CatalogCategory> = {}): CatalogCategory {
  return {
    categoryId: '0199bb00-0000-7000-8000-0000000000b1',
    parentCategoryId: null,
    code: 'BLOUSE',
    name: 'Blouse',
    nameTamil: null,
    description: 'Everything stitched as a blouse.',
    displayOrder: 0,
    isGroupingNode: false,
    branchIds: [COIMBATORE, ERODE],
    activeFrom: null,
    activeTo: null,
    featureFlagKey: null,
    ...overrides,
  }
}

/**
 * A service type with every link present, so a test can remove exactly one.
 *
 * The default is orderable, because a fixture that started life not orderable would let a screen
 * that never handles the orderable case pass its tests.
 */
export function aServiceType(overrides: Partial<CatalogServiceType> = {}): CatalogServiceType {
  return {
    serviceTypeId: '0199bb00-0000-7000-8000-0000000000d1',
    categoryId: '0199bb00-0000-7000-8000-0000000000b1',
    code: 'PATTERN',
    name: 'Pattern work',
    nameTamil: null,
    description: 'A blouse cut to a pattern.',
    displayOrder: 0,
    branchIds: [COIMBATORE],
    activeFrom: null,
    activeTo: null,
    measurementTemplateId: '0199bb00-0000-7000-8000-0000000000f1',
    workflowDefinitionId: '0199bb00-0000-7000-8000-0000000000f2',
    designOptionGroupIds: ['0199bb00-0000-7000-8000-0000000000f3'],
    priceListItemCode: 'PL_BLOUSE_PATTERN',
    qcChecklistTemplateId: '0199bb00-0000-7000-8000-0000000000f4',
    allowIncomplete: false,
    notOrderable: false,
    expectedDurationDays: 5,
    intakeWarning: null,
    ...overrides,
  }
}

/** A draft with one category and one service type under it. */
export function aCatalogVersion(overrides: Partial<CatalogVersion> = {}): CatalogVersion {
  return {
    version: aCatalogVersionSummary(),
    categories: [aCategory()],
    serviceTypes: [aServiceType()],
    designGroups: [],
    designRules: [],
    ...overrides,
  }
}

export function aDesignOption(overrides: Partial<CatalogDesignOption> = {}): CatalogDesignOption {
  return {
    designOptionId: '0199bb00-0000-7000-8000-0000000000e1',
    designOptionGroupId: '0199bb00-0000-7000-8000-0000000000e0',
    code: 'ROUND',
    name: 'Round',
    nameTamil: null,
    helpText: 'A plain round neckline.',
    illustrationKey: null,
    illustrationAlt: 'A round neckline, no collar.',
    priceListItemCode: null,
    timeImpactDays: 0,
    displayOrder: 0,
    active: true,
    ...overrides,
  }
}

export function aDesignGroup(overrides: Partial<CatalogDesignGroup> = {}): CatalogDesignGroup {
  return {
    designOptionGroupId: '0199bb00-0000-7000-8000-0000000000e0',
    categoryId: '0199bb00-0000-7000-8000-0000000000b1',
    code: 'neckline',
    name: 'Neckline',
    nameTamil: null,
    selectionMode: 'SingleChoice',
    required: true,
    displayOrder: 0,
    activeFrom: null,
    activeTo: null,
    branchIds: [],
    options: [aDesignOption()],
    ...overrides,
  }
}

export function anOperand(overrides: Partial<CatalogDesignOperand> = {}): CatalogDesignOperand {
  return {
    groupCode: 'neckline',
    form: 'Equals',
    optionCodes: ['ROUND'],
    ...overrides,
  }
}

export function aDesignRule(overrides: Partial<CatalogDesignRule> = {}): CatalogDesignRule {
  return {
    designRuleId: '0199bb00-0000-7000-8000-0000000000e2',
    categoryId: '0199bb00-0000-7000-8000-0000000000b1',
    number: 1,
    identifier: 'DR-01',
    type: 'Requires',
    antecedent: anOperand(),
    consequent: anOperand({ groupCode: 'sleeve', form: 'Equals', optionCodes: ['SHORT'] }),
    note: null,
    why: null,
    statement: 'If neckline is Round, then sleeve is Short is required.',
    blocks: true,
    ...overrides,
  }
}

export function aCatalogFinding(overrides: Partial<CatalogFinding> = {}): CatalogFinding {
  return {
    severity: 'Error',
    code: 'catalog.service-type-link-missing',
    message: "'BLOUSE.PATTERN' has no price-list item.",
    target: 'serviceTypes[BLOUSE.PATTERN].priceListItemCode',
    validator: 'BuiltInCatalogValidator',
    ...overrides,
  }
}

export function aValidationReport(
  overrides: Partial<CatalogValidationReport> = {},
): CatalogValidationReport {
  return {
    catalogVersionId: '0199bb00-0000-7000-8000-0000000000c1',
    publishable: true,
    errorCount: 0,
    warningCount: 0,
    findings: [],
    ...overrides,
  }
}
