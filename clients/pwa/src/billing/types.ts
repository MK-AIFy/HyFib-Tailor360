/**
 * The billing payloads this client reads and writes (#161-#165), pinned against the published
 * contract in `api/contract.ts`.
 *
 * ## Money is never arithmetic on this side
 *
 * Every amount arrives as an unformatted decimal — a number or a string, per COD-01 — and this
 * module never adds, multiplies or rounds one. The one exception is the denomination count sheet
 * (`denominationCount.ts`), which totals what a cashier counted so the screen can show it before the
 * server is asked; the server recomputes the same total from the same rows and is the only version
 * that is ever stored.
 */

/** A payment mode the caller's branch may take money in — cash, card, UPI or a similar rail. */
export interface AvailablePaymentMode {
  readonly id: string
  /** The stable code sent as `modeCode` — `CASH`, `CARD`, `UPI`. Not translated. */
  readonly code: string
  readonly name: string
  /** Whether recording a payment in this mode must carry a reference. */
  readonly requiresReference: boolean
}

/** One invoice's own balance, as it stands inside an order's. */
export interface InvoiceBalance {
  readonly invoiceId: string
  readonly invoiceNumber: string
  readonly status: string
  readonly charges: number | string
  readonly credits: number | string
  readonly debits: number | string
  readonly allocated: number | string
  readonly refunds: number | string
  readonly outstanding: number | string
  readonly currency: string
}

/**
 * What an order still owes across its posted invoices, and what is held against it.
 *
 * Computed from rows on every read (INV-PAY-06) — never cached by this client, and never re-derived
 * from anything the client already holds.
 */
export interface OrderBalance {
  readonly orderId: string
  readonly charges: number | string
  readonly credits: number | string
  readonly debits: number | string
  readonly allocated: number | string
  readonly refunds: number | string
  readonly unappliedAdvances: number | string
  readonly outstanding: number | string
  readonly currency: string
  readonly invoices: readonly InvoiceBalance[]
}

/** One of the branch's invoices, as the list shows it — without its lines. */
export interface InvoiceSummary {
  readonly invoiceId: string
  readonly customerId: string
  readonly customerDisplayName: string
  readonly orderId: string
  readonly orderNumber: string
  readonly status: string
  readonly grandTotal: number | string
  readonly invoiceNumber: string | null
  readonly createdAt: string
  readonly updatedAt: string
  readonly cancelled: boolean
}

/** A page of the branch's invoices, newest first. */
export interface InvoicePage {
  readonly invoices: readonly InvoiceSummary[]
  readonly nextCursor: string | null
}

/** An order's outstanding balance, joined to the invoice it was found from, for one screen's list. */
export interface OutstandingBalanceRow {
  readonly invoiceId: string
  readonly invoiceNumber: string | null
  readonly orderId: string
  readonly orderNumber: string
  readonly customerDisplayName: string
  readonly grandTotal: number | string
  readonly outstanding: number | string
  readonly currency: string
}

/** One rupee received against an advance, once a payment allocated part of it. */
export interface PaymentAllocation {
  readonly id: string
  readonly invoiceId: string
  readonly advanceId: string | null
  readonly amount: number | string
  /** `Automatic` (oldest posted invoice first) or `Manual` (`payments.allocate_manual`). */
  readonly kind: string
  readonly allocatedAt: string
  readonly allocatedBy: string | null
}

/** What a payment left unapplied, held against the order until it is allocated or refunded. */
export interface Advance {
  readonly id: string
  readonly amount: number | string
  readonly unapplied: number | string
  readonly receivedAt: string
}

/** How and why a payment was reversed. The original row is never touched. */
export interface PaymentReversal {
  readonly id: string
  readonly reason: string
  readonly reversedAt: string
  readonly reversedBy: string | null
}

/** The receipt a payment issues, in the same transaction it is recorded in. */
export interface Receipt {
  readonly id: string
  readonly paymentId: string
  readonly branchId: string
  readonly receiptNumber: string
  /** The `R-` barcode payload. Never a URL — resolved only through `ResolveReceiptBarcode`. */
  readonly barcodePayload: string
  readonly financialYear: string
  readonly issuedOn: string
  readonly issuedAt: string
  readonly amount: number | string
  readonly allocated: number | string
  readonly unappliedAdvance: number | string
  readonly orderOutstanding: number | string
  readonly currency: string
}

/** A payment recorded in a cashier's open session, allocated at once and receipted. */
export interface Payment {
  readonly id: string
  readonly branchId: string
  readonly cashierSessionId: string
  readonly cashierId: string
  readonly customerId: string
  readonly orderId: string
  readonly modeCode: string
  readonly amount: number | string
  readonly currency: string
  readonly reference: string | null
  readonly status: string
  readonly recordedAt: string
  readonly recordedBy: string | null
  readonly allocated: number | string
  readonly unappliedAdvance: number | string
  readonly allocations: readonly PaymentAllocation[]
  readonly advance: Advance | null
  readonly receipt: Receipt | null
  readonly reversal: PaymentReversal | null
  readonly refundedFromAdvance: number | string
}

/** What is sent to record a payment. */
export interface RecordPaymentRequest {
  readonly orderId: string
  readonly modeCode: string | null
  readonly amount: number
  readonly reference: string | null
}

/** What is sent to move part of a held advance to a posted invoice, against the automatic rule. */
export interface AllocateAdvanceRequest {
  readonly invoiceId: string
  readonly amount: number
  readonly reason: string | null
}

/** One face value counted, as the server holds it once a session is closed. */
export interface DenominationCount {
  readonly denomination: number | string
  readonly quantity: number | string
  readonly value: number | string
}

/** What is sent for one face value counted. */
export interface DenominationCountRequest {
  readonly denomination: number
  readonly quantity: number
}

/** What a mode's takings counted to, against what the session's own records expect. */
export interface ModeTotal {
  readonly modeCode: string
  readonly expected: number | string
  readonly counted: number | string
  readonly variance: number | string
}

/** What is sent for one mode's counted total. */
export interface ModeCountRequest {
  readonly modeCode: string | null
  readonly counted: number
}

/** One mode's variance inside an approved reconciliation batch. */
export interface ReconciliationBatchModeLine {
  readonly modeCode: string
  readonly expected: number | string
  readonly recorded: number | string
  readonly variance: number | string
}

/** The variance a closed session's takings were checked against, and its approval if one exists. */
export interface ReconciliationBatch {
  readonly id: string
  readonly status: string
  readonly expectedTotal: number | string
  readonly recordedTotal: number | string
  readonly variance: number | string
  readonly currency: string
  readonly approvedBy: string | null
  readonly approvedAt: string | null
  readonly modeLines: readonly ReconciliationBatchModeLine[]
}

/** A cashier's day at the drawer, from opening float to the reconciled close. */
export interface CashierSession {
  readonly id: string
  readonly branchId: string
  readonly cashierId: string
  readonly status: string
  readonly openingFloat: number | string
  readonly openedAt: string
  readonly closedAt: string | null
  readonly closedBy: string | null
  readonly expectedTotal: number | string
  readonly countedTotal: number | string
  readonly variance: number | string
  readonly varianceReason: string | null
  readonly currency: string
  readonly denominations: readonly DenominationCount[]
  readonly modeTotals: readonly ModeTotal[]
  readonly reconciliationBatch: ReconciliationBatch | null
}

/** What is sent to open a session with the float put in the drawer. */
export interface OpenCashierSessionRequest {
  readonly openingFloat: number
}

/** What is sent to close a session against its count sheet. */
export interface CloseCashierSessionRequest {
  readonly denominations: readonly DenominationCountRequest[] | null
  readonly modeTotals: readonly ModeCountRequest[] | null
  readonly reason: string | null
}

/** What is sent to approve a closed session's variance. */
export interface ApproveReconciliationRequest {
  readonly reason: string | null
}

/** A single-use permission to dispatch named jobs of an order despite what it still owes. */
export interface DispatchException {
  readonly id: string
  readonly branchId: string
  readonly orderId: string
  readonly jobIds: readonly string[]
  readonly maxOutstandingAmount: number | string
  readonly currency: string
  readonly policyVersion: string
  readonly reasonCode: string
  readonly approvedBy: string
  readonly approvedAt: string
  readonly expiresAt: string
  readonly status: string
}

/** What is sent to approve a dispatch exception. */
export interface CreateDispatchExceptionRequest {
  readonly orderId: string
  readonly jobIds: readonly string[] | null
  readonly maxOutstandingAmount: number
  readonly reasonCode: string | null
  readonly reasonText: string | null
  readonly expiresAt: string
}

/** What a print-queue submission is acknowledged with — nothing prints until #55's bridge lands. */
export interface PrintJob {
  readonly printJobId: string
}

/** What is sent to print a receipt. Null asks for the server's own default of one copy. */
export interface PrintReceiptRequest {
  readonly copies: number | null
}
