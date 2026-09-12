import { describe, expect, it } from 'vitest'
import {
  NOTE_DENOMINATIONS,
  cashVariance,
  countedCashTotal,
  emptyCashCount,
  noteValue,
  toDenominationRequests,
} from './denominationCount'
import type { CashCount } from './denominationCount'

describe('denominationCount', () => {
  it('starts every note face value at zero with no coins', () => {
    const empty = emptyCashCount()

    expect(empty.notes.map((note) => note.denomination)).toEqual([...NOTE_DENOMINATIONS])
    expect(empty.notes.every((note) => note.quantity === 0)).toBe(true)
    expect(empty.coinsValue).toBe(0)
    expect(countedCashTotal(empty)).toBe(0)
  })

  it('values one note face value as denomination times quantity', () => {
    expect(noteValue({ denomination: 500, quantity: 3 })).toBe(1500)
    expect(noteValue({ denomination: 2000, quantity: 0 })).toBe(0)
  })

  it('totals every note face value plus the coins tray as one figure', () => {
    const count: CashCount = {
      notes: [
        { denomination: 2000, quantity: 2 },
        { denomination: 500, quantity: 3 },
        { denomination: 200, quantity: 1 },
        { denomination: 100, quantity: 0 },
        { denomination: 50, quantity: 0 },
        { denomination: 20, quantity: 0 },
        { denomination: 10, quantity: 5 },
      ],
      coinsValue: 47.5,
    }

    // 4000 + 1500 + 200 + 50 (ten notes) + 47.50 coins
    expect(countedCashTotal(count)).toBe(5797.5)
  })

  it('rounds the total to the nearest paisa, so binary floating-point error never surfaces', () => {
    const count: CashCount = {
      notes: NOTE_DENOMINATIONS.map((denomination) => ({ denomination, quantity: 0 })),
      coinsValue: 0.1 + 0.2, // the canonical floating-point trap: 0.30000000000000004 unrounded
    }

    expect(countedCashTotal(count)).toBe(0.3)
  })

  it('reports a variance of zero when the count matches what was expected', () => {
    expect(cashVariance(4350, 4350)).toBe(0)
  })

  it('reports a positive variance when the drawer holds more than expected', () => {
    expect(cashVariance(4400, 4350)).toBe(50)
  })

  it('reports a negative variance when the drawer holds less than expected — a shortfall', () => {
    expect(cashVariance(4300, 4350)).toBe(-50)
  })

  it('rounds a variance computed from unrounded totals', () => {
    expect(cashVariance(10.1 + 0.2, 10.3)).toBe(0)
  })

  it('sends only the note face values actually counted, dropping an untouched zero', () => {
    const count: CashCount = {
      notes: [
        { denomination: 2000, quantity: 1 },
        { denomination: 500, quantity: 0 },
        { denomination: 200, quantity: 0 },
        { denomination: 100, quantity: 0 },
        { denomination: 50, quantity: 0 },
        { denomination: 20, quantity: 0 },
        { denomination: 10, quantity: 0 },
      ],
      coinsValue: 0,
    }

    expect(toDenominationRequests(count)).toEqual([{ denomination: 2000, quantity: 1 }])
  })

  it('sends the coins as one line of quantity one, at the counted value', () => {
    const count: CashCount = { notes: emptyCashCount().notes, coinsValue: 63.25 }

    expect(toDenominationRequests(count)).toEqual([{ denomination: 63.25, quantity: 1 }])
  })

  it('sends nothing for a tray nobody counted', () => {
    expect(toDenominationRequests(emptyCashCount())).toEqual([])
  })

  it('never sends a negative quantity, which the field itself refuses to hold', () => {
    const count: CashCount = {
      notes: [{ denomination: 2000, quantity: -1 }, ...emptyCashCount().notes.slice(1)],
      coinsValue: 0,
    }

    expect(toDenominationRequests(count)).toEqual([])
  })
})
