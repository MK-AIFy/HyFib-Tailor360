import { apiRequest, apiRequestVersioned } from '../auth/apiClient'
import type { VersionedResponse } from '../auth/apiClient'
import { CUSTOMER_DUPLICATES_CODE } from './types'
import type {
  Customer,
  CustomerDetailsInput,
  CustomerPage,
  CustomerMergeOutcome,
  CustomerTimelinePage,
  DuplicateCandidate,
  DuplicateReview,
} from './types'

const CUSTOMERS = '/api/v1/customers/'

/** The server returns nothing for a shorter term rather than the whole customer list. */
export const CUSTOMER_SEARCH_MINIMUM_LENGTH = 3

/**
 * Finds a customer by name, native name, customer number or the tail of a telephone number.
 *
 * Answers across the organisation. The term travels as a query string, so it is encoded here rather
 * than at every call site; a name with an ampersand in it is a name, not a second parameter.
 */
export async function searchCustomers(term: string, signal?: AbortSignal): Promise<CustomerPage> {
  const query = new URLSearchParams({ term })

  return await apiRequest<CustomerPage>(`${CUSTOMERS}?${query.toString()}`, {
    ...(signal === undefined ? {} : { signal }),
  })
}

/** Reads one customer record, with the version a correction must be made against. */
export async function readCustomer(
  customerId: string,
  signal?: AbortSignal,
): Promise<VersionedResponse<Customer>> {
  return await apiRequestVersioned<Customer>(`${CUSTOMERS}${customerId}`, {
    ...(signal === undefined ? {} : { signal }),
  })
}

/**
 * What a registration attempt answers, once the duplicate question is settled one way or the other.
 *
 * There is no third case on the wire: the server either creates the record (`customer` present) or
 * refuses with the candidates to read (`customer` null, handled as the 409 this function throws
 * rather than returns — see `RegisterCustomerConflict`). This type exists for the success half only,
 * kept narrow so a caller cannot forget to look at `customer`.
 */
export interface RegisteredCustomer {
  readonly customer: Customer
}

/**
 * Creates a customer record at the branch the caller is working in.
 *
 * Refused with a `409 customers.duplicates-not-reviewed` and a `candidates` list on the problem when
 * an existing record resembles this one strongly enough to be worth reading — the caller reads them,
 * then either opens one or calls this again with `duplicatesReviewed: true`, which records that a
 * person took the decision. `readDuplicateCandidates` reads that list back off the thrown `ApiError`.
 */
export async function registerCustomer(input: {
  readonly details: CustomerDetailsInput
  readonly duplicatesReviewed: boolean
  readonly idempotencyKey: string
}): Promise<RegisteredCustomer> {
  const customer = await apiRequestVersioned<Customer>(CUSTOMERS, {
    method: 'POST',
    body: { ...input.details, duplicatesReviewed: input.duplicatesReviewed },
    idempotencyKey: input.idempotencyKey,
  })

  return { customer: customer.value }
}

/** The shape of the problem `registerCustomer` throws when duplicates have not been reviewed. */
interface DuplicatesProblem {
  readonly code?: string
  readonly candidates?: readonly DuplicateCandidate[]
}

/**
 * Reads the duplicate candidates off a failed `registerCustomer` call, or null when the failure was
 * something else.
 *
 * A separate function rather than a field on `ApiError` itself, because the candidates are specific
 * to this one endpoint's 409 and `ApiProblem` is the shared shape every screen reads — adding a field
 * there for one caller is exactly the kind of quiet coupling that makes the shared type harder to
 * read for everybody else.
 */
export function readDuplicateCandidates(failure: unknown): readonly DuplicateCandidate[] | null {
  if (
    typeof failure !== 'object' ||
    failure === null ||
    !('problem' in failure) ||
    typeof failure.problem !== 'object' ||
    failure.problem === null
  ) {
    return null
  }

  const problem = failure.problem as DuplicatesProblem
  return problem.code === CUSTOMER_DUPLICATES_CODE && problem.candidates !== undefined
    ? problem.candidates
    : null
}

/**
 * Corrects what a customer record says about the person.
 *
 * `PUT`, and whole-record: the server compares every field against what it holds and records which
 * ones changed, so a field left out of `details` is a field being cleared, not a field being left
 * alone. The edit screen therefore sends the record it read back with the person's edits applied,
 * never a sparse patch.
 *
 * Three headers carry the things that make it safe, and `apiClient` puts all three on:
 *
 *  - `If-Match` is the version read from the record's own `GET`. A correction made against a version
 *    that is no longer current is refused with `409 customers.version-conflict` rather than quietly
 *    overwriting whatever a colleague saved in between.
 *  - `Idempotency-Key` makes a resend of *this* correction replay the first outcome instead of
 *    applying it twice.
 *  - The reason travels in the body because the endpoint is audited with `reasonRequired`, and a
 *    correction with no reason is a change nobody can question later.
 */
export async function correctCustomer(input: {
  readonly customerId: string
  readonly details: CustomerDetailsInput
  readonly reason: string
  readonly version: string
  readonly idempotencyKey: string
}): Promise<VersionedResponse<Customer>> {
  return await apiRequestVersioned<Customer>(`${CUSTOMERS}${input.customerId}`, {
    method: 'PUT',
    body: { ...input.details, reason: input.reason },
    ifMatch: input.version,
    idempotencyKey: input.idempotencyKey,
  })
}

/** The page size the server uses when the client names none. Matches `TimelineQuery.DefaultLimit`. */
export const CUSTOMER_TIMELINE_PAGE_SIZE = 25

/**
 * Reads one page of a customer's history, merged across every module that holds part of it.
 *
 * Newest first and cursor-paged: `nextCursor` is opaque and is sent back verbatim for the next page,
 * never decoded or constructed here. The composition happens in the web host rather than in any one
 * module — no module may read another's tables — which is why this route hangs off the customer and
 * not off a module of its own.
 *
 * What comes back is already filtered to what the caller may see: a module withholds the entries the
 * caller's permissions do not reach, and the approved response view decides which fields of the ones
 * that survive are populated. The client filters nothing and unmasks nothing.
 */
export async function readCustomerTimeline(
  input: { readonly customerId: string; readonly cursor?: string | undefined },
  signal?: AbortSignal,
): Promise<CustomerTimelinePage> {
  const query = new URLSearchParams(input.cursor === undefined ? {} : { cursor: input.cursor })
  const suffix = query.size === 0 ? '' : `?${query.toString()}`

  return await apiRequest<CustomerTimelinePage>(
    `${CUSTOMERS}${input.customerId}/timeline${suffix}`,
    { ...(signal === undefined ? {} : { signal }) },
  )
}

/**
 * Lists the records that may be the same person as this one.
 *
 * Scored as the records stand, not read back from the suspicions raised when either was created: a
 * correction to either can create a resemblance or remove one, and a merge is too final to take on a
 * score somebody computed months ago.
 *
 * Gated on `customers.read` and not `customers.merge`, deliberately — reading who might be a
 * duplicate is what Reception does *before* asking a manager to merge, and demanding the merge
 * permission to look would mean nobody could prepare the decision.
 */
export async function readDuplicateReview(
  customerId: string,
  signal?: AbortSignal,
): Promise<DuplicateReview> {
  return await apiRequest<DuplicateReview>(`${CUSTOMERS}${customerId}/duplicates`, {
    ...(signal === undefined ? {} : { signal }),
  })
}

/**
 * Folds one customer record into another. This cannot be undone.
 *
 * The record in the path **survives**; the one named in the body is folded into it. The direction is
 * decided by which identifier goes where, which is why it is two named fields here rather than a
 * pair of positional arguments a call site could transpose.
 *
 * ## Both records are preconditions, and they are not the same kind of precondition
 *
 * `If-Match` carries the survivor's version, and `mergedCustomerVersion` carries the folded record's.
 * What a manager approves is a *pair* — these two records, as they read on the screen, are one
 * person — and an `If-Match` alone protects only one half of that. The half it leaves open is the
 * record about to stop existing, so a correction to its name or number would otherwise be merged
 * away with no undo.
 *
 * `mergedCustomerVersion` is always a concrete version and never `*`: there is no such thing as "any
 * version" of a record somebody approved destroying.
 *
 * ## The step-up is the transport's job
 *
 * The endpoint demands a fresh proof of identity, and `apiClient` answers a
 * `403 security.step-up-required` by raising the re-authentication dialog and replaying this exact
 * request — same body, same version, same retry key. So this function does not ask first: doing so
 * would put a second dialog over the confirmation, and would still not remove the case where the
 * proof goes stale between the asking and the sending.
 */
export async function mergeCustomers(input: {
  /** The record that survives. */
  readonly customerId: string
  /** The record that does not. */
  readonly mergedCustomerId: string
  /** That record's version, from its own `GET`. Never `*`. */
  readonly mergedCustomerVersion: string
  readonly reason: string
  /** The survivor's version, from its own `GET`. */
  readonly version: string
  readonly idempotencyKey: string
}): Promise<CustomerMergeOutcome> {
  const merged = await apiRequestVersioned<CustomerMergeOutcome>(
    `${CUSTOMERS}${input.customerId}/merge`,
    {
      method: 'POST',
      body: {
        mergedCustomerId: input.mergedCustomerId,
        mergedCustomerVersion: input.mergedCustomerVersion,
        reason: input.reason,
      },
      ifMatch: input.version,
      idempotencyKey: input.idempotencyKey,
    },
  )

  return merged.value
}
