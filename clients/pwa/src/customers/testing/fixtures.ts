import type { Customer, CustomerCard, DuplicateCandidate } from '../types'

/**
 * Fixtures for the customer screens.
 *
 * **Synthetic only, and deliberately obviously so.** Every name below is a stock character name,
 * every address is on `example.invalid`, and no value here resembles a real person's — the client
 * guide's rule for fixtures, and not decoration: a fixture that looks like real data ends up in a
 * screenshot in a pull request, and from there in a browser cache on somebody's laptop.
 */

/** A response carrying the `ETag` a customer read has to return. */
export function versionedResponse(body: unknown, version: string, status = 200): Response {
  return new Response(JSON.stringify(body), {
    status,
    headers: { 'Content-Type': 'application/json', ETag: version },
  })
}

/** One search result. */
export function aCustomerCard(overrides: Partial<CustomerCard> = {}): CustomerCard {
  return {
    customerId: '0199cc00-0000-7000-8000-000000000001',
    customerNumber: 'C-000123',
    displayName: 'Priya Selvam',
    nativeName: null,
    maskedPhone: '••••••1234',
    owningBranchId: '0199a000-0000-7000-8000-000000000001',
    visibleToCaller: true,
    status: 'Active',
    lastSeenAt: '2026-09-01T10:00:00Z',
    ...overrides,
  }
}

/** One full customer record, as the detail screen reads it. */
export function aCustomer(overrides: Partial<Customer> = {}): Customer {
  return {
    customerId: '0199cc00-0000-7000-8000-000000000001',
    customerNumber: 'C-000123',
    displayName: 'Priya Selvam',
    nativeName: null,
    phone: '+919000000001',
    alternatePhone: null,
    email: null,
    addressLine: null,
    locality: null,
    postcode: null,
    contactIncluded: true,
    language: 'en-IN',
    status: 'Active',
    owningBranchId: '0199a000-0000-7000-8000-000000000001',
    visibilityBranchIds: ['0199a000-0000-7000-8000-000000000001'],
    aliases: [],
    createdAt: '2026-09-01T10:00:00Z',
    updatedAt: '2026-09-01T10:00:00Z',
    mergedIntoCustomerId: null,
    mergedAt: null,
    ...overrides,
  }
}

/** One duplicate candidate, as the create screen's 409 carries it. */
export function aDuplicateCandidate(
  overrides: Partial<DuplicateCandidate> = {},
): DuplicateCandidate {
  return {
    customer: aCustomerCard(),
    confidence: 'High',
    reasons: ['Same telephone number'],
    ...overrides,
  }
}
