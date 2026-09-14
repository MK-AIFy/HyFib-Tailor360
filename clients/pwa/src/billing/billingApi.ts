import { apiRequest, apiRequestBlob, apiRequestVersioned } from '../auth/apiClient'
import type { VersionedResponse } from '../auth/apiClient'
import type {
  AdjustmentNote,
  AllocateAdvanceRequest,
  ApproveReconciliationRequest,
  AvailablePaymentMode,
  BarcodeResolution,
  BillingReasonRequest,
  CashierSession,
  CloseCashierSessionRequest,
  CreateDispatchExceptionRequest,
  CreateInvoiceDraftRequest,
  DispatchException,
  DocumentDownload,
  Invoice,
  InvoicePage,
  OpenCashierSessionRequest,
  OrderBalance,
  OutstandingBalanceRow,
  Payment,
  PostAdjustmentNoteRequest,
  PrintInvoiceRequest,
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
 * Reads the balance an Owner is weighing a dispatch exception against, authorised by
 * `billing.approve_dispatch_exception` itself rather than the general-purpose balance read's own
 * `payments.record`, which the Owner does not hold (#220).
 */
export async function getOrderBalanceForDispatchException(
  orderId: string,
  signal?: AbortSignal,
): Promise<OrderBalance> {
  return await apiRequest<OrderBalance>(`${BILLING}/orders/${orderId}/dispatch-exception-balance`, {
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
 * time — so this reads every page of the branch's posted invoices, following `nextCursor` until the
 * server answers null, and asks each invoice's order for its balance (concurrently within a page, via
 * `Promise.all`), keeping only the invoice's own line where it still shows an outstanding amount.
 * Stopping at the first page (as this once did) silently dropped every older outstanding invoice past
 * the first fifty — a branch could even read as fully settled while an older invoice still owed
 * money.
 *
 * **Decision (#216), recorded rather than fixed silently**: defer a true server-side aggregate
 * endpoint. A branch runs a bounded number of posted, unsettled invoices at once, so the fan-out is
 * at most one page's worth of concurrent reads (≤ 50), not a serial N+1 — a cost this screen's own
 * loading state already accounts for. A branch that grows past a handful of pages of outstanding
 * invoices, or a fan-out that becomes visible in practice (a slow read, a rate-limit warning), is the
 * trigger to revisit this and add a single aggregate endpoint alongside `GetOrderBalance` instead.
 */
export async function listOutstandingBalances(
  signal?: AbortSignal,
): Promise<readonly OutstandingBalanceRow[]> {
  const rows: OutstandingBalanceRow[] = []
  let cursor: string | undefined

  for (;;) {
    const page = await listInvoices({
      status: 'Posted',
      limit: 50,
      ...(cursor === undefined ? {} : { cursor }),
      ...(signal === undefined ? {} : { signal }),
    })

    const pageRows = await Promise.all(
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

    rows.push(...pageRows.filter((row): row is OutstandingBalanceRow => row !== null))

    if (page.nextCursor === null) {
      return rows
    }
    cursor = page.nextCursor
  }
}

/* Reading one invoice, and finding one from its barcode. ---------------------------------------- */

/**
 * Reads an invoice with its lines, and the version an edit sends back as `If-Match`.
 *
 * `#42`'s later slices send `If-Match` back to cancel or correct a posted invoice; this slice reads
 * only, so the version travels along for whichever screen edits it next rather than being read
 * itself.
 */
export async function getInvoice(
  invoiceId: string,
  signal?: AbortSignal,
): Promise<VersionedResponse<Invoice>> {
  return await apiRequestVersioned<Invoice>(`${BILLING}/invoices/${invoiceId}`, {
    ...(signal === undefined ? {} : { signal }),
  })
}

/**
 * Resolves a scanned or typed `I-` barcode payload to the invoice it was printed on, for the
 * caller's own branch. Another branch's invoice, another organisation's, a payload whose check
 * character does not hold and a payload of nothing all answer alike — `billing.document-not-found` —
 * so this reveals nothing about what exists elsewhere.
 */
export async function resolveInvoiceBarcode(
  payload: string,
  signal?: AbortSignal,
): Promise<BarcodeResolution> {
  return await apiRequest<BarcodeResolution>(`${BILLING}/barcodes/${encodeURIComponent(payload)}`, {
    ...(signal === undefined ? {} : { signal }),
  })
}

/* Drafting, discarding and posting an invoice (#345). -------------------------------------------- */

/**
 * Drafts an invoice from an order's stored calculation. Nothing is re-priced: the lines are the
 * calculation's, exactly as the server returns them.
 */
export async function createInvoiceDraft(input: {
  readonly body: CreateInvoiceDraftRequest
  readonly idempotencyKey: string
  readonly signal?: AbortSignal
}): Promise<VersionedResponse<Invoice>> {
  return await apiRequestVersioned<Invoice>(`${BILLING}/invoices`, {
    method: 'POST',
    body: input.body,
    idempotencyKey: input.idempotencyKey,
    ...(input.signal === undefined ? {} : { signal: input.signal }),
  })
}

/**
 * Abandons a draft, freeing its garment jobs for another invoice. `SQ-06`: this is a status, not a
 * deletion — the draft is left readable, discarded.
 */
export async function discardInvoiceDraft(input: {
  readonly invoiceId: string
  readonly reason: string
  readonly version: string
  readonly idempotencyKey: string
}): Promise<VersionedResponse<Invoice>> {
  const body: BillingReasonRequest = { reason: input.reason }
  return await apiRequestVersioned<Invoice>(`${BILLING}/invoices/${input.invoiceId}/discard`, {
    method: 'POST',
    body,
    idempotencyKey: input.idempotencyKey,
    ifMatch: input.version,
  })
}

/**
 * Posts a draft: draws its invoice number under the branch/financial-year sequence and makes it
 * immutable. The reason is optional — posting is the ordinary path, not a correction.
 */
export async function postInvoice(input: {
  readonly invoiceId: string
  readonly reason: string | null
  readonly version: string
  readonly idempotencyKey: string
}): Promise<VersionedResponse<Invoice>> {
  const body: BillingReasonRequest = { reason: input.reason }
  return await apiRequestVersioned<Invoice>(`${BILLING}/invoices/${input.invoiceId}/post`, {
    method: 'POST',
    body,
    idempotencyKey: input.idempotencyKey,
    ifMatch: input.version,
  })
}

/* Cancelling an invoice, and issuing credit and debit notes (#354). ------------------------------ */

/**
 * Cancels a posted invoice by its compensating credit note, in one transaction: the invoice keeps
 * its number, its lines and its totals, and its garment jobs become free to invoice again.
 *
 * No `If-Match`: nothing on the invoice's row moves, so the server locks and re-reads the row inside
 * the transaction instead of asking for a precondition. `challengeOnStepUp` asks `apiClient` to raise
 * the re-authentication dialogue on `403 security.step-up-required` and replay this identical
 * request — same key, same reason — once signed back in; a call site never answers that refusal
 * itself.
 */
export async function cancelInvoice(input: {
  readonly invoiceId: string
  readonly reason: string
  readonly idempotencyKey: string
}): Promise<Invoice> {
  const body: BillingReasonRequest = { reason: input.reason }
  return await apiRequest<Invoice>(`${BILLING}/invoices/${input.invoiceId}/cancel`, {
    method: 'POST',
    body,
    idempotencyKey: input.idempotencyKey,
    challengeOnStepUp: true,
  })
}

/**
 * Posts a credit note against a posted invoice, relieving what its lines name. Each line is bounded
 * server-side to what the invoice line still carries after the credit notes already posted —
 * `billing.note-exceeds-line` if it is not, whatever `remainingTaxableValueOf` showed.
 */
export async function postCreditNote(input: {
  readonly invoiceId: string
  readonly body: PostAdjustmentNoteRequest
  readonly idempotencyKey: string
}): Promise<AdjustmentNote> {
  return await apiRequest<AdjustmentNote>(`${BILLING}/invoices/${input.invoiceId}/credit-notes`, {
    method: 'POST',
    body: input.body,
    idempotencyKey: input.idempotencyKey,
  })
}

/** Posts a debit note against a posted invoice, adding to what the customer owes. No upper bound. */
export async function postDebitNote(input: {
  readonly invoiceId: string
  readonly body: PostAdjustmentNoteRequest
  readonly idempotencyKey: string
}): Promise<AdjustmentNote> {
  return await apiRequest<AdjustmentNote>(`${BILLING}/invoices/${input.invoiceId}/debit-notes`, {
    method: 'POST',
    body: input.body,
    idempotencyKey: input.idempotencyKey,
  })
}

/* The invoice document: download and the print-station hand-off. -------------------------------- */

/**
 * Streams the stored invoice PDF. The file name comes from the server's `Content-Disposition`; the
 * invoice number is the fallback, for the one response shape the transport cannot read a name from.
 */
export async function downloadInvoiceDocument(
  invoiceId: string,
  fallbackFileName: string,
  signal?: AbortSignal,
): Promise<DocumentDownload> {
  return await apiRequestBlob(`${BILLING}/invoices/${invoiceId}/document`, {
    fallbackFileName,
    ...(signal === undefined ? {} : { signal }),
  })
}

/** Streams the stored PDF of a credit or debit note posted against an invoice. */
export async function downloadNoteDocument(input: {
  readonly invoiceId: string
  readonly noteId: string
  readonly fallbackFileName: string
  readonly signal?: AbortSignal
}): Promise<DocumentDownload> {
  return await apiRequestBlob(
    `${BILLING}/invoices/${input.invoiceId}/notes/${input.noteId}/document`,
    {
      fallbackFileName: input.fallbackFileName,
      ...(input.signal === undefined ? {} : { signal: input.signal }),
    },
  )
}

/**
 * Sends the rendered invoice to the branch's print queue. Acknowledged only: the interim adapter
 * (ADR-0014) logs the job, and no printer is reached until #205's bridge replaces it.
 */
export async function printInvoice(input: {
  readonly invoiceId: string
  readonly body: PrintInvoiceRequest
  readonly idempotencyKey: string
}): Promise<PrintJob> {
  return await apiRequest<PrintJob>(`${BILLING}/invoices/${input.invoiceId}/print`, {
    method: 'POST',
    body: input.body,
    idempotencyKey: input.idempotencyKey,
  })
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

/**
 * Reads the session an approver is reconciling, authorised by the approval permission itself
 * (`payments.approve_reconciliation`) rather than the general-purpose session read's own
 * `payments.session`, which the Owner does not hold (#220).
 */
export async function getCashierSessionForReconciliation(
  sessionId: string,
  signal?: AbortSignal,
): Promise<CashierSession> {
  return await apiRequest<CashierSession>(
    `${BILLING}/cashier-sessions/${sessionId}/reconciliation`,
    { ...(signal === undefined ? {} : { signal }) },
  )
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
