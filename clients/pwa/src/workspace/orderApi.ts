import { apiRequestVersioned } from '../auth/apiClient'
import type { VersionedResponse } from '../auth/apiClient'

export interface DraftGarment {
  readonly garmentId: string
  readonly serviceTypeId: string
  readonly catalogVersionId: string
  readonly categoryCode: string
  readonly serviceCode: string
  readonly serviceName: string
  readonly quantity: number
  readonly notes: string | null
}

export interface OrderDraft {
  readonly draftId: string
  readonly customerId: string
  readonly customerNumber: string
  readonly customerName: string
  readonly createdAt: string
  readonly updatedAt: string
  readonly expiresAt: string
  readonly garments: readonly DraftGarment[]
}

const DRAFTS = '/api/v1/orders/drafts'

export async function startOrderDraft(
  customerId: string,
  idempotencyKey: string,
): Promise<VersionedResponse<OrderDraft>> {
  return await apiRequestVersioned<OrderDraft>(DRAFTS, {
    method: 'POST',
    body: { customerId },
    idempotencyKey,
  })
}

export async function readOrderDraft(
  draftId: string,
  signal?: AbortSignal,
): Promise<VersionedResponse<OrderDraft>> {
  return await apiRequestVersioned<OrderDraft>(`${DRAFTS}/${draftId}`, {
    ...(signal === undefined ? {} : { signal }),
  })
}

export async function addOrderDraftGarment(input: {
  readonly draftId: string
  readonly serviceTypeId: string
  readonly quantity: number
  readonly notes: string | null
  readonly version: string
  readonly idempotencyKey: string
}): Promise<VersionedResponse<OrderDraft>> {
  return await apiRequestVersioned<OrderDraft>(`${DRAFTS}/${input.draftId}/garments`, {
    method: 'POST',
    body: {
      serviceTypeId: input.serviceTypeId,
      quantity: input.quantity,
      notes: input.notes,
    },
    ifMatch: input.version,
    idempotencyKey: input.idempotencyKey,
  })
}
