import { apiRequest } from '../auth/apiClient'
import type { PricingPreviewRequest, PricingResult } from './pricingPreviewTypes'

/**
 * The pricing preview (E09-F01-8b, #147's client): the same engine an order is priced by, run against
 * a named price-list version — draft or published — without storing the result.
 *
 * **No `Idempotency-Key`.** Every other write in this family carries one; this route does not, because
 * the endpoint declares no `.RequireIdempotency()` — it stores nothing, so there is no first outcome to
 * replay. Sending one anyway would be harmless but would misrepresent the route.
 */

const BILLING = '/api/v1/billing'

/** Prices `request` against the named version. Refuses per `billingProblems.ts`'s pricing entries. */
export async function previewPricing(
  request: PricingPreviewRequest,
  signal?: AbortSignal,
): Promise<PricingResult> {
  return await apiRequest<PricingResult>(`${BILLING}/pricing/preview`, {
    method: 'POST',
    body: request,
    ...(signal === undefined ? {} : { signal }),
  })
}
