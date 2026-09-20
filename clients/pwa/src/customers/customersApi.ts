import { apiRequest, apiRequestVersioned } from '../auth/apiClient'
import type { VersionedResponse } from '../auth/apiClient'
import { CUSTOMER_DUPLICATES_CODE } from './types'
import type { Customer, CustomerDetailsInput, CustomerPage, DuplicateCandidate } from './types'

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
