import { DATES } from './branch'

/**
 * Customers and their consent records. Synthetic — see the note at the top of `branch.ts`.
 *
 * The consent shape matters to the Reception journey rather than being decoration: a declined
 * consent is stored as a record, never as an absence (docs/prd/walkthroughs.md walkthrough 2 step
 * 2), so `marketingMessages: false` here means "asked and declined" and the intake screen has to be
 * able to say which of the two it is.
 */
export interface JourneyConsent {
  readonly measurementStorage: boolean
  readonly photoCapture: boolean
  readonly transactionalMessages: boolean
  readonly marketingMessages: boolean
  /** The wording version each of the above was recorded against. */
  readonly wordingVersion: number
  /** When they were last recorded, as an ISO instant. */
  readonly recordedAt: string
}

export interface JourneyCustomer {
  readonly id: string
  readonly name: string
  /** Stored and displayed in the national format, which is what `inputmode="tel"` produces. */
  readonly phone: string
  /** The last six digits, which is what Reception actually types into the search. */
  readonly searchKey: string
  readonly since: string
  readonly lastOrder: string | null
  readonly consent: JourneyConsent
}

export const CUSTOMERS: readonly JourneyCustomer[] = [
  {
    id: 'C-CBE01-004182',
    name: 'Kavitha Raman',
    phone: '+91 98430 21174',
    searchKey: '021174',
    since: '2025-08-03T10:00:00+05:30',
    lastOrder: DATES.lastMonth,
    consent: {
      measurementStorage: true,
      photoCapture: false,
      transactionalMessages: true,
      marketingMessages: false,
      wordingVersion: 1,
      recordedAt: '2025-08-03T10:04:00+05:30',
    },
  },
  {
    id: 'C-CBE01-004610',
    name: 'Revathi Murugan',
    phone: '+91 94420 66315',
    searchKey: '066315',
    since: '2026-06-02T17:40:00+05:30',
    lastOrder: null,
    consent: {
      measurementStorage: true,
      photoCapture: true,
      transactionalMessages: true,
      marketingMessages: false,
      wordingVersion: 1,
      recordedAt: '2026-06-02T17:44:00+05:30',
    },
  },
  {
    id: 'C-CBE01-004903',
    name: 'Bhuvaneswari Karthik',
    phone: '+91 99529 18840',
    searchKey: '918840',
    since: '2025-11-19T12:15:00+05:30',
    lastOrder: DATES.yesterday,
    consent: {
      measurementStorage: true,
      photoCapture: true,
      transactionalMessages: true,
      marketingMessages: true,
      wordingVersion: 1,
      recordedAt: '2025-11-19T12:20:00+05:30',
    },
  },
]

/**
 * A search over the fixtures, matching the last six digits or any part of the name.
 *
 * The segmented phone search of the Reception journey is a real behaviour rather than a filter for
 * the story's benefit: step 1 of `A11Y-PZ-01` is "search by the last six digits of a telephone
 * number", and a search that matched the whole number would not exercise it.
 */
export function findCustomers(term: string): readonly JourneyCustomer[] {
  const trimmed = term.trim().toLowerCase()
  if (trimmed === '') {
    return []
  }
  return CUSTOMERS.filter(
    (customer) =>
      customer.searchKey.includes(trimmed) ||
      customer.name.toLowerCase().includes(trimmed) ||
      customer.phone.replace(/\s/g, '').includes(trimmed),
  )
}
