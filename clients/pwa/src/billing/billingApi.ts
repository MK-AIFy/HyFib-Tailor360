import { apiRequest } from '../auth/apiClient'
import type {
  AllocateAdvanceRequest,
  ApproveReconciliationRequest,
  AvailablePaymentMode,
  CashierSession,
  CloseCashierSessionRequest,
  CreateDispatchExceptionRequest,
  DispatchException,
  InvoicePage,
  OpenCashierSessionRequest,
  OrderBalance,
  OutstandingBalanceRow,
  Payment,
  PrintJob,
  PrintReceiptRequest,
  ReconciliationBatch,
  RecordPaymentRequest,
} from './types'

/**
 * Every call the billing screens make, named for what a person at the counter or in the office is
 * doing.
 *
 * ## What every write here carries
 *
 * **A retry key**, in `Idempotency-Key`, minted when the person commits to the act and held until it
 * succeeds. Recording a payment, closing a session and approving a dispatch exception are each acts
 * that must never happen twice, and the key is what turns a retry into a replay of the first outcome
 * rather than a second one.
 *
 * **A step-up challenge** on the three acts the permission matrix flags for it —
 * `AllocateAdvance`, `ApproveReconciliation` and `ApproveDispatchException` — passed as
 * `challengeOnStepUp: true` so `apiClient` raises the re-authentication dialog on a
 * `403 security.step-up-required` and replays the same request once signed back in, rather than the
 * screen answering the refusal itself.
 *
 * Every branch scope is the caller's own: none of these routes accept a branch parameter, because the
 * server reads it from the session (`docs/security/permission-matrix.md`).
 */

const BILLING = '/api/v1/billing'

/* Taking a payment. -------------------------------------------------------------------------- */

/** The active payment modes the caller's branch may take money in. */
export async function listAvailablePaymentModes(
  signal?: AbortSignal,
): Promise<readonly AvailablePaymentMode[]> {
  return await apiRequest<readonly AvailablePaymentMode[]>(`${BILLING}/payment-modes/available`, {
    ...(signal === undefined ? {} : { signal }),
  })
}

/** What an order still owes across its posted invoices, and what is held against it. */
export async function getOrderBalance(
  orderId: string,
  signal?: AbortSignal,
): Promise<OrderBalance> {
  return await apiRequest<OrderBalance>(`${BILLING}/orders/${orderId}/balance`, {
    ...(signal === undefined ? {} : { signal }),
  })
}

/**
 * Records a payment in the caller's open cashier session and allocates it at once, oldest posted
 * invoice first; what is left is held as an advance. Issues the receipt in the same transaction.
 */
export async function recordPayment(input: {
  readonly body: RecordPaymentRequest
  readonly idempotencyKey: string
}): Promise<Payment> {
  return await apiRequest<Payment>(`${BILLING}/payments`, {
    method: 'POST',
    body: input.body,
    idempotencyKey: input.idempotencyKey,
  })
}

/** Reads one payment with its allocations and what of it is still held. */
export async function getPayment(paymentId: string, signal?: AbortSignal): Promise<Payment> {
  return await apiRequest<Payment>(`${BILLING}/payments/${paymentId}`, {
    ...(signal === undefined ? {} : { signal }),
  })
}

/* Allocating an advance by hand. -------------------------------------------------------------- */

/**
 * Applies part of a payment's held advance to a posted invoice of the same order, against the
 * automatic rule. Step-up and a reason.
 */
export async function allocateAdvance(input: {
  readonly paymentId: string
  readonly body: AllocateAdvanceRequest
  readonly idempotencyKey: string
}): Promise<Payment> {
  return await apiRequest<Payment>(`${BILLING}/payments/${input.paymentId}/allocations`, {
    method: 'POST',
    body: input.body,
    idempotencyKey: input.idempotencyKey,
    challengeOnStepUp: true,
  })
}

/* Outstanding balances. ------------------------------------------------------------------------ */

/**
 * The branch's invoices, newest first. `status` is one of `Draft`, `Posted` or `Discarded`; the
 * branch is always the caller's own.
 */
export async function listInvoices(input: {
  readonly status?: string
  readonly cursor?: string
  readonly limit?: number
  readonly signal?: AbortSignal
}): Promise<InvoicePage> {
  const query = new URLSearchParams()
  if (input.status !== undefined) {
    query.set('status', input.status)
  }
  if (input.cursor !== undefined) {
    query.set('cursor', input.cursor)
  }
  if (input.limit !== undefined) {
    query.set('limit', String(input.limit))
  }
  const suffix = query.size === 0 ? '' : `?${query.toString()}`

  return await apiRequest<InvoicePage>(`${BILLING}/invoices${suffix}`, {
    ...(input.signal === undefined ? {} : { signal: input.signal }),
  })
}

/**
 * The branch's posted invoices with money still owed against them.
 *
 * There is no `ListInvoices`-style aggregate for this — `GetOrderBalance` answers one order at a
 * time — so this reads a page of the branch's posted invoices and asks each invoice's order for its
 * balance, keeping only the invoice's own line where it still shows an outstanding amount. A branch
 * runs a bounded number of open invoices at once, so the fan-out is small; a true aggregate read is
 * a reasonable follow-up once this list needs to grow past one page.
 */
export async function listOutstandingBalances(
  signal?: AbortSignal,
): Promise<readonly OutstandingBalanceRow[]> {
  const page = await listInvoices({
    status: 'Posted',
    limit: 50,
    ...(signal === undefined ? {} : { signal }),
  })

  const rows = await Promise.all(
    page.invoices.map(async (invoice): Promise<OutstandingBalanceRow | null> => {
      const order = await getOrderBalance(invoice.orderId, signal)
      const line = order.invoices.find((candidate) => candidate.invoiceId === invoice.invoiceId)
      if (line === undefined || Number(line.outstanding) <= 0) {
        return null
      }
      return {
        invoiceId: invoice.invoiceId,
        invoiceNumber: invoice.invoiceNumber,
        orderId: invoice.orderId,
        orderNumber: invoice.orderNumber,
        customerDisplayName: invoice.customerDisplayName,
        grandTotal: invoice.grandTotal,
        outstanding: line.outstanding,
        currency: line.currency,
      }
    }),
  )

  return rows.filter((row): row is OutstandingBalanceRow => row !== null)
}

/* The cashier session. ------------------------------------------------------------------------- */

/** The branch's cashier sessions, newest first, optionally filtered to one status. */
export async function listCashierSessions(
  status?: string,
  signal?: AbortSignal,
): Promise<readonly CashierSession[]> {
  const suffix = status === undefined ? '' : `?${new URLSearchParams({ status }).toString()}`
  return await apiRequest<readonly CashierSession[]>(`${BILLING}/cashier-sessions${suffix}`, {
    ...(signal === undefined ? {} : { signal }),
  })
}

/** Reads one session with its count sheet, once it has one. */
export async function getCashierSession(
  sessionId: string,
  signal?: AbortSignal,
): Promise<CashierSession> {
  return await apiRequest<CashierSession>(`${BILLING}/cashier-sessions/${sessionId}`, {
    ...(signal === undefined ? {} : { signal }),
  })
}

/** Opens a session at the caller's branch with the float put in the drawer. */
export async function openCashierSession(input: {
  readonly body: OpenCashierSessionRequest
  readonly idempotencyKey: string
}): Promise<CashierSession> {
  return await apiRequest<CashierSession>(`${BILLING}/cashier-sessions`, {
    method: 'POST',
    body: input.body,
    idempotencyKey: input.idempotencyKey,
  })
}

/**
 * Closes a session against its denomination sheet and the counted totals by mode. Only the cashier
 * who opened it may close it; a variance beyond the configured threshold needs a reason. No
 * `If-Match` — the row itself is the guard, so a second close of the same session is a 409.
 */
export async function closeCashierSession(input: {
  readonly sessionId: string
  readonly body: CloseCashierSessionRequest
  readonly idempotencyKey: string
}): Promise<CashierSession> {
  return await apiRequest<CashierSession>(`${BILLING}/cashier-sessions/${input.sessionId}/close`, {
    method: 'POST',
    body: input.body,
    idempotencyKey: input.idempotencyKey,
  })
}

/**
 * Approves a closed session's variance, by someone other than the cashier who closed it. Step-up
 * and a reason.
 */
export async function approveReconciliation(input: {
  readonly sessionId: string
  readonly body: ApproveReconciliationRequest
  readonly idempotencyKey: string
}): Promise<ReconciliationBatch> {
  return await apiRequest<ReconciliationBatch>(
    `${BILLING}/cashier-sessions/${input.sessionId}/reconciliation/approve`,
    {
      method: 'POST',
      body: input.body,
      idempotencyKey: input.idempotencyKey,
      challengeOnStepUp: true,
    },
  )
}

/* The dispatch gate. --------------------------------------------------------------------------- */

/**
 * Approves a single-use dispatch exception for named jobs of an order. Step-up and a reason; the
 * Owner only.
 */
export async function approveDispatchException(input: {
  readonly body: CreateDispatchExceptionRequest
  readonly idempotencyKey: string
}): Promise<DispatchException> {
  return await apiRequest<DispatchException>(`${BILLING}/dispatch-exceptions`, {
    method: 'POST',
    body: input.body,
    idempotencyKey: input.idempotencyKey,
    challengeOnStepUp: true,
  })
}

/* The receipt. ---------------------------------------------------------------------------------- */

/**
 * Sends the rendered receipt to the branch's print queue. Acknowledged only: the interim adapter
 * (ADR-0014) logs the job, and no printer is reached until #55's bridge replaces it.
 */
export async function printReceipt(input: {
  readonly receiptId: string
  readonly body: PrintReceiptRequest
  readonly idempotencyKey: string
}): Promise<PrintJob> {
  return await apiRequest<PrintJob>(`${BILLING}/receipts/${input.receiptId}/print`, {
    method: 'POST',
    body: input.body,
    idempotencyKey: input.idempotencyKey,
  })
}
