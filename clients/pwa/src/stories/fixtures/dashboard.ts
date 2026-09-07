import type { IconName } from '../../components/primitives/icons'

/**
 * The owner's dashboard. Synthetic — see the note at the top of `branch.ts`.
 *
 * Two things here are requirements rather than sample content.
 *
 * **Every tile carries its period.** Step 2 of `A11Y-RJ-08` is "read each tile with its label and
 * its period", because "₹48,200" with no period is a number nobody can act on and a screen reader
 * announces it with no way to ask.
 *
 * **Every chart carries the rows it was drawn from.** Step 3 is "reach a chart's table or text
 * alternative from the keyboard". So the fixture is the table, and any chart drawn later is drawn
 * *from* it — which is the only ordering that cannot end with a canvas nobody can read.
 */
export interface DashboardTile {
  readonly id: string
  readonly label: string
  /** What the figure covers, in words: "this month", "the last 7 days". */
  readonly period: string
  /** Already a display string where the value is not money; money stays a number. */
  readonly value: string
  readonly icon: IconName
  /** True where this role may not read the figure, which is a state and not an error. */
  readonly forbidden?: boolean
}

export const TILES: readonly DashboardTile[] = [
  {
    id: 'sales',
    label: 'Sales',
    period: 'This month, 1 to 7 May',
    value: '48200',
    icon: 'rupee',
  },
  {
    id: 'openOrders',
    label: 'Open orders',
    period: 'Now',
    value: '37',
    icon: 'clipboard',
  },
  {
    id: 'overdue',
    label: 'Overdue jobs',
    period: 'Now',
    value: '2',
    icon: 'alert-triangle',
  },
  {
    id: 'payroll',
    label: 'Staff cost',
    period: 'This month',
    value: '',
    icon: 'users',
    forbidden: true,
  },
]

/** One row of the pipeline table, which is also the chart's text alternative. */
export interface PipelineRow {
  readonly phase: string
  readonly jobs: number
  readonly overdue: number
}

export const PIPELINE: readonly PipelineRow[] = [
  { phase: 'Cutting', jobs: 9, overdue: 0 },
  { phase: 'Stitching', jobs: 14, overdue: 1 },
  { phase: 'Finishing', jobs: 8, overdue: 1 },
  { phase: 'QC', jobs: 6, overdue: 0 },
]

/** An alert on the dashboard. Each names its subject in words and units, never as a code. */
export interface DashboardAlert {
  readonly id: string
  readonly title: string
  readonly detail: string
  readonly actionLabel: string
}

export const ALERTS: readonly DashboardAlert[] = [
  {
    id: 'low-stock-hooks',
    title: 'Hook card — 12 pairs is below its reorder level',
    detail: '8 cards on hand in Drawer B1, reorder level 15 cards, shortfall 7 cards.',
    actionLabel: 'Open the stock item',
  },
  {
    id: 'held-job',
    title: 'One job is held for material',
    detail:
      'J-CBE01-2627-000689-01, Blouse — Aari: stones and beads short by one kit, waiting on the supplier.',
    actionLabel: 'Open the held job',
  },
]

/**
 * The sales report's reconciliation footer.
 *
 * Step 7 of `A11Y-RJ-08` is "read a report with its reconciliation footer, and hear the totals
 * labelled". The footer exists so that the figures on screen can be tied back to something: the
 * lines, the taxes and the total have to add up in front of the person reading them.
 */
export const SALES_REPORT = {
  period: '1 to 7 May 2026',
  rows: [
    { id: 'blouse', category: 'Blouse', invoices: 18, taxableValue: 12400 },
    { id: 'salwar', category: 'Salwar', invoices: 11, taxableValue: 14300 },
    { id: 'lehenga', category: 'Lehenga', invoices: 3, taxableValue: 18200 },
  ],
  taxableValue: 44900,
  centralTax: 1122.5,
  stateTax: 1122.5,
  roundOff: 0,
  total: 47145,
} as const
