import { describe, expect, it } from 'vitest'
import { createIntl, createIntlCache } from 'react-intl'
import { enIN } from '../../../i18n/en-IN'
import {
  INCH_FRACTION_STEPS,
  inchFractionToMillimetres,
  millimetresToInchFraction,
} from '../../../i18n/units'
import type { CentimetreDecimals } from '../../../i18n/units'
import { formatBound, formatMeasurementRange, unitForBands } from './measurementRange'

const intl = createIntl({ locale: 'en-IN', messages: enIN }, createIntlCache())

describe('a bound said the way a tailor reads it', () => {
  it('says a whole number of inches without a fraction nobody would speak', () => {
    expect(formatBound(intl, 914.4, { unit: 'in', step: 8 })).toBe('36 in')
  })

  it('says a fraction as a fraction, never as a decimal', () => {
    // 3.19 in is not a number anybody can find on a tape
    // (docs/prd/measurement-templates.md section 2).
    expect(formatBound(intl, 368.3, { unit: 'in', step: 8 })).toBe('14 1/2 in')
    expect(formatBound(intl, 390.5375, { unit: 'in', step: 16 })).toBe('15 3/8 in')
  })

  it('says centimetres at the precision the field declares, not at a fixed one', () => {
    expect(formatBound(intl, 368.3, { unit: 'cm', decimals: 1 })).toBe('36.8 cm')
    expect(formatBound(intl, 368.3, { unit: 'cm', decimals: 2 })).toBe('36.83 cm')
  })
})

describe('the whole band', () => {
  it('reads as a sentence in the unit on screen', () => {
    const said = formatMeasurementRange(
      intl,
      { minimumMillimetres: 390.5375, maximumMillimetres: 558.8 },
      { unit: 'in', step: 16 },
    )

    expect(said).toBe('Expected between 15 3/8 in and 22 in.')
  })

  it('says nothing at all for a field that declares no bounds', () => {
    // The sentinel is 0 / 0, which is "any measurement" rather than "only zero" — and
    // "between 0 mm and 0 mm" would say the second.
    expect(
      formatMeasurementRange(
        intl,
        { minimumMillimetres: 0, maximumMillimetres: 0 },
        { unit: 'in' },
      ),
    ).toBeUndefined()
  })

  it('still states a range whose lower bound happens to be zero', () => {
    // Only the pair being zero is the sentinel. A field accepting 0 to 50 mm is a real range and
    // must not be silently dropped.
    expect(
      formatMeasurementRange(
        intl,
        { minimumMillimetres: 0, maximumMillimetres: 50.8 },
        { unit: 'in', step: 8 },
      ),
    ).toBe('Expected between 0 in and 2 in.')
  })
})

/**
 * The property, swept rather than sampled.
 *
 * #100 established that the client's conversion agrees with `UnitConversion.cs`. This is the first
 * thing that *renders* it, and a display that rounds differently from the server is
 * indistinguishable to a tailor from a storage bug — so every value the step can express is checked
 * to survive the round trip, rather than three that happen to.
 */
describe('every value a step can express survives being displayed', () => {
  it.each(INCH_FRACTION_STEPS)('at 1/%s of an inch', (step) => {
    // Every representable value from 0 to 40 inches, which covers every bound in the seeded
    // templates and then some: 640 values at eighths, 1,280 at sixteenths.
    for (let steps = 0; steps <= 40 * step; steps += 1) {
      const millimetres = inchFractionToMillimetres({
        whole: 0,
        numerator: steps,
        denominator: step,
      })
      const said = formatBound(intl, millimetres, { unit: 'in', step })
      const parsed = millimetresToInchFraction(millimetres, step)

      // The rendered text is the parsed value, said. Reading it back gives the same millimetres,
      // which is what makes a bound a person typed the bound the server stores.
      expect(inchFractionToMillimetres(parsed)).toBe(millimetres)
      expect(said.endsWith(' in')).toBe(true)
      expect(said).not.toContain('.')
    }
  })

  it.each([0, 1, 2] as const)('at %s centimetre decimal places', (decimals: CentimetreDecimals) => {
    for (let tenths = 0; tenths <= 2000; tenths += 1) {
      const millimetres = tenths
      const said = formatBound(intl, millimetres, { unit: 'cm', decimals })
      const digits = said.replace(' cm', '').split('.')[1] ?? ''

      // Never more places than the field declares: a bound shown to three decimals is a bound
      // nobody can enter, and one shown to fewer is a bound that reads as a different number.
      expect(digits.length).toBeLessThanOrEqual(decimals)
    }
  })
})

describe('which unit a field is read in', () => {
  it('uses the only unit it declares a precision for', () => {
    expect(unitForBands({ inchFraction: 8, centimetreDecimals: 0 })).toBe('in')
    expect(unitForBands({ inchFraction: 0, centimetreDecimals: 1 })).toBe('cm')
  })

  it('follows the version when the field supports both', () => {
    // A reviewer checking a bound against a tape should not have to convert it in their head.
    expect(unitForBands({ inchFraction: 8, centimetreDecimals: 1 }, 'Centimetre')).toBe('cm')
    expect(unitForBands({ inchFraction: 8, centimetreDecimals: 1 }, 'Inch')).toBe('in')
    expect(unitForBands({ inchFraction: 8, centimetreDecimals: 1 })).toBe('in')
  })

  it('has none for a field with no precision at all, which is a count or a choice', () => {
    expect(unitForBands({ inchFraction: 0, centimetreDecimals: 0 })).toBeUndefined()
  })
})
