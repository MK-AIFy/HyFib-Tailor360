import type {
  Branch,
  DeadLetteredMessage,
  FeatureFlag,
  Permission,
  Role,
  StaffSummary,
  StaffUser,
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
    aggregateId: '0199bb00-0000-7000-8000-0000000000c2',
    eventType: 'notifications.message_queued',
    schemaVersion: 1,
    occurredAt: '2026-09-06T10:00:00.000Z',
    deadLetteredAt: '2026-09-06T10:12:00.000Z',
    attemptCount: 5,
    lastError: 'The provider refused the request: 503 Service Unavailable.',
    correlationId: 'req-synthetic-0001',
    ...overrides,
  }
}
