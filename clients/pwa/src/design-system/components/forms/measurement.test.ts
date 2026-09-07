import { describe, expect, it } from 'vitest'
import { MILLIMETRES_PER_INCH } from '../../../i18n/units'
import {
  MEASUREMENT_EXAMPLE_MILLIMETRES,
  clampToBounds,
  evaluateMeasurement,
  fractionOptions,
  fractionPartsToMillimetres,
  isConfirmationBand,
  isInchFractionStep,
  isMeasurementDisplayUnit,
  isRejectedBand,
  millimetresToFractionParts,
} from './measurement'
import type { MeasurementBounds } from './measurement'

/**
 * The bands and the strip, against the seeded template values themselves.
 *
 * The numbers below are copied from docs/prd/measurement-templates.md section 9.1 — the
 * `MT_BLOUSE_PATTERN` waist and front-neck-depth rows — rather than invented, so that a change to
 * the arithmetic that would let a real template value through the wrong band fails here.
 */

/** `MT_BLOUSE_PATTERN.waist`: hard 450–1500 mm, confirmation band 610–1220 mm. */
const WAIST: MeasurementBounds = {
  minimumMillimetres: 450,
  maximumMillimetres: 1500,
  warnBelowMillimetres: 610,
  warnAboveMillimetres: 1220,
}

describe('evaluateMeasurement', () => {
  it('accepts a value inside the confirmation band without comment', () => {
    expect(evaluateMeasurement(800, WAIST)).toBe('ok')
  })

  it('treats both hard bounds as inclusive — measuring exactly the extreme is not a mistake', () => {
    expect(evaluateMeasurement(450, WAIST)).toBe('below-usual')
    expect(evaluateMeasurement(1500, WAIST)).toBe('above-usual')
  })

  it('rejects outside the hard bounds', () => {
    expect(evaluateMeasurement(449.99, WAIST)).toBe('below-minimum')
    expect(evaluateMeasurement(1500.01, WAIST)).toBe('above-maximum')
  })

  it('warns inside the confirmation band', () => {
    expect(evaluateMeasurement(609.99, WAIST)).toBe('below-usual')
    expect(evaluateMeasurement(1220.01, WAIST)).toBe('above-usual')
    expect(evaluateMeasurement(610, WAIST)).toBe('ok')
    expect(evaluateMeasurement(1220, WAIST)).toBe('ok')
  })

  it('lets a hard bound win over the confirmation band', () => {
    // A value below the minimum is also below the warning threshold; it must reject, not warn,
    // because a rejected value blocks the capture and a warning does not.
    expect(isRejectedBand(evaluateMeasurement(100, WAIST))).toBe(true)
    expect(isConfirmationBand(evaluateMeasurement(100, WAIST))).toBe(false)
  })

  it('is silent when a template declares no bounds at all', () => {
    expect(evaluateMeasurement(1_000_000)).toBe('ok')
  })

  it('applies one bound when only one is given', () => {
    expect(evaluateMeasurement(10, { minimumMillimetres: 30 })).toBe('below-minimum')
    expect(evaluateMeasurement(10, { maximumMillimetres: 30 })).toBe('ok')
  })

  it('catches the mistake the bands exist for: centimetres typed into an inch field', () => {
    // A tailor means 36 in (914.4 mm) and types 36 into a field reading centimetres: 360 mm.
    expect(evaluateMeasurement(360, WAIST)).toBe('below-minimum')
  })
})

describe('fractionOptions', () => {
  it('offers eighths, reduced the way a tape is read', () => {
    expect(fractionOptions(8).map((option) => option.text)).toEqual([
      '0',
      '1/8',
      '1/4',
      '3/8',
      '1/2',
      '5/8',
      '3/4',
      '7/8',
    ])
  })

  it('offers sixteenths for the shaping and neckline fields', () => {
    const texts = fractionOptions(16).map((option) => option.text)
    expect(texts).toHaveLength(16)
    expect(texts[1]).toBe('1/16')
    expect(texts[2]).toBe('1/8')
    expect(texts[8]).toBe('1/2')
    expect(texts[15]).toBe('15/16')
  })

  it('gives every option the inch value the strip compares against', () => {
    const half = fractionOptions(8).find((option) => option.text === '1/2')
    expect(half?.inches).toBe(0.5)
  })
})

describe('the fraction parts round trip', () => {
  it('splits 15 3/8 in the way a tailor reads it', () => {
    const millimetres = 15.375 * MILLIMETRES_PER_INCH
    expect(millimetresToFractionParts(millimetres, 8)).toEqual({
      whole: 15,
      numerator: 3,
      denominator: 8,
    })
  })

  it('reassembles to the millimetres it came from', () => {
    expect(fractionPartsToMillimetres({ whole: 15, numerator: 3, denominator: 8 })).toBe(390.53)
  })

  it('round-trips every eighth and every sixteenth of the first two feet', () => {
    for (const step of [8, 16] as const) {
      for (let sixteenths = 0; sixteenths < 24 * step; sixteenths += 1) {
        const inches = sixteenths / step
        const millimetres = Math.round(inches * MILLIMETRES_PER_INCH * 100) / 100
        const parts = millimetresToFractionParts(millimetres, step)
        expect(fractionPartsToMillimetres(parts)).toBeCloseTo(millimetres, 1)
      }
    }
  })

  it('reports a whole number of inches as a zero fraction, not as 0/8', () => {
    expect(millimetresToFractionParts(MILLIMETRES_PER_INCH * 12, 8)).toEqual({
      whole: 12,
      numerator: 0,
      denominator: 1,
    })
  })
})

describe('clampToBounds', () => {
  it('holds a stepped value inside the template range', () => {
    expect(clampToBounds(100, WAIST)).toBe(450)
    expect(clampToBounds(2000, WAIST)).toBe(1500)
    expect(clampToBounds(800, WAIST)).toBe(800)
  })

  it('leaves a value alone where the template sets no bound', () => {
    expect(clampToBounds(-5, {})).toBe(-5)
  })
})

describe('the guards', () => {
  it('knows which display units a measurement field offers', () => {
    expect(isMeasurementDisplayUnit('in')).toBe(true)
    expect(isMeasurementDisplayUnit('cm')).toBe(true)
    // Millimetres are storage, never a display unit — section 2 of the templates document.
    expect(isMeasurementDisplayUnit('mm')).toBe(false)
  })

  it('knows which fraction steps a template may declare', () => {
    expect(isInchFractionStep(8)).toBe(true)
    expect(isInchFractionStep(16)).toBe(true)
    expect(isInchFractionStep(3)).toBe(false)
  })

  it('has an example value that reads naturally in both units', () => {
    expect(millimetresToFractionParts(MEASUREMENT_EXAMPLE_MILLIMETRES, 8)).toEqual({
      whole: 15,
      numerator: 3,
      denominator: 8,
    })
  })
})
