import { describe, expect, it } from 'vitest'
import {
  COIN_DENOMINATIONS,
  NOTE_DENOMINATIONS,
  cashVariance,
  coinValue,
  countedCashTotal,
  emptyCashCount,
  noteValue,
  toDenominationRequests,
} from './denominationCount'
import type { CashCount } from './denominationCount'

describe('denominationCount', () => {
  it('starts every note and coin face value at zero', () => {
    const empty = emptyCashCount()

    expect(empty.notes.map((note) => note.denomination)).toEqual([...NOTE_DENOMINATIONS])
    expect(empty.notes.every((note) => note.quantity === 0)).toBe(true)
    expect(empty.coins.map((coin) => coin.denomination)).toEqual([...COIN_DENOMINATIONS])
    expect(empty.coins.every((coin) => coin.quantity === 0)).toBe(true)
    expect(countedCashTotal(empty)).toBe(0)
  })

  it('values one note face value as denomination times quantity', () => {
    expect(noteValue({ denomination: 500, quantity: 3 })).toBe(1500)
    expect(noteValue({ denomination: 2000, quantity: 0 })).toBe(0)
  })

  it('values one coin face value as denomination times quantity', () => {
    expect(coinValue({ denomination: 5, quantity: 4 })).toBe(20)
    expect(coinValue({ denomination: 1, quantity: 0 })).toBe(0)
  })

  it('totals every note face value plus every coin face value counted', () => {
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
      coins: [
        { denomination: 5, quantity: 6 },
        { denomination: 2, quantity: 4 },
        { denomination: 1, quantity: 3 },
      ],
    }

    // 4000 + 1500 + 200 + 50 (ten notes) + 30 + 8 + 3 (coins)
    expect(countedCashTotal(count)).toBe(5791)
  })

  it('never reports an untouched tray as negative zero', () => {
    // `roundToPaisa`'s `+0` is what keeps a "true zero" from a subtraction printing as "−₹0.00" the
    // one time a formatter chose to show the sign; an empty tray is the simplest case that would
    // otherwise round to `-0`.
    expect(Object.is(countedCashTotal(emptyCashCount()), -0)).toBe(false)
    expect(countedCashTotal(emptyCashCount())).toBe(0)
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
      coins: emptyCashCount().coins,
    }

    expect(toDenominationRequests(count)).toEqual([{ denomination: 2000, quantity: 1 }])
  })

  // Codex review, PR #217: the server's `CashDenominations.All` only recognises the note and coin
  // face values actually in circulation. Sending the coin tray as one aggregate value — the shape
  // this sheet used before this fix — is not one of those values, so the server refused it as
  // `billing.denomination-not-known` for any tray that held coins at all.
  it('sends only the coin face values actually counted, as real denominations rather than one aggregate', () => {
    const count: CashCount = {
      notes: emptyCashCount().notes,
      coins: [
        { denomination: 5, quantity: 12 },
        { denomination: 2, quantity: 0 },
        { denomination: 1, quantity: 5 },
      ],
    }

    expect(toDenominationRequests(count)).toEqual([
      { denomination: 5, quantity: 12 },
      { denomination: 1, quantity: 5 },
    ])
    // Every denomination this sheet can ever send is one the server's own list recognises.
    const known = new Set([2000, 500, 200, 100, 50, 20, 10, 5, 2, 1])
    for (const request of toDenominationRequests(count)) {
      expect(known.has(request.denomination)).toBe(true)
    }
  })

  it('sends nothing for a tray nobody counted', () => {
    expect(toDenominationRequests(emptyCashCount())).toEqual([])
  })

  it('never sends a negative quantity, which the field itself refuses to hold', () => {
    const count: CashCount = {
      notes: [{ denomination: 2000, quantity: -1 }, ...emptyCashCount().notes.slice(1)],
      coins: [{ denomination: 5, quantity: -1 }, ...emptyCashCount().coins.slice(1)],
    }

    expect(toDenominationRequests(count)).toEqual([])
  })
})
