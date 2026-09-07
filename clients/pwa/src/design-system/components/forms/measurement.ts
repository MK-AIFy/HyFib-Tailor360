import {
  INCH_FRACTION_STEPS,
  MILLIMETRES_PER_INCH,
  millimetresToInchFraction,
} from '../../../i18n/units'
import type { DisplayUnit, InchFractionStep } from '../../../i18n/units'

/**
 * The measurement rules of docs/prd/measurement-templates.md, as pure functions.
 *
 * Nothing here renders, formats or knows a locale: a band decision and a fraction strip are
 * arithmetic, and keeping them out of the component is what lets the ranges of section 9 of that
 * document be tested exhaustively without a DOM.
 *
 * Everything is in canonical millimetres, because that is the only unit anything is ever stored in
 * (section 2). The display unit is a rendering concern and appears only in the formatting helpers
 * at the bottom.
 */

/** The display units a measurement field offers. Millimetres are never shown to staff. */
export const MEASUREMENT_DISPLAY_UNITS = ['in', 'cm'] as const

export type MeasurementDisplayUnit = (typeof MEASUREMENT_DISPLAY_UNITS)[number]

export function isMeasurementDisplayUnit(unit: DisplayUnit): unit is MeasurementDisplayUnit {
  return unit === 'in' || unit === 'cm'
}

/**
 * The two guard bands of section 7, kept in one shape.
 *
 * Hard bounds reject: the capture cannot be confirmed. The confirmation band accepts after an
 * explicit acknowledgement and never blocks a save — which is exactly why the warning has to be
 * announced rather than merely coloured (A11Y-ME-07).
 */
export interface MeasurementBounds {
  readonly minimumMillimetres?: number
  readonly maximumMillimetres?: number
  readonly warnBelowMillimetres?: number
  readonly warnAboveMillimetres?: number
}

export const MEASUREMENT_BANDS = [
  'ok',
  'below-minimum',
  'above-maximum',
  'below-usual',
  'above-usual',
] as const

export type MeasurementBand = (typeof MEASUREMENT_BANDS)[number]

/**
 * Which band a value falls in.
 *
 * Hard bounds are tested first and win, because a value outside them is rejected whatever the
 * confirmation band says. Both bounds are inclusive: section 9 states them as the extremes that are
 * still allowed, and a tailor who measures exactly the stated maximum has not made a mistake.
 */
export function evaluateMeasurement(
  millimetres: number,
  bounds: MeasurementBounds = {},
): MeasurementBand {
  const { minimumMillimetres, maximumMillimetres, warnBelowMillimetres, warnAboveMillimetres } =
    bounds

  if (minimumMillimetres !== undefined && millimetres < minimumMillimetres) {
    return 'below-minimum'
  }
  if (maximumMillimetres !== undefined && millimetres > maximumMillimetres) {
    return 'above-maximum'
  }
  if (warnBelowMillimetres !== undefined && millimetres < warnBelowMillimetres) {
    return 'below-usual'
  }
  if (warnAboveMillimetres !== undefined && millimetres > warnAboveMillimetres) {
    return 'above-usual'
  }
  return 'ok'
}

/** A rejected value: the field is invalid and the step cannot be confirmed. */
export function isRejectedBand(band: MeasurementBand): boolean {
  return band === 'below-minimum' || band === 'above-maximum'
}

/** An unusual but permitted value: it needs the acknowledgement of section 7, never a block. */
export function isConfirmationBand(band: MeasurementBand): boolean {
  return band === 'below-usual' || band === 'above-usual'
}

/**
 * One segment of the inch fraction strip.
 *
 * The numerator and denominator are already reduced — `2/8` is offered as `1/4` — because that is
 * how a tailor reads a tape and how the value is printed on the measurement sheet.
 */
export interface FractionOption {
  /** Reduced numerator; 0 for the whole-inch option. */
  readonly numerator: number
  /** Reduced denominator; 1 for the whole-inch option. */
  readonly denominator: number
  /** The value in inches, used to compare against the current value. */
  readonly inches: number
  /** What the segment shows and what assistive technology reads: `0`, `1/8`, `1/2`. */
  readonly text: string
}

function greatestCommonDivisor(a: number, b: number): number {
  return b === 0 ? a : greatestCommonDivisor(b, a % b)
}

/**
 * The segments offered for a field's fraction step: eighths for general lengths, sixteenths for the
 * shaping and neckline fields where a quarter-inch error changes the fit.
 *
 * A segmented control and not a slider, by rule (docs/nfr/accessibility-localisation.md section 5
 * rule 4): a slider cannot be operated accurately with a finger guard on and cannot be typed.
 */
export function fractionOptions(step: InchFractionStep): readonly FractionOption[] {
  const options: FractionOption[] = []
  for (let index = 0; index < step; index += 1) {
    if (index === 0) {
      options.push({ numerator: 0, denominator: 1, inches: 0, text: '0' })
      continue
    }
    const divisor = greatestCommonDivisor(index, step)
    const numerator = index / divisor
    const denominator = step / divisor
    options.push({
      numerator,
      denominator,
      inches: numerator / denominator,
      text: `${String(numerator)}/${String(denominator)}`,
    })
  }
  return options
}

/** The parts the fraction control holds while it is being edited. */
export interface FractionParts {
  readonly whole: number
  readonly numerator: number
  readonly denominator: number
}

/** Splits canonical millimetres into the whole and fraction parts at the field's step. */
export function millimetresToFractionParts(
  millimetres: number,
  step: InchFractionStep,
): FractionParts {
  const fraction = millimetresToInchFraction(millimetres, step)
  return {
    whole: fraction.negative ? -fraction.whole : fraction.whole,
    numerator: fraction.numerator,
    denominator: fraction.denominator,
  }
}

/** Reassembles the parts into canonical millimetres, rounded to the stored two decimal places. */
export function fractionPartsToMillimetres(parts: FractionParts): number {
  const inches = parts.whole + (parts.denominator === 0 ? 0 : parts.numerator / parts.denominator)
  return Math.round(inches * MILLIMETRES_PER_INCH * 100) / 100
}

/** Whether a step value came from a template that this control can render. */
export function isInchFractionStep(step: number): step is InchFractionStep {
  return (INCH_FRACTION_STEPS as readonly number[]).includes(step)
}

/**
 * The sample value the "enter a number" message shows.
 *
 * One canonical value formatted in whichever unit is on screen, so the example is always in the
 * unit the person is typing in: `15 3/8 in` for inches and `39.1 cm` for centimetres. A hard-coded
 * English example would be wrong in half the cases and untranslatable in the other half.
 */
export const MEASUREMENT_EXAMPLE_MILLIMETRES = 390.5

/**
 * Clamps a value to the hard bounds. Used by the stepper's buttons, which must not be able to walk
 * a value outside the range the template allows; typing is validated instead, so that a person who
 * types a rejected value sees why rather than watching the number change under them.
 */
export function clampToBounds(value: number, bounds: MeasurementBounds): number {
  const { minimumMillimetres, maximumMillimetres } = bounds
  const lowered = maximumMillimetres === undefined ? value : Math.min(value, maximumMillimetres)
  return minimumMillimetres === undefined ? lowered : Math.max(lowered, minimumMillimetres)
}
