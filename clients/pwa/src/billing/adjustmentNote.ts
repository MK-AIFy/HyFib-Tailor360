import type { Invoice } from './types'

/**
 * The arithmetic behind issuing a credit or debit note (#354): what one garment-job line of an
 * invoice still carries, and what the rows a person has typed will move in total.
 *
 * Kept apart from the screen for the same reason `denominationCount.ts` is: the sums a note's own
 * form turns on can be tested without rendering anything, and the screen never carries its own copy
 * of them. The server recomputes `remainingTaxableValueOf` from the same rows when the note is
 * posted (`Invoice.RemainingTaxableValueOf` on the server) and is the only version that is ever
 * stored — this module exists so the person sees the figure, and the running total it bounds, before
 * they press Post rather than after. Nothing computed here is ever sent as a figure the server
 * trusts: only the per-row taxable value the person typed travels in the request body, and a credit
 * that exceeds what a line still carries is refused server-side (`billing.note-exceeds-line`)
 * whatever this module showed.
 */

/**
 * What one garment-job line of an invoice still carries, to the paisa.
 *
 * The line's own `taxableValue` less every **credit**-note line already posted against the same
 * job — a debit note is never subtracted, mirroring the server's own `RemainingTaxableValueOf`
 * exactly, because a debit note adds to what is owed rather than relieving it. A line the invoice
 * does not carry answers zero rather than throwing, since the caller has already read `garmentJobId`
 * from the invoice's own lines and a mismatch here is a defect in the caller, not a value to explain.
 */
export function remainingTaxableValueOf(invoice: Invoice, garmentJobId: string): number {
  const line = invoice.lines.find((candidate) => candidate.garmentJobId === garmentJobId)
  if (line === undefined) {
    return 0
  }

  const credited = invoice.notes
    .filter((note) => note.kind === 'Credit')
    .flatMap((note) => note.lines)
    .filter((noteLine) => noteLine.garmentJobId === garmentJobId)
    .reduce((total, noteLine) => total + Number(noteLine.taxableValue), 0)

  return Number(line.taxableValue) - credited
}

/** One row of the note form: the garment job, and the taxable value typed against it. */
export interface NoteRow {
  readonly garmentJobId: string
  readonly taxableValue: number
}

/** What the note will move in total: the sum of every row's typed taxable value. */
export function noteTotalOf(rows: readonly NoteRow[]): number {
  return rows.reduce((total, row) => total + row.taxableValue, 0)
}
