import type {
  AvailablePaymentMode,
  CashierSession,
  DispatchException,
  InvoiceBalance,
  InvoicePage,
  InvoiceSummary,
  OrderBalance,
  Payment,
  ReconciliationBatch,
} from '../types'

/**
 * Synthetic fixtures for the billing screens. No real name, telephone number, card number or
 * amount that resembles a real transaction appears here or in any story or test built on them.
 */

export const BRANCH_ID = '0199dd00-0000-7000-8000-0000000000b1'
export const CASHIER_ID = '0199dd00-0000-7000-8000-0000000000c1'
export const ORDER_ID = '0199dd00-0000-7000-8000-0000000000d1'
export const CUSTOMER_ID = '0199dd00-0000-7000-8000-0000000000e1'
export const INVOICE_ID = '0199dd00-0000-7000-8000-0000000000f1'
export const PAYMENT_ID = '0199dd00-0000-7000-8000-000000000a1'
export const RECEIPT_ID = '0199dd00-0000-7000-8000-000000000b2'
export const CASHIER_SESSION_ID = '0199dd00-0000-7000-8000-000000000c2'
export const DISPATCH_EXCEPTION_ID = '0199dd00-0000-7000-8000-000000000d2'
export const JOB_ID = '0199dd00-0000-7000-8000-000000000e2'

/** The modes a branch takes money in — cash, card and UPI, the three every walkthrough shows. */
export function anAvailablePaymentModeList(): readonly AvailablePaymentMode[] {
  return [
    {
      id: '0199dd00-0000-7000-8000-000000001001',
      code: 'CASH',
      name: 'Cash',
      requiresReference: false,
    },
    {
      id: '0199dd00-0000-7000-8000-000000001002',
      code: 'CARD',
      name: 'Card',
      requiresReference: true,
    },
    {
      id: '0199dd00-0000-7000-8000-000000001003',
      code: 'UPI',
      name: 'UPI',
      requiresReference: true,
    },
  ]
}

export function anInvoiceBalance(overrides: Partial<InvoiceBalance> = {}): InvoiceBalance {
  return {
    invoiceId: INVOICE_ID,
    invoiceNumber: 'I-CBE01-2627-000731',
    status: 'Posted',
    charges: 609,
    credits: 0,
    debits: 0,
    allocated: 300,
    refunds: 0,
    outstanding: 309,
    currency: 'INR',
    ...overrides,
  }
}

export function anOrderBalance(overrides: Partial<OrderBalance> = {}): OrderBalance {
  return {
    orderId: ORDER_ID,
    charges: 609,
    credits: 0,
    debits: 0,
    allocated: 300,
    refunds: 0,
    unappliedAdvances: 0,
    outstanding: 309,
    currency: 'INR',
    invoices: [anInvoiceBalance()],
    ...overrides,
  }
}

export function anInvoiceSummary(overrides: Partial<InvoiceSummary> = {}): InvoiceSummary {
  return {
    invoiceId: INVOICE_ID,
    customerId: CUSTOMER_ID,
    customerDisplayName: 'Kavitha (counter)',
    orderId: ORDER_ID,
    orderNumber: 'O-CBE01-2627-000512',
    status: 'Posted',
    grandTotal: 609,
    invoiceNumber: 'I-CBE01-2627-000731',
    createdAt: '2026-09-10T05:00:00.000Z',
    updatedAt: '2026-09-10T05:05:00.000Z',
    cancelled: false,
    ...overrides,
  }
}

export function anInvoicePage(overrides: Partial<InvoicePage> = {}): InvoicePage {
  return {
    invoices: [anInvoiceSummary()],
    nextCursor: null,
    ...overrides,
  }
}

export function aPayment(overrides: Partial<Payment> = {}): Payment {
  return {
    id: PAYMENT_ID,
    branchId: BRANCH_ID,
    cashierSessionId: CASHIER_SESSION_ID,
    cashierId: CASHIER_ID,
    customerId: CUSTOMER_ID,
    orderId: ORDER_ID,
    modeCode: 'UPI',
    amount: 309,
    currency: 'INR',
    reference: 'UPI-426114-8QX2',
    status: 'Recorded',
    recordedAt: '2026-09-12T05:00:00.000Z',
    recordedBy: CASHIER_ID,
    allocated: 309,
    unappliedAdvance: 0,
    allocations: [
      {
        id: '0199dd00-0000-7000-8000-000000002001',
        invoiceId: INVOICE_ID,
        advanceId: null,
        amount: 309,
        kind: 'Automatic',
        allocatedAt: '2026-09-12T05:00:00.000Z',
        allocatedBy: CASHIER_ID,
      },
    ],
    advance: null,
    receipt: {
      id: RECEIPT_ID,
      paymentId: PAYMENT_ID,
      branchId: BRANCH_ID,
      receiptNumber: 'R-CBE01-2627-001366',
      barcodePayload: 'R-CBE01-2627-001366-7',
      financialYear: '2026-27',
      issuedOn: '2026-09-12',
      issuedAt: '2026-09-12T05:00:00.000Z',
      amount: 309,
      allocated: 309,
      unappliedAdvance: 0,
      orderOutstanding: 0,
      currency: 'INR',
    },
    reversal: null,
    refundedFromAdvance: 0,
    ...overrides,
  }
}

export function aCashierSession(overrides: Partial<CashierSession> = {}): CashierSession {
  return {
    id: CASHIER_SESSION_ID,
    branchId: BRANCH_ID,
    cashierId: CASHIER_ID,
    status: 'Open',
    openingFloat: 2000,
    openedAt: '2026-09-12T03:30:00.000Z',
    closedAt: null,
    closedBy: null,
    expectedTotal: 0,
    countedTotal: 0,
    variance: 0,
    varianceReason: null,
    currency: 'INR',
    denominations: [],
    modeTotals: [],
    reconciliationBatch: null,
    ...overrides,
  }
}

export function aReconciliationBatch(
  overrides: Partial<ReconciliationBatch> = {},
): ReconciliationBatch {
  return {
    id: '0199dd00-0000-7000-8000-000000003001',
    status: 'PendingApproval',
    expectedTotal: 4350,
    recordedTotal: 4180,
    variance: -170,
    currency: 'INR',
    approvedBy: null,
    approvedAt: null,
    modeLines: [
      { modeCode: 'CASH', expected: 2350, recorded: 2180, variance: -170 },
      { modeCode: 'UPI', expected: 2000, recorded: 2000, variance: 0 },
    ],
    ...overrides,
  }
}

export function aDispatchException(overrides: Partial<DispatchException> = {}): DispatchException {
  return {
    id: DISPATCH_EXCEPTION_ID,
    branchId: BRANCH_ID,
    orderId: ORDER_ID,
    jobIds: [JOB_ID],
    maxOutstandingAmount: 500,
    currency: 'INR',
    policyVersion: 'W/"1"',
    reasonCode: 'CUSTOMER_TRAVELLING',
    approvedBy: '0199dd00-0000-7000-8000-000000004001',
    approvedAt: '2026-09-12T06:00:00.000Z',
    expiresAt: '2026-09-15T06:00:00.000Z',
    status: 'Active',
    ...overrides,
  }
}
