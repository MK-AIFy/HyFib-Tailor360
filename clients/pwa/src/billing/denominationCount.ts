import type { DenominationCountRequest } from './types'

/**
 * The arithmetic behind the cashier's denomination count sheet (#165).
 *
 * Kept apart from the screen so the sums a variance turns on can be tested without rendering
 * anything, and so the screen never carries its own copy of them. The server recomputes the same
 * total from the same rows on close (`CloseCashierSession`) and is the only version that is ever
 * stored — this module exists so the person sees that total, and the variance it produces, before
 * they press Close rather than after.
 *
 * ## Notes counted, coins totalled
 *
 * The note denominations in circulation are counted one at a time — ₹2000, ₹500, ₹200, ₹100, ₹50,
 * ₹20 and ₹10 — because a note tray is sorted that way and a wrong count of one face value should
 * not hide inside a lump sum. Coins are not: a drawer's coin tray is counted as a running total on
 * a coin-counting scale or by hand as one figure, never face value by face value, so the sheet asks
 * for that figure directly rather than pretending a cashier counts fifty 1-rupee coins one at a
 * time. The two are combined into `CloseCashierSessionRequest.denominations` as one list, the coins
 * line carrying `quantity: 1` at its counted value — a value the schema's `denomination` field
 * permits, being any positive amount rather than a fixed face value.
 */

/** The note face values this sheet counts, largest first — the order a note tray is sorted in. */
export const NOTE_DENOMINATIONS = [2000, 500, 200, 100, 50, 20, 10] as const

export type NoteDenomination = (typeof NOTE_DENOMINATIONS)[number]

/** One face value's count, notes only. */
export interface NoteCount {
  readonly denomination: NoteDenomination
  /** A whole number of notes. Negative and fractional counts are refused by the field itself. */
  readonly quantity: number
}

/** What the cashier counted: each note face value, and the coins as one total. */
export interface CashCount {
  readonly notes: readonly NoteCount[]
  /** The coin tray's counted value, as one figure. Zero when there were none to count. */
  readonly coinsValue: number
}

/** A count sheet with every note face value at zero and no coins — the sheet's starting state. */
export function emptyCashCount(): CashCount {
  return {
    notes: NOTE_DENOMINATIONS.map((denomination) => ({ denomination, quantity: 0 })),
    coinsValue: 0,
  }
}

/**
 * Rounds to the nearest paisa, which is the precision every amount on this screen is held to.
 *
 * `+0` rather than whatever sign the rounding left behind: a variance of "true zero" computed from
 * two unrounded totals can round to `-0`, which prints the same as `0` but compares unequal to it
 * and would read as "−₹0.00" the one time a formatter chose to show the sign.
 */
function roundToPaisa(value: number): number {
  return Math.round(value * 100) / 100 || 0
}

/** One note face value's counted value: `denomination × quantity`. */
export function noteValue(note: NoteCount): number {
  return roundToPaisa(note.denomination * note.quantity)
}

/** What the whole tray counts to: every note face value's value, plus the coins. */
export function countedCashTotal(count: CashCount): number {
  const notesTotal = count.notes.reduce((total, note) => total + noteValue(note), 0)
  return roundToPaisa(notesTotal + count.coinsValue)
}

/**
 * How far the counted total is from what was expected. Positive is over, negative is short — the
 * same sign `ModeTotalPayload.variance` carries, so a screen can show one number for the drawer
 * before close and the same number the server answers with after it.
 */
export function cashVariance(countedTotal: number, expectedTotal: number): number {
  return roundToPaisa(countedTotal - expectedTotal)
}

/**
 * `CloseCashierSessionRequest.denominations`, from the counted sheet.
 *
 * A note line is sent only when it was actually counted — a zero quantity is a face value nobody
 * saw in the tray, not a fact worth a row — and the coins line is sent only when there was a coin
 * value to report, as `quantity: 1` at that value.
 */
export function toDenominationRequests(count: CashCount): readonly DenominationCountRequest[] {
  const notes = count.notes
    .filter((note) => note.quantity > 0)
    .map((note) => ({ denomination: note.denomination, quantity: note.quantity }))

  return count.coinsValue > 0
    ? [...notes, { denomination: roundToPaisa(count.coinsValue), quantity: 1 }]
    : notes
}
