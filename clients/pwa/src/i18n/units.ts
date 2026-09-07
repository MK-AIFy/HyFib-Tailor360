/**
 * Measurement unit conversions.
 *
 * Pure arithmetic, no locale and no `Intl`: these are the rules of
 * docs/prd/measurement-templates.md section 2, which fixes millimetres as the canonical unit,
 * `1 in = 25.4 mm` and `1 cm = 10 mm` as exact constants, and a per-field precision — a 1/8 inch
 * step for general lengths, 1/16 for the shaping and neckline fields where a quarter-inch error
 * changes the fit, and one decimal place for centimetres.
 *
 * The guarantee the templates document asks for, and that units.test.ts holds this module to, is
 * `toDisplay(fromDisplay(x)) === x` at the field's precision in both display units.
 */

/** Exact, by definition. Never 25.4mm approximated, and never a locale-dependent parse. */
export const MILLIMETRES_PER_INCH = 25.4

/** Exact. */
export const MILLIMETRES_PER_CENTIMETRE = 10

/** The display units a measurement field may use. */
export const DISPLAY_UNITS = ['in', 'cm', 'mm'] as const

export type DisplayUnit = (typeof DISPLAY_UNITS)[number]

/**
 * The inch fraction steps a template may specify: eighths for general lengths, sixteenths for
 * shaping and neckline fields. A slider is never used for a measurement — `FractionInput` offers
 * exactly these divisions as segmented controls, which is what makes the value enterable with a
 * finger guard on.
 */
export const INCH_FRACTION_STEPS = [8, 16] as const

export type InchFractionStep = (typeof INCH_FRACTION_STEPS)[number]

/**
 * Canonical storage keeps two decimal places (`numeric(8,2)`), which is what lets a 1/16 inch step
 * — 1.5875 mm — survive a round trip.
 */
export function roundMillimetres(millimetres: number): number {
  return Math.round(millimetres * 100) / 100
}

/** An inch value split into the parts `FractionInput` shows and reads back. */
export interface InchFraction {
  /** True when the whole value is negative, as an ease allowance may be. */
  readonly negative: boolean
  /** Whole inches, always non-negative; the sign lives on `negative`. */
  readonly whole: number
  /** Fraction numerator after reduction; 0 when the value is a whole number of inches. */
  readonly numerator: number
  /** Fraction denominator after reduction; 1 when the numerator is 0. */
  readonly denominator: number
}

function greatestCommonDivisor(a: number, b: number): number {
  return b === 0 ? a : greatestCommonDivisor(b, a % b)
}

/**
 * Converts canonical millimetres to the nearest whole-plus-fraction inch value at the field's step.
 *
 * Rounds half away from zero, matching the money rule in docs/architecture/conventions.md section
 * 1.2, so a measurement and an amount never disagree about which way a halfway value goes.
 */
export function millimetresToInchFraction(
  millimetres: number,
  step: InchFractionStep = 8,
): InchFraction {
  const negative = millimetres < 0
  const totalSteps = Math.round((Math.abs(millimetres) / MILLIMETRES_PER_INCH) * step)
  const whole = Math.floor(totalSteps / step)
  const remainder = totalSteps % step

  if (remainder === 0) {
    return { negative, whole, numerator: 0, denominator: 1 }
  }

  const divisor = greatestCommonDivisor(remainder, step)
  return { negative, whole, numerator: remainder / divisor, denominator: step / divisor }
}

/** Converts a whole-plus-fraction inch value back to canonical millimetres. */
export function inchFractionToMillimetres(fraction: {
  readonly negative?: boolean
  readonly whole: number
  readonly numerator?: number
  readonly denominator?: number
}): number {
  const numerator = fraction.numerator ?? 0
  const denominator = fraction.denominator ?? 1
  if (denominator === 0) {
    throw new Error('An inch fraction cannot have a denominator of zero.')
  }
  const inches = fraction.whole + numerator / denominator
  const millimetres = roundMillimetres(inches * MILLIMETRES_PER_INCH)
  return fraction.negative === true ? -millimetres : millimetres
}

/**
 * Converts canonical millimetres to centimetres at the one decimal place templates specify.
 *
 * Rounds the millimetre value first and divides afterwards: dividing and then rounding to one
 * decimal place in binary floating point produces values like 36.800000000000004, which then print
 * with a spurious digit on a measurement sheet.
 */
export function millimetresToCentimetres(millimetres: number): number {
  return Math.round(millimetres) / MILLIMETRES_PER_CENTIMETRE
}

/** Converts centimetres to canonical millimetres. */
export function centimetresToMillimetres(centimetres: number): number {
  return roundMillimetres(centimetres * MILLIMETRES_PER_CENTIMETRE)
}

/** Converts decimal inches to canonical millimetres, for a value that is not entered as a fraction. */
export function inchesToMillimetres(inches: number): number {
  return roundMillimetres(inches * MILLIMETRES_PER_INCH)
}
