import type {
  AdjustmentNote,
  AvailablePaymentMode,
  BarcodeResolution,
  CashierSession,
  DispatchException,
  Invoice,
  InvoiceBalance,
  InvoiceLine,
  InvoicePage,
  InvoiceSummary,
  InvoiceTotals,
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

const NOTE_ID = '0199dd00-0000-7000-8000-000000005001'
const CANCELLATION_ID = '0199dd00-0000-7000-8000-000000005002'

/**
 * One stitched line and one altered line with a loyalty discount, carrying real GST components.
 *
 * The totals fixture below is deliberately built to leave a non-zero round-off — checklist item
 * A11Y-BI-06 has to be answered on an invoice that carries one, because a zero round-off is not
 * rendered at all.
 */
export function anInvoiceLineList(): readonly InvoiceLine[] {
  return [
    {
      lineNumber: 1,
      garmentJobId: JOB_ID,
      itemCode: 'STITCH_BLOUSE',
      description: 'Blouse stitching',
      quantity: 1,
      catalogueRate: 505,
      appliedRate: 505,
      base: 505,
      surcharges: [],
      discountRuleCode: null,
      discountKind: null,
      discountValue: null,
      discountAmount: 0,
      gross: 505,
      taxableValue: 505,
      taxCode: 'GST5',
      classification: '998821',
      taxCodeKind: 'Services',
      taxes: [
        { kind: 'CGST', ratePercent: 2.5, amount: 12.63 },
        { kind: 'SGST', ratePercent: 2.5, amount: 12.63 },
      ],
      taxTotal: 25.26,
      lineTotal: 530.26,
      variance: 0,
    },
    {
      lineNumber: 2,
      garmentJobId: '0199dd00-0000-7000-8000-000000005003',
      itemCode: 'ALTER_SLEEVE',
      description: 'Sleeve alteration',
      quantity: 1,
      catalogueRate: 200,
      appliedRate: 200,
      base: 200,
      surcharges: [],
      discountRuleCode: 'FESTIVE10',
      discountKind: 'Percentage',
      discountValue: 10,
      discountAmount: 20,
      gross: 180,
      taxableValue: 180,
      taxCode: 'GST5',
      classification: '998821',
      taxCodeKind: 'Services',
      taxes: [
        { kind: 'CGST', ratePercent: 2.5, amount: 4.5 },
        { kind: 'SGST', ratePercent: 2.5, amount: 4.5 },
      ],
      taxTotal: 9,
      lineTotal: 189,
      variance: 0,
    },
  ]
}

/** Taxable value 685.00, CGST/SGST 17.13 each, a 0.74 round-off up to the whole rupee 720.00. */
export function anInvoiceTotals(overrides: Partial<InvoiceTotals> = {}): InvoiceTotals {
  return {
    subtotal: 705,
    discountTotal: 20,
    taxableValue: 685,
    centralTax: 17.13,
    stateTax: 17.13,
    integratedTax: 0,
    cess: 0,
    roundOff: 0.74,
    grandTotal: 720,
    ...overrides,
  }
}

export function anInvoice(overrides: Partial<Invoice> = {}): Invoice {
  return {
    invoiceId: INVOICE_ID,
    branchId: BRANCH_ID,
    customerId: CUSTOMER_ID,
    orderId: ORDER_ID,
    orderNumber: 'O-CBE01-2627-000512',
    status: 'Posted',
    revision: 1,
    customer: {
      customerNumber: 'C-CBE01-000201',
      displayName: 'Kavitha (counter)',
      addressLine: '12 Second Street, Demo Nagar',
      locality: 'Peelamedu',
      postcode: '641004',
    },
    calculation: {
      reference: `order:${ORDER_ID}:1`,
      priceListVersionId: '0199dd00-0000-7000-8000-000000005004',
      taxConfigurationVersionId: '0199dd00-0000-7000-8000-000000005005',
      gstRegistrationId: '0199dd00-0000-7000-8000-000000005006',
      gstin: '33AAACH7409R1Z8',
      supplierStateCode: '33',
      placeOfSupplyStateCode: '33',
      scheme: 'IntraState',
      taxInclusive: false,
      supplierLegalName: 'Example Tailors Private Limited',
      supplierTradeName: 'Example Tailors',
    },
    currency: 'INR',
    lines: anInvoiceLineList(),
    totals: anInvoiceTotals(),
    createdAt: '2026-09-12T05:00:00.000Z',
    updatedAt: '2026-09-12T05:30:00.000Z',
    discardedAt: null,
    discardReason: null,
    orderRevisionNumber: 1,
    invoiceNumber: 'INV-CBE01-2627-000731',
    barcodePayload: 'I-7K3M9QW2XZ4B',
    financialYear: '2627',
    postedOn: '2026-09-12',
    postedAt: '2026-09-12T05:30:00.000Z',
    cancelled: false,
    cancellation: null,
    notes: [],
    ...overrides,
  }
}

/** A credit note relieving the lining surcharge line, posted with its own reason. */
export function anAdjustmentNote(overrides: Partial<AdjustmentNote> = {}): AdjustmentNote {
  return {
    noteId: NOTE_ID,
    invoiceId: INVOICE_ID,
    kind: 'Credit',
    number: 'CN-CBE01-2627-000045',
    reason: 'Lining charged twice.',
    currency: 'INR',
    lines: [
      {
        lineNumber: 1,
        garmentJobId: JOB_ID,
        taxableValue: 90,
        taxes: [
          { kind: 'CGST', ratePercent: 2.5, amount: 2.25 },
          { kind: 'SGST', ratePercent: 2.5, amount: 2.25 },
        ],
        taxTotal: 4.5,
        lineTotal: 94.5,
      },
    ],
    totals: {
      subtotal: 90,
      discountTotal: 0,
      taxableValue: 90,
      centralTax: 2.25,
      stateTax: 2.25,
      integratedTax: 0,
      cess: 0,
      roundOff: 0,
      grandTotal: 94.5,
    },
    postedAt: '2026-09-12T06:00:00.000Z',
    ...overrides,
  }
}

/** A cancelled invoice: the compensating credit note relieves the whole amount. */
export function aCancelledInvoice(overrides: Partial<Invoice> = {}): Invoice {
  return anInvoice({
    cancelled: true,
    cancellation: {
      cancellationId: CANCELLATION_ID,
      creditNoteId: NOTE_ID,
      reason: 'Issued to the wrong customer.',
      cancelledAt: '2026-09-12T07:00:00.000Z',
    },
    notes: [
      anAdjustmentNote({
        number: 'CN-CBE01-2627-000046',
        reason: 'Issued to the wrong customer.',
        totals: {
          subtotal: 705,
          discountTotal: 20,
          taxableValue: 685,
          centralTax: 17.13,
          stateTax: 17.13,
          integratedTax: 0,
          cess: 0,
          roundOff: 0.74,
          grandTotal: 720,
        },
        lines: [
          {
            lineNumber: 1,
            garmentJobId: JOB_ID,
            taxableValue: 685,
            taxes: [
              { kind: 'CGST', ratePercent: 2.5, amount: 17.13 },
              { kind: 'SGST', ratePercent: 2.5, amount: 17.13 },
            ],
            taxTotal: 34.26,
            lineTotal: 719.26,
          },
        ],
      }),
    ],
    ...overrides,
  })
}

export function aBarcodeResolution(overrides: Partial<BarcodeResolution> = {}): BarcodeResolution {
  return {
    invoiceId: INVOICE_ID,
    invoiceNumber: 'INV-CBE01-2627-000731',
    branchId: BRANCH_ID,
    customerId: CUSTOMER_ID,
    orderId: ORDER_ID,
    status: 'Posted',
    cancelled: false,
    grandTotal: 720,
    currency: 'INR',
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
