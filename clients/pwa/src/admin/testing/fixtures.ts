import type {
  Branch,
  DeadLetteredMessage,
  FeatureFlag,
  Permission,
  Role,
  StaffSummary,
  StaffUser,
  MeasurementTemplate,
  TemplateField,
  TemplateVersion,
} from '../types'

/**
 * Fixtures for the administration screens.
 *
 * **Synthetic only, and deliberately obviously so.** Every name below is a role, every address is on
 * `example.invalid`, and no value here resembles a real person's. That is the client guide's rule for
 * fixtures and it is not decoration: a fixture that looks like real data ends up in a screenshot in a
 * pull request, and from there in a browser cache on somebody's laptop.
 */

/** A response carrying the `ETag` an administrative read has to return. */
export function versionedResponse(body: unknown, version: string, status = 200): Response {
  return new Response(JSON.stringify(body), {
    status,
    headers: { 'Content-Type': 'application/json', ETag: version },
  })
}

/** One row of the staff list. Carries no version — the list deliberately does not receive one. */
export function aStaffSummary(overrides: Partial<StaffSummary> = {}): StaffSummary {
  return {
    userId: '0199bb00-0000-7000-8000-000000000001',
    displayName: 'Meera (counter)',
    userName: 'meera.counter',
    status: 'Active',
    mfaEnrolment: 'Enrolled',
    homeBranchId: '0199bb00-0000-7000-8000-0000000000aa',
    roleKeys: ['reception'],
    lastSignInAt: '2026-09-05T09:15:00.000Z',
    createdAt: '2026-08-01T05:00:00.000Z',
    ...overrides,
  }
}

/** One staff account as the detail screen reads it. */
export function aStaffUser(overrides: Partial<StaffUser> = {}): StaffUser {
  return {
    userId: '0199bb00-0000-7000-8000-000000000001',
    displayName: 'Meera (counter)',
    userName: 'meera.counter',
    email: 'meera.counter@example.invalid',
    status: 'Active',
    mfaEnrolment: 'Enrolled',
    homeBranchId: '0199bb00-0000-7000-8000-0000000000aa',
    lastSignInAt: '2026-09-05T09:15:00.000Z',
    createdAt: '2026-08-01T05:00:00.000Z',
    version: 'W/"1"',
    ...overrides,
  }
}

/** One branch in the register. */
export function aBranch(overrides: Partial<Branch> = {}): Branch {
  return {
    branchId: '0199bb00-0000-7000-8000-0000000000aa',
    code: 'CBE01',
    name: 'Coimbatore counter',
    timeZoneId: 'Asia/Kolkata',
    status: 'Open',
    statusReason: null,
    addressLine1: null,
    addressLine2: null,
    city: null,
    state: null,
    postalCode: null,
    contactPhone: null,
    contactEmail: null,
    gstRegistrationReference: null,
    version: 'W/"1"',
    ...overrides,
  }
}

/** One role, with what it grants and how many people hold it. */
export function aRole(overrides: Partial<Role> = {}): Role {
  return {
    roleId: '0199bb00-0000-7000-8000-0000000000b1',
    key: 'reception',
    name: 'Reception',
    description: 'The counter: intake, measurements, estimate, confirmation, labels.',
    reach: 'Branch',
    isSystem: true,
    assignedByDefault: true,
    permissionKeys: ['orders.read'],
    holders: 3,
    updatedAt: '2026-09-01T05:00:00.000Z',
    updatedBy: null,
    version: 'W/"1"',
    ...overrides,
  }
}

/** One entry in the permission catalogue. */
export function aPermission(overrides: Partial<Permission> = {}): Permission {
  return {
    key: 'orders.read',
    description: 'Read orders in the branches the holder works in.',
    module: 'Orders',
    scope: 'Branch',
    requiresMfa: false,
    requiresStepUp: false,
    requiresReason: false,
    ...overrides,
  }
}

/** One feature setting. */
export function aFeatureFlag(overrides: Partial<FeatureFlag> = {}): FeatureFlag {
  return {
    key: 'pilot.counter-queue',
    enabled: false,
    reason: 'Paused until the stock count.',
    updatedAt: '2026-09-04T07:30:00.000Z',
    updatedBy: null,
    revision: 2,
    propagationSeconds: 30,
    version: 'W/"2"',
    ...overrides,
  }
}

/** One message that exhausted its delivery attempts. */
export function aDeadLetter(overrides: Partial<DeadLetteredMessage> = {}): DeadLetteredMessage {
  return {
    id: '0199bb00-0000-7000-8000-0000000000c1',
    module: 'notifications',
    aggregateId: '0199bb00-0000-7000-8000-0000000000c2',
    eventType: 'notifications.message-queued.v1',
    schemaVersion: 1,
    occurredAt: '2026-09-06T10:00:00.000Z',
    deadLetteredAt: '2026-09-06T10:12:00.000Z',
    attemptCount: 5,
    lastError: 'The provider refused the request: 503 Service Unavailable.',
    correlationId: 'req-synthetic-0001',
    ...overrides,
  }
}

/* Measurement templates (#93) ------------------------------------------------------------------ */

/**
 * One field of a version.
 *
 * The bands are the blouse chest measurement from the specification, in millimetres: 100 mm to
 * 2000 mm refused outside, 200 mm and 1800 mm the thresholds that ask for an acknowledgement. They
 * are real numbers from a real field set rather than round ones, because a fixture of 0 and 100
 * would let a rendering bug that swaps the pair look correct.
 */
export function aTemplateField(overrides: Partial<TemplateField> = {}): TemplateField {
  return {
    templateFieldId: '0199bb00-0000-7000-8000-0000000000d1',
    key: 'chest_bust',
    label: 'Chest / bust',
    labelTamil: null,
    groupName: 'Bodice',
    displayOrder: 0,
    canonicalUnit: 'Millimetre',
    displayUnits: ['Inch', 'Centimetre'],
    inchFraction: 8,
    centimetreDecimals: 1,
    isRequired: true,
    minimumMillimetres: 100,
    maximumMillimetres: 2000,
    warnBelowMillimetres: 200,
    warnAboveMillimetres: 1800,
    helpText: 'Round the fullest part, tape level.',
    diagramReference: 'blouse_front_v1#chest_bust',
    diagramAlt: 'Around the fullest part of the chest, tape level at the back.',
    diagramKey: 'blouse_front_v1',
    diagramMediaId: null,
    rule: null,
    ruleDefinition: null,
    optionCodes: [],
    options: [],
    ...overrides,
  }
}

/** One version. A draft by default, because that is the state every version starts in. */
export function aTemplateVersion(overrides: Partial<TemplateVersion> = {}): TemplateVersion {
  return {
    templateVersionId: '0199bb00-0000-7000-8000-0000000000e1',
    versionNumber: 1,
    name: 'Version 1',
    notes: null,
    status: 'Draft',
    defaultDisplayUnit: 'Inch',
    isApproved: false,
    publishedAt: null,
    retiredAt: null,
    fields: [aTemplateField()],
    ...overrides,
  }
}

/**
 * One template, with a single draft version and nothing published.
 *
 * Nothing published is the honest default: a template arrives that way, and a fixture that started
 * life published would let a screen that never handles "cannot measure anything yet" pass its tests.
 */
export function aMeasurementTemplate(
  overrides: Partial<MeasurementTemplate> = {},
): MeasurementTemplate {
  return {
    measurementTemplateId: '0199bb00-0000-7000-8000-0000000000f1',
    code: 'MT_BLOUSE_PATTERN',
    name: 'Blouse, pattern work',
    description: 'What is measured for a pattern-work blouse.',
    publishedVersionId: null,
    versions: [aTemplateVersion()],
    ...overrides,
  }
}
