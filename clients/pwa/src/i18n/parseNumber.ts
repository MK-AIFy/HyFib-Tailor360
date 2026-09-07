import { centimetresToMillimetres, inchesToMillimetres, roundMillimetres } from './units'
import type { DisplayUnit } from './units'

/**
 * Reading a number a member of staff typed.
 *
 * docs/prd/measurement-templates.md section 2 requires centimetre fields to accept **both `,` and
 * `.`** as the decimal separator, and the #50 blueprint repeats it for the whole system. The reason
 * is not theoretical: an Android keypad in `inputmode="decimal"` offers whichever separator the
 * device locale prefers, staff type what the key gives them, and a rejected `36,5` at the counter
 * is a customer waiting while somebody works out why the form will not accept a waist measurement.
 *
 * Grouping separators are accepted too, in both the Indian form (`12,34,567.89`) and the European
 * one (`12.34.567,89`), because a value is as often pasted as typed.
 *
 * The one genuine ambiguity is a lone comma followed by exactly three digits: `1,234` is a
 * thousands group in Indian usage and one and a bit in European. It is read as **grouping**, which
 * is the right answer for every field in this product — a measurement is never given to three
 * decimal places, and an amount is given to two.
 *
 * Failure is `null`, never an exception and never `NaN`: an unparseable value is a validation error
 * the field shows in words, not a crash.
 */

/** Characters an amount may arrive wrapped in when it is pasted from a document or a message. */
const ADORNMENTS = /[\s₹]/g

/**
 * Normalises typed text to a plain decimal string — `-1234.56` — or null if it is not a number.
 *
 * Returns a **string**, so an amount can be sent to the API exactly as the person entered it.
 * `decimal` values never become JavaScript numbers on the way to the server (COD-01: the browser
 * never does decimal arithmetic).
 */
export function parseDecimalString(input: string): string | null {
  const cleaned = input.replace(ADORNMENTS, '')
  if (cleaned === '') {
    return null
  }

  const signed = /^[+-]/.test(cleaned)
  const sign = cleaned.startsWith('-') ? '-' : ''
  const body = signed ? cleaned.slice(1) : cleaned
  if (body === '' || !/^[0-9.,]+$/.test(body)) {
    return null
  }

  const commas = (body.match(/,/g) ?? []).length
  const dots = (body.match(/\./g) ?? []).length

  let decimalSeparator: '.' | ',' | null = null
  if (commas > 0 && dots > 0) {
    // Whichever comes last is the decimal separator; the other one groups.
    decimalSeparator = body.lastIndexOf(',') > body.lastIndexOf('.') ? ',' : '.'
  } else if (dots === 1) {
    // A lone full stop is a decimal point in en-IN, whatever follows it.
    decimalSeparator = '.'
  } else if (commas === 1) {
    // A lone comma groups when exactly three digits follow it, and is a decimal point otherwise.
    decimalSeparator = /,\d{3}$/.test(body) ? null : ','
  }

  const [integerPart, fractionPart] =
    decimalSeparator === null
      ? [body, '']
      : [
          body.slice(0, body.lastIndexOf(decimalSeparator)),
          body.slice(body.lastIndexOf(decimalSeparator) + 1),
        ]

  // The decimal separator may appear once. Seeing it again among the groups means the two
  // separators are interleaved — `1.2.3,4.5` — which is not a number in any convention, and reading
  // it leniently would give a silently wrong answer on a form that takes money.
  if (decimalSeparator !== null && integerPart.includes(decimalSeparator)) {
    return null
  }

  const integerDigits = integerPart.replace(/[.,]/g, '')
  if (/[.,]/.test(fractionPart)) {
    return null
  }
  if (integerDigits === '' && fractionPart === '') {
    return null
  }
  if (!/^\d*$/.test(integerDigits) || !/^\d*$/.test(fractionPart)) {
    return null
  }

  const whole = integerDigits === '' ? '0' : integerDigits
  return fractionPart === '' ? `${sign}${whole}` : `${sign}${whole}.${fractionPart}`
}

/** The same, as a number. Use it for measurements and counts; never for money. */
export function parseDecimal(input: string): number | null {
  const text = parseDecimalString(input)
  if (text === null) {
    return null
  }
  const value = Number(text)
  return Number.isFinite(value) ? value : null
}

/**
 * Reads an inch value in any of the forms staff actually type.
 *
 * `36 1/2`, `36-1/2`, `36 1/2 in`, `1/2`, `36`, `36.5` and `36,5` all mean the same thing. Returns
 * decimal inches, or null.
 */
export function parseInchInput(input: string): number | null {
  const cleaned = input
    .replace(/\bin\b|"|″/gi, '')
    .trim()
    .replace(/\s+/g, ' ')

  if (cleaned === '') {
    return null
  }

  const negative = cleaned.startsWith('-')
  const body = negative ? cleaned.slice(1).trim() : cleaned

  const fractionMatch = /^(?:(\d+)\s*[- ]\s*)?(\d+)\s*\/\s*(\d+)$/.exec(body)
  if (fractionMatch !== null) {
    const [, wholeText, numeratorText, denominatorText] = fractionMatch
    const denominator = Number(denominatorText)
    if (denominator === 0) {
      return null
    }
    const inches = Number(wholeText ?? '0') + Number(numeratorText) / denominator
    return negative ? -inches : inches
  }

  const decimal = parseDecimal(body)
  if (decimal === null) {
    return null
  }
  return negative ? -decimal : decimal
}

/**
 * Reads typed text in the reader's display unit and returns canonical millimetres — the only unit
 * anything is ever stored in (docs/prd/measurement-templates.md section 2).
 */
export function parseMeasurementToMillimetres(input: string, unit: DisplayUnit): number | null {
  if (unit === 'in') {
    const inches = parseInchInput(input)
    return inches === null ? null : inchesToMillimetres(inches)
  }

  const value = parseDecimal(input)
  if (value === null) {
    return null
  }
  return unit === 'cm' ? centimetresToMillimetres(value) : roundMillimetres(value)
}
