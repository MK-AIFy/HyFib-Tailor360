import { describe, expect, it } from 'vitest'
import {
  INCH_FRACTION_STEPS,
  MILLIMETRES_PER_INCH,
  centimetresToMillimetres,
  inchFractionToMillimetres,
  inchesToMillimetres,
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

/* Agreement with the server ---------------------------------------------------------------------
 *
 * These are the tests issue #94 asks for and issue #100 was opened by: the client's conversion is
 * held to the server's *definition*, swept over every representable step rather than sampled.
 *
 * The oracle below is deliberately not this module. It is exact rational arithmetic in `BigInt`,
 * derived from the same two definitions `UnitConversion.cs` uses — 25.4 mm to the inch, 10 mm to
 * the centimetre, rounded half away from zero to two decimal places. A test that compared the
 * client against itself is what let a 0.01 mm disagreement stand on 146 of these values.
 */

/** `numerator / denominator` rounded half away from zero, exactly. */
function exactDivide(numerator: bigint, denominator: bigint): bigint {
  const negative = numerator < 0n !== denominator < 0n
  const top = numerator < 0n ? -numerator : numerator
  const bottom = denominator < 0n ? -denominator : denominator
  const quotient = top / bottom
  const remainder = top - quotient * bottom
  const magnitude = 2n * remainder >= bottom ? quotient + 1n : quotient

  return negative ? -magnitude : magnitude
}

/** What the server stores for an inch value of `steps / denominator`, in hundredths of a millimetre. */
function serverHundredthsForInches(steps: bigint, denominator: bigint): bigint {
  return exactDivide(steps * 2540n, denominator)
}

/** What the server stores for a centimetre value of `units / 10 ** scale`. */
function serverHundredthsForCentimetres(units: bigint, scale: bigint): bigint {
  return exactDivide(units * 1000n, 10n ** scale)
}

describe('agreement with the server', () => {
  it.each(INCH_FRACTION_STEPS)(
    'stores every representable value at a step of 1/%i exactly as the server does',
    (step) => {
      let swept = 0

      for (let steps = -60 * step; steps <= 60 * step; steps += 1) {
        const negative = steps < 0
        const magnitude = Math.abs(steps)
        const client = inchFractionToMillimetres({
          negative,
          whole: Math.floor(magnitude / step),
          numerator: magnitude % step,
          denominator: step,
        })
        const server = serverHundredthsForInches(BigInt(steps), BigInt(step))

        expect(Math.round(client * 100)).toBe(Number(server))
        swept += 1
      }

      expect(swept).toBeGreaterThan(200)
    },
  )

  it('stores a decimal inch value exactly as the server does', () => {
    // 0.375 in is the value that exposed the defect: `0.375 * 25.4 * 100` is 952.4999999999999 in
    // binary floating point, so the naive conversion sent 9.52 mm where the server stores 9.53.
    expect(inchesToMillimetres(0.375)).toBe(9.53)
    expect(Math.round(inchesToMillimetres(0.375) * 100)).toBe(
      Number(serverHundredthsForInches(3n, 8n)),
    )
  })

  it('stores every representable centimetre value exactly as the server does', () => {
    let swept = 0

    for (let tenths = -2000; tenths <= 2000; tenths += 1) {
      const client = centimetresToMillimetres(tenths / 10)
      const server = serverHundredthsForCentimetres(BigInt(tenths), 1n)

      expect(Math.round(client * 100)).toBe(Number(server))
      swept += 1
    }

    expect(swept).toBeGreaterThan(200)
  })

  it('rounds a midpoint away from zero in both directions, where Math.round does not', () => {
    // Math.round is half toward positive infinity: it agrees on the positive midpoint and disagrees
    // on the negative one. An ease allowance may be negative, which is why this matters here.
    expect(roundMillimetres(9.525)).toBe(9.53)
    expect(roundMillimetres(-9.525)).toBe(-9.53)
    expect(Math.round(-952.5)).toBe(-952)
  })

  it('rounds half up rather than to even, as the server does', () => {
    // The server's own test: 32.5 mm is exactly on the halfway mark for one decimal place, and
    // "the halfway mark sometimes goes down" is not a rule anybody can state at the counter.
    expect(millimetresToCentimetres(32.5, 1)).toBe(3.3)
    expect(millimetresToCentimetres(42.5, 1)).toBe(4.3)
  })

  it.each(INCH_FRACTION_STEPS)(
    'brings every representable value at a step of 1/%i back unchanged',
    (step) => {
      for (let steps = 0; steps <= 60 * step; steps += 1) {
        const whole = Math.floor(steps / step)
        const numerator = steps % step
        const stored = inchFractionToMillimetres({ whole, numerator, denominator: step })
        const shown = millimetresToInchFraction(stored, step)

        expect(shown.whole * step + (shown.numerator * step) / shown.denominator).toBe(steps)
      }
    },
  )

  it('renders a centimetre field at the precision it declares, not at one decimal place', () => {
    // A two-decimal field is legal — FieldPrecision.MaximumCentimetreDecimals is 2 — and rendered
    // at one decimal place until #100.
    expect(millimetresToCentimetres(368.35, 2)).toBe(36.84)
    expect(millimetresToCentimetres(368.35, 1)).toBe(36.8)
    expect(millimetresToCentimetres(368.35, 0)).toBe(37)
  })
})
