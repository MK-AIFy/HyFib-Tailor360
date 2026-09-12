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
 * ## Notes and coins, each counted one face value at a time
 *
 * `CashDenominations.All` on the server (`Tailor360.Modules.Billing.Domain.Payments`) names the
 * only values a count sheet may carry — the seven note face values and three coin face values in
 * circulation — and refuses anything else as `billing.denomination-not-known` rather than summing
 * it, so a typo in a denomination cannot balance a drawer. A coin tray totalled as one lump figure
 * (₹63.25, say) is not one of those values, so it was refused every time the tray held any coins at
 * all: coins are counted the same way notes are, one face value at a time, and sent as one line per
 * face value actually counted.
 */

/** The note face values this sheet counts, largest first — the order a note tray is sorted in. */
export const NOTE_DENOMINATIONS = [2000, 500, 200, 100, 50, 20, 10] as const

export type NoteDenomination = (typeof NOTE_DENOMINATIONS)[number]

/**
 * The coin face values this sheet counts, largest first — the only ones `CashDenominations.All`
 * recognises as coins. A coin tray is counted the same way a note tray is: one face value at a time.
 */
export const COIN_DENOMINATIONS = [5, 2, 1] as const

export type CoinDenomination = (typeof COIN_DENOMINATIONS)[number]

/** One face value's count, notes only. */
export interface NoteCount {
  readonly denomination: NoteDenomination
  /** A whole number of notes. Negative and fractional counts are refused by the field itself. */
  readonly quantity: number
}

/** One face value's count, coins only. */
export interface CoinCount {
  readonly denomination: CoinDenomination
  /** A whole number of coins. Negative and fractional counts are refused by the field itself. */
  readonly quantity: number
}

/** What the cashier counted: every note face value and every coin face value. */
export interface CashCount {
  readonly notes: readonly NoteCount[]
  readonly coins: readonly CoinCount[]
}

/** A count sheet with every face value at zero — the sheet's starting state. */
export function emptyCashCount(): CashCount {
  return {
    notes: NOTE_DENOMINATIONS.map((denomination) => ({ denomination, quantity: 0 })),
    coins: COIN_DENOMINATIONS.map((denomination) => ({ denomination, quantity: 0 })),
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

/** One coin face value's counted value: `denomination × quantity`. */
export function coinValue(coin: CoinCount): number {
  return roundToPaisa(coin.denomination * coin.quantity)
}

/** What the whole tray counts to: every note face value's value, plus every coin face value's. */
export function countedCashTotal(count: CashCount): number {
  const notesTotal = count.notes.reduce((total, note) => total + noteValue(note), 0)
  const coinsTotal = count.coins.reduce((total, coin) => total + coinValue(coin), 0)
  return roundToPaisa(notesTotal + coinsTotal)
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
 * A line is sent only when it was actually counted — a zero quantity is a face value nobody saw in
 * the tray, not a fact worth a row — whether that line is a note or a coin. Every denomination this
 * function can produce is one `CashDenominations.All` recognises, so the server never refuses one
 * of these lines as unknown.
 */
export function toDenominationRequests(count: CashCount): readonly DenominationCountRequest[] {
  const notes = count.notes
    .filter((note) => note.quantity > 0)
    .map((note) => ({ denomination: note.denomination, quantity: note.quantity }))
  const coins = count.coins
    .filter((coin) => coin.quantity > 0)
    .map((coin) => ({ denomination: coin.denomination, quantity: coin.quantity }))

  return [...notes, ...coins]
}
