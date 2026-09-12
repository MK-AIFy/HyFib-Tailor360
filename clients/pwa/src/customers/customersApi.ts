import { apiRequest } from '../auth/apiClient'
import type { CustomerPage } from './types'

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
