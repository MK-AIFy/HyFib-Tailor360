import { apiRequest } from '../auth/apiClient'

export interface CustomerCard {
  readonly customerId: string
  readonly customerNumber: string
  readonly displayName: string
  readonly nativeName: string | null
  readonly maskedPhone: string
  readonly visibleToCaller: boolean
  readonly status: string
  readonly lastSeenAt: string
}

export interface CustomerPage {
  readonly customers: readonly CustomerCard[]
  readonly nextCursor: string | null
}

export interface CustomerRecord {
  readonly customerId: string
  readonly customerNumber: string
  readonly displayName: string
  readonly nativeName: string | null
  readonly phone: string | null
  readonly email: string | null
  readonly locality: string | null
  readonly language: string
  readonly status: string
  readonly createdAt: string
}

export interface RegisterCustomerInput {
  readonly displayName: string
  readonly nativeName: string | null
  readonly phone: string | null
  readonly email: string | null
  readonly locality: string | null
  readonly language: string
  readonly duplicatesReviewed: boolean
}

export interface DuplicateCandidate {
  readonly customer: CustomerCard
  readonly confidence: string
  readonly reasons: readonly string[]
}

export async function searchCustomers(
  term: string,
  cursor?: string,
  signal?: AbortSignal,
): Promise<CustomerPage> {
  const query = new URLSearchParams({ term, limit: '20' })
  if (cursor !== undefined) query.set('cursor', cursor)
  return await apiRequest<CustomerPage>(`/api/v1/customers?${query.toString()}`, {
    ...(signal === undefined ? {} : { signal }),
  })
}

export async function readCustomer(
  customerId: string,
  signal?: AbortSignal,
): Promise<CustomerRecord> {
  return await apiRequest<CustomerRecord>(`/api/v1/customers/${customerId}`, {
    ...(signal === undefined ? {} : { signal }),
  })
}

export async function registerCustomer(
  input: RegisterCustomerInput,
  idempotencyKey: string,
): Promise<CustomerRecord> {
  return await apiRequest<CustomerRecord>('/api/v1/customers', {
    method: 'POST',
    body: input,
    idempotencyKey,
  })
}
