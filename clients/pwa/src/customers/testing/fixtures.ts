import type {
  CommunicationPreferences,
  ConsentAnswer,
  ConsentPurpose,
  Customer,
  CustomerCard,
  CustomerTimelineEntry,
  CustomerTimelinePage,
  DuplicateCandidate,
} from '../types'

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

/** One timeline entry. Defaults to the plainest case: a correction, with a reason anyone may read. */
export function aTimelineEntry(
  overrides: Partial<CustomerTimelineEntry> = {},
): CustomerTimelineEntry {
  return {
    entryId: '0199cc00-0000-7000-8000-00000000e001',
    occurredAt: '2026-09-01T10:00:00Z',
    source: 'customers',
    kind: 'customers.record.corrected',
    title: 'Customer record corrected',
    detail: 'Name changed.',
    reason: 'Spelling on her identity document',
    reasonPermission: 'customers.read_notes',
    referenceType: null,
    referenceId: null,
    expandPermission: null,
    branchId: '0199a000-0000-7000-8000-000000000001',
    actorDisplayName: 'Kavitha R',
    ...overrides,
  }
}

/** One page of history. Newest first, one entry, nothing missing and nothing more to fetch. */
export function aTimelinePage(overrides: Partial<CustomerTimelinePage> = {}): CustomerTimelinePage {
  return {
    entries: [aTimelineEntry()],
    nextCursor: null,
    unavailableSources: [],
    ...overrides,
  }
}

/** One consent answer. Defaults to the plainest case: she agreed, at the counter. */
export function aConsentAnswer(overrides: Partial<ConsentAnswer> = {}): ConsentAnswer {
  return {
    recordId: '0199cc00-0000-7000-8000-00000000c001',
    purposeKey: 'appointment-reminders',
    decision: 'Granted',
    wordingVersion: 2,
    recordedAt: '2026-09-01T10:00:00Z',
    source: 'At the counter',
    recordedBy: '0199b000-0000-7000-8000-000000000001',
    branchId: '0199a000-0000-7000-8000-000000000001',
    ...overrides,
  }
}

/** One consent purpose, answerable and agreed to. */
export function aConsentPurpose(overrides: Partial<ConsentPurpose> = {}): ConsentPurpose {
  return {
    key: 'appointment-reminders',
    name: 'Appointment reminders',
    description: 'We may message you when your garment is ready.',
    isRetired: false,
    currentWordingVersion: 2,
    canBeAnswered: true,
    status: 'Granted',
    answers: [aConsentAnswer()],
    ...overrides,
  }
}

/**
 * How a customer wants to be reached.
 *
 * Defaults to a preference that **has** been recorded, so a test has to opt into the
 * never-recorded case — which is the one where `If-Match` must be omitted rather than sent.
 */
export function aCommunicationPreference(
  overrides: Partial<CommunicationPreferences> = {},
): CommunicationPreferences {
  return {
    customerId: '0199cc00-0000-7000-8000-000000000001',
    hasBeenRecorded: true,
    allowedChannels: ['Sms'],
    language: 'en-IN',
    quietHoursStart: '21:00:00',
    quietHoursEnd: '08:00:00',
    updatedAt: '2026-09-01T10:00:00Z',
    version: 'W/"4"',
    ...overrides,
  }
}
