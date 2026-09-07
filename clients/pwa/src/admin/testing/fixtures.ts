import type { StaffSummary, StaffUser } from '../types'

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
