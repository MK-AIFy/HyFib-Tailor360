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
 * The inch fraction steps a template may specify.
 *
 * The server permits halves, quarters, eighths and sixteenths (`FieldPrecision.PermittedInchFractions`):
 * eighths for general lengths, sixteenths for the shaping and neckline fields where a quarter-inch
 * error changes the fit, and the coarser two for fields a tailor reads off in halves. A slider is
 * never used for a measurement — `FractionInput` offers exactly these divisions as segmented
 * controls, which is what makes the value enterable with a finger guard on.
 */
export const INCH_FRACTION_STEPS = [2, 4, 8, 16] as const

export type InchFractionStep = (typeof INCH_FRACTION_STEPS)[number]

/** The decimal places a centimetre field may declare. The server permits nought to two. */
export const CENTIMETRE_DECIMALS = [0, 1, 2] as const

export type CentimetreDecimals = (typeof CENTIMETRE_DECIMALS)[number]

/* The arithmetic --------------------------------------------------------------------------------
 *
 * Every conversion below goes through these three functions, and none of them multiplies two
 * inexact doubles. That is the whole point of them.
 *
 * `UnitConversion.cs` does this arithmetic in `decimal`, whose remarks say why: "double would
 * introduce an error in a value that a tailor then cuts fabric against". JavaScript has no decimal,
 * and the obvious transliteration is wrong in a way that is invisible until somebody sweeps it —
 * `0.375 * 25.4 * 100` is `952.49999999999988631316`, just under the midpoint, so `Math.round` sends
 * 3/8 in down to 9.52 mm where the server stores 9.53. That disagreement held for 146 of the 1,804
 * values a tailor can type between 0 and 60 inches at the permitted denominators.
 *
 * So the conversion factors are held as integers — 2540 hundredths of a millimetre to the inch
 * rather than 25.4 millimetres — and the value being converted is decomposed into its own decimal
 * digits first. Integers multiply exactly, and the division that follows carries its remainder, so
 * the midpoint is a comparison rather than a floating-point accident.
 */

/** Hundredths of a millimetre in one inch. Exact, and an integer so that it multiplies exactly. */
const HUNDREDTHS_PER_INCH = 2540

/** Hundredths of a millimetre in one centimetre. */
const HUNDREDTHS_PER_CENTIMETRE = 1000

/** Hundredths in one millimetre, which is also the storage precision. */
const HUNDREDTHS_PER_MILLIMETRE = 100

/**
 * A number as the exact decimal it reads as: `value === units / 10 ** scale`.
 *
 * `toString` gives the shortest representation that round-trips, which is the number a person would
 * write, and that is the value the conversion should honour.
 */
function scaledDigits(value: number): { readonly units: number; readonly scale: number } {
  const text = value.toString()

  if (text.includes('e') || text.includes('E')) {
    // No measurement reaches exponent notation. Treating such a value as whole is honest; guessing
    // at a mantissa would put a wrong number on a cutting table.
    return { units: Math.trunc(value), scale: 0 }
  }

  const point = text.indexOf('.')

  return point === -1
    ? { units: Number(text), scale: 0 }
    : { units: Number(text.replace('.', '')), scale: text.length - point - 1 }
}

/**
 * `numerator / divisor`, rounded half away from zero, on non-negative integers.
 *
 * Half away from zero rather than `Math.round`, which is half toward positive infinity: the two
 * agree on every positive midpoint and disagree on every negative one, and an ease allowance may be
 * negative. It is also the rule `UnitConversion` and the money conventions both state, so a
 * measurement and an amount never disagree about which way a halfway value goes.
 */
function divideHalfAwayFromZero(numerator: number, divisor: number): number {
  const quotient = Math.floor(numerator / divisor)
  const remainder = numerator - quotient * divisor

  return 2 * remainder >= divisor ? quotient + 1 : quotient
}

/** `value * factor`, rounded half away from zero, without multiplying two inexact doubles. */
function scaleExactly(value: number, factor: number): number {
  const { units, scale } = scaledDigits(value)
  const magnitude = divideHalfAwayFromZero(Math.abs(units) * factor, 10 ** scale)

  return units < 0 ? -magnitude : magnitude
}

/**
 * Canonical storage keeps two decimal places (`numeric(8,2)`), which is what lets a 1/16 inch step
 * — 1.5875 mm — survive a round trip.
 */
export function roundMillimetres(millimetres: number): number {
  return scaleExactly(millimetres, HUNDREDTHS_PER_MILLIMETRE) / HUNDREDTHS_PER_MILLIMETRE
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
  const hundredths = scaleExactly(millimetres, HUNDREDTHS_PER_MILLIMETRE)
  const negative = hundredths < 0
  // `RoundToFraction(mm / 25.4, step)` from UnitConversion, as integers: the value in hundredths
  // times the step, over the hundredths in an inch.
  const totalSteps = divideHalfAwayFromZero(Math.abs(hundredths) * step, HUNDREDTHS_PER_INCH)
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

  // Whole and fraction are combined into a count of steps before anything is divided, so the only
  // division is the exact one below and 14 1/2 in reaches storage as 368.30 mm rather than near it.
  const steps = fraction.whole * denominator + numerator
  const millimetres =
    divideHalfAwayFromZero(steps * HUNDREDTHS_PER_INCH, denominator) / HUNDREDTHS_PER_MILLIMETRE

  return fraction.negative === true ? -millimetres : millimetres
}

/**
 * Converts canonical millimetres to centimetres at the field's own precision.
 *
 * The decimal places are the field's, not this module's: the server permits nought, one or two
 * (`FieldPrecision.CentimetreDecimals`), and one was hard-coded here until a two-decimal field —
 * perfectly legal — was found to render wrongly. One remains the default because it is what the
 * templates document specifies for a field that does not say otherwise.
 */
export function millimetresToCentimetres(
  millimetres: number,
  decimals: CentimetreDecimals = 1,
): number {
  const hundredths = scaleExactly(millimetres, HUNDREDTHS_PER_MILLIMETRE)
  const places = 10 ** decimals
  const magnitude = divideHalfAwayFromZero(Math.abs(hundredths) * places, HUNDREDTHS_PER_CENTIMETRE)

  return (hundredths < 0 ? -magnitude : magnitude) / places
}

/** Converts centimetres to canonical millimetres. */
export function centimetresToMillimetres(centimetres: number): number {
  return scaleExactly(centimetres, HUNDREDTHS_PER_CENTIMETRE) / HUNDREDTHS_PER_MILLIMETRE
}

/** Converts decimal inches to canonical millimetres, for a value that is not entered as a fraction. */
export function inchesToMillimetres(inches: number): number {
  return scaleExactly(inches, HUNDREDTHS_PER_INCH) / HUNDREDTHS_PER_MILLIMETRE
}
