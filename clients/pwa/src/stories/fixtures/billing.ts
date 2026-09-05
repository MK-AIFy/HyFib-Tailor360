import { DATES } from './branch'

/**
 * An invoice, its advance and the delivery queue. Synthetic — see the note at the top of
 * `branch.ts`.
 *
 * The money is walkthrough 1's: ₹580.00 taxable, CGST 2.5% and SGST 2.5% on an intra-state supply of
 * stitching services under SAC 998821, ₹609.00 total, ₹300.00 already taken as an advance. Those
 * rates are illustrative and confirmed by the accountant under OD-05 — the journey exercises the
 * arithmetic being *shown*, never the arithmetic being decided.
 *
 * Every amount here is a number of rupees, formatted at the point of display by the `formatters`
 * module. Nothing in this file contains a currency symbol: a fixture that pre-formats its own money
 * would make the Tamil and pseudo-locale stories quietly meaningless.
 */
export interface InvoiceLine {
  readonly id: string
  readonly description: string
  /** The garment job the line belongs to, so profitability can attribute it (walkthrough 3). */
  readonly job: string
  readonly amount: number
}

export const INVOICE = {
  number: 'INV-CBE01-2627-000731',
  order: 'O-CBE01-2627-000512',
  customer: 'Kavitha Raman',
  postedOn: DATES.today,
  sac: '998821',
  lines: [
    {
      id: 'line-1',
      description: 'Blouse stitching — pattern',
      job: 'J-CBE01-2627-000512-01',
      amount: 450,
    },
    {
      id: 'line-2',
      description: 'Katori cup lining',
      job: 'J-CBE01-2627-000512-01',
      amount: 90,
    },
    { id: 'line-3', description: 'Piping finish', job: 'J-CBE01-2627-000512-01', amount: 40 },
  ] as readonly InvoiceLine[],
  taxableValue: 580,
  centralTaxRate: 2.5,
  centralTax: 14.5,
  stateTaxRate: 2.5,
  stateTax: 14.5,
  roundOff: 0,
  total: 609,
  advanceReceived: 300,
  balance: 309,
} as const

export const PAYMENT_METHODS = [
  { value: 'cash', label: 'Cash' },
  { value: 'upi', label: 'UPI' },
  { value: 'card', label: 'Card' },
] as const

export const RECEIPT_NUMBER = 'RCPT-CBE01-2627-001366'

/**
 * The delivery queue, in the order the run is currently planned.
 *
 * `eligibility` is the dispatch gate's answer, not a status badge: `paid` opens the gate, `unpaid`
 * closes it, and walkthrough 3 turns on exactly that refusal (EX-10). A queue entry that cannot be
 * dispatched still has to be readable, reorderable and reachable — it is not hidden.
 */
export interface DeliveryEntry {
  readonly id: string
  readonly job: string
  readonly customer: string
  readonly garment: string
  readonly address: string
  readonly eligibility: 'paid' | 'unpaid'
  readonly balance: number
}

export const DELIVERY_QUEUE: readonly DeliveryEntry[] = [
  {
    id: 'DQ-01',
    job: 'J-CBE01-2627-000512-01',
    customer: 'Kavitha Raman',
    garment: 'Blouse — pattern',
    address: 'RS Puram, Coimbatore',
    eligibility: 'paid',
    balance: 0,
  },
  {
    id: 'DQ-02',
    job: 'J-CBE01-2627-000934-01',
    customer: 'Anitha Selvam',
    garment: 'Salwar — churidar',
    address: 'Saibaba Colony, Coimbatore',
    eligibility: 'unpaid',
    balance: 1043,
  },
  {
    id: 'DQ-03',
    job: 'J-CBE01-2627-001007-01',
    customer: 'Bhuvaneswari Karthik',
    garment: 'Lehenga — choli',
    address: 'Peelamedu, Coimbatore',
    eligibility: 'paid',
    balance: 0,
  },
]
