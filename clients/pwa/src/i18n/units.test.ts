import { describe, expect, it } from 'vitest'
import {
  INCH_FRACTION_STEPS,
  MILLIMETRES_PER_INCH,
  centimetresToMillimetres,
  inchFractionToMillimetres,
  millimetresToCentimetres,
  millimetresToInchFraction,
  roundMillimetres,
} from './units'
import type { InchFractionStep } from './units'

describe('inch fractions', () => {
  it('splits the worked example from the measurement templates', () => {
    // Reception enters 14 1/2 in; the client sends 368.30 mm.
    expect(millimetresToInchFraction(368.3, 8)).toEqual({
      negative: false,
      whole: 14,
      numerator: 1,
      denominator: 2,
    })
  })

  it('reduces the fraction rather than leaving eighths and sixteenths unsimplified', () => {
    expect(
      millimetresToInchFraction(
        inchFractionToMillimetres({ whole: 3, numerator: 4, denominator: 8 }),
        8,
      ),
    ).toEqual({ negative: false, whole: 3, numerator: 1, denominator: 2 })
    expect(
      millimetresToInchFraction(
        inchFractionToMillimetres({ whole: 3, numerator: 8, denominator: 16 }),
        16,
      ),
    ).toEqual({ negative: false, whole: 3, numerator: 1, denominator: 2 })
  })

  it('reports a whole number of inches with no fraction at all', () => {
    expect(millimetresToInchFraction(36 * MILLIMETRES_PER_INCH, 8)).toEqual({
      negative: false,
      whole: 36,
      numerator: 0,
      denominator: 1,
    })
  })

  it('carries a negative ease allowance on its own flag, not on the whole part', () => {
    const fraction = millimetresToInchFraction(-38.1, 8)
    expect(fraction).toEqual({ negative: true, whole: 1, numerator: 1, denominator: 2 })
    expect(inchFractionToMillimetres(fraction)).toBe(-38.1)
  })

  it('refuses a denominator of zero rather than returning Infinity', () => {
    expect(() => inchFractionToMillimetres({ whole: 1, numerator: 1, denominator: 0 })).toThrow(
      /denominator/,
    )
  })
})

/**
 * The round-trip guarantee docs/prd/measurement-templates.md section 2 asks for:
 * `toDisplay(fromDisplay(x)) === x` at the field's precision, in both display units. Two decimal
 * places of millimetre storage is what lets a 1/16 in step — 1.5875 mm — survive it.
 */
describe('the round trip', () => {
  it.each(INCH_FRACTION_STEPS)(
    'holds for every fraction at a step of 1/%i',
    (step: InchFractionStep) => {
      for (let whole = 0; whole <= 40; whole += 1) {
        for (let numerator = 0; numerator < step; numerator += 1) {
          const millimetres = roundMillimetres(
            inchFractionToMillimetres({ whole, numerator, denominator: step }),
          )
          const back = millimetresToInchFraction(millimetres, step)
          const reconstructed = back.whole + back.numerator / back.denominator
          expect(reconstructed).toBeCloseTo(whole + numerator / step, 10)
        }
      }
    },
  )

  it('holds for centimetres at one decimal place', () => {
    for (let tenths = 0; tenths <= 2000; tenths += 1) {
      const centimetres = tenths / 10
      expect(millimetresToCentimetres(centimetresToMillimetres(centimetres))).toBeCloseTo(
        centimetres,
        10,
      )
    }
  })

  it('does not leave binary floating-point noise on a centimetre value', () => {
    expect(String(millimetresToCentimetres(368))).toBe('36.8')
  })
})

describe('roundMillimetres', () => {
  it('keeps two decimal places, which is what a sixteenth of an inch needs', () => {
    expect(roundMillimetres(MILLIMETRES_PER_INCH / 16)).toBe(1.59)
  })
})
