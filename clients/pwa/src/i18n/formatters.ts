import { DEFAULT_LOCALE } from './locales'
import type { SupportedLocale } from './locales'
import { DISPLAY_UNITS, millimetresToCentimetres, millimetresToInchFraction } from './units'
import type { CentimetreDecimals, DisplayUnit, InchFractionStep } from './units'

/**
 * The one place a number, an amount, a date, a time or a measurement becomes text.
 *
 * docs/architecture/conventions.md section 1.4 and docs/nfr/accessibility-localisation.md section 12
 * both say the same thing in different words: no component formats a value by hand, no component
 * calls `toLocaleString`, and no message string concatenates a formatted value — values are ICU
 * arguments inside a message so that word order can differ in Tamil.
 *
 * What that buys, concretely:
 *
 *  - **Indian digit grouping everywhere.** `₹12,34,567.89`, lakh and crore, never the three-digit
 *    western grouping, in both `en-IN` and `ta-IN`.
 *  - **Latin digits everywhere**, requested explicitly through `-u-nu-latn` rather than left to a
 *    default, because Tamil digits are not used in daily commerce (AL-06).
 *  - **One date form.** `dd-MM-yyyy` in both locales, so a date on a screen is never ambiguous
 *    between two members of staff; and a 12-hour clock, never a bare 24-hour one.
 *  - **Branch-local time.** Everything is rendered in the branch's IANA timezone, `Asia/Kolkata` by
 *    default, never a hard-coded `+05:30` offset (plan D11, conventions section 2.2).
 *
 * Money arrives from the API as an unformatted decimal — a string, per COD-01, so the browser never
 * does decimal arithmetic. This module formats it; it never adds, multiplies or re-rounds it. Every
 * amount is already rounded to paise by the server that produced it.
 */

/** The branch timezone default. A branch outside `Asia/Kolkata` passes its own IANA identifier. */
export const DEFAULT_TIME_ZONE = 'Asia/Kolkata'

/** The currency of the single Indian legal entity this product serves (assumption A1). */
export const CURRENCY_CODE = 'INR'

/** Anything that can name an instant. Strings are parsed as ISO 8601, which is what the API sends. */
export type DateInput = Date | number | string

function toDate(value: DateInput): Date {
  const date = value instanceof Date ? value : new Date(value)
  if (Number.isNaN(date.getTime())) {
    throw new Error(`Not a valid date: ${JSON.stringify(value)}`)
  }
  return date
}

function toNumber(value: number | string): number {
  const parsed = typeof value === 'number' ? value : Number(value)
  if (!Number.isFinite(parsed)) {
    throw new Error(`Not a finite number: ${JSON.stringify(value)}`)
  }
  return parsed
}

/**
 * Replaces the narrow and ordinary no-break spaces ICU inserts with a plain space.
 *
 * ICU puts U+202F between a time and its day-period marker, and the character is missing from some
 * of the fonts on the shop's Android devices, where it renders as a box. The layout job it was doing
 * belongs to CSS: a component that must not wrap a value says so with `white-space: nowrap`.
 */
function normaliseSpaces(text: string): string {
  return text.replace(/[\u202F\u00A0]/g, ' ')
}

/** Requests Latin digits explicitly rather than relying on the locale's default numbering system. */
function withLatinDigits(locale: SupportedLocale): string {
  return `${locale}-u-nu-latn`
}

export interface FormatterOptions {
  /** The branch's IANA timezone. Defaults to `Asia/Kolkata`. */
  readonly timeZone?: string
}

/** A relative time, with the absolute value it stands for. */
export interface RelativeTime {
  /** "in 2 days", "3 hours ago". */
  readonly relative: string
  /** The same instant in full, because a due cue is never relative alone. */
  readonly absolute: string
}

export interface MeasurementFormatOptions {
  /** The display unit the reader has chosen. */
  readonly unit: DisplayUnit
  /** The inch fraction step from the template field. Ignored for centimetres. */
  readonly step?: InchFractionStep
  /**
   * The decimal places from the template field. Ignored for inches.
   *
   * The server permits nought, one or two, and the field declares which. One was fixed here until a
   * two-decimal field was found to render at the wrong precision (#100); it stays the default
   * because it is what the templates document specifies for a field that does not say otherwise.
   */
  readonly decimals?: CentimetreDecimals
  /**
   * The unit word to print instead of the symbol. The symbol is language-neutral; the word is
   * glossary-owned and comes from the message catalogue.
   */
  readonly unitLabel?: string
}

export interface Formatters {
  readonly locale: SupportedLocale
  readonly timeZone: string

  /** A plain number with Indian grouping. */
  formatNumber(value: number | string, fractionDigits?: number): string
  /** An amount with the rupee symbol, for screens and documents. */
  formatMoney(value: number | string): string
  /** An amount with the ISO code, for exports and integration payloads. */
  formatMoneyWithCode(value: number | string): string
  /** A percentage given as a percentage, not a fraction: `formatPercent(2.5)` is `2.5%`. */
  formatPercent(percentage: number | string): string
  /** A stock or line quantity in its base unit, to four decimal places at most. */
  formatQuantity(value: number | string, unitSymbol: string): string

  /** `04-09-2026`. Identical in both locales, on purpose. */
  formatShortDate(value: DateInput): string
  /** `04 September 2026`, with Tamil month names in `ta-IN`. */
  formatLongDate(value: DateInput): string
  /** `04:30 PM`. Never a bare 24-hour clock on a shop-floor screen. */
  formatTime(value: DateInput): string
  /** `04-09-2026 04:30 PM`. */
  formatDateTime(value: DateInput): string
  /** A relative cue and the absolute value it stands for. */
  formatRelativeTime(value: DateInput, now?: DateInput): RelativeTime
  /** `2026-27`, the April-to-March financial year the document sequences are keyed on. */
  formatFinancialYear(value: DateInput): string

  /** `36 1/2 in`, `36.8 cm`, `368.3 mm` — from canonical millimetres, always. */
  formatMeasurement(millimetres: number, options: MeasurementFormatOptions): string
}

const RELATIVE_UNITS: readonly (readonly [Intl.RelativeTimeFormatUnit, number])[] = [
  ['year', 365 * 24 * 60 * 60 * 1000],
  ['month', 30 * 24 * 60 * 60 * 1000],
  ['week', 7 * 24 * 60 * 60 * 1000],
  ['day', 24 * 60 * 60 * 1000],
  ['hour', 60 * 60 * 1000],
  ['minute', 60 * 1000],
]

/**
 * Builds the formatter set for one locale and one branch timezone.
 *
 * Every `Intl` object is constructed once here rather than per call: constructing them is the
 * expensive part, and a queue screen formats a due date for every row.
 */
export function createFormatters(
  locale: SupportedLocale,
  options: FormatterOptions = {},
): Formatters {
  const timeZone = options.timeZone ?? DEFAULT_TIME_ZONE
  const tag = withLatinDigits(locale)

  const decimal = new Intl.NumberFormat(tag, { maximumFractionDigits: 4 })
  const money = new Intl.NumberFormat(tag, {
    style: 'currency',
    currency: CURRENCY_CODE,
    currencyDisplay: 'symbol',
    minimumFractionDigits: 2,
    maximumFractionDigits: 2,
  })
  const moneyWithCode = new Intl.NumberFormat(tag, {
    style: 'currency',
    currency: CURRENCY_CODE,
    currencyDisplay: 'code',
    minimumFractionDigits: 2,
    maximumFractionDigits: 2,
  })
  const percent = new Intl.NumberFormat(tag, {
    style: 'percent',
    minimumFractionDigits: 0,
    // Tax rates carry three decimal places (conventions section 1.1).
    maximumFractionDigits: 3,
  })
  const quantity = new Intl.NumberFormat(tag, { maximumFractionDigits: 4 })
  // One per permitted precision rather than one fixed at a single decimal place: a formatter is the
  // expensive thing to build, and there are exactly three of them.
  const centimetresAt = [0, 1, 2].map(
    (places) =>
      new Intl.NumberFormat(tag, {
        minimumFractionDigits: places,
        maximumFractionDigits: places,
      }),
  )
  const millimetreFormat = new Intl.NumberFormat(tag, { maximumFractionDigits: 2 })

  const shortDateParts = new Intl.DateTimeFormat(tag, {
    timeZone,
    day: '2-digit',
    month: '2-digit',
    year: 'numeric',
  })
  const longDate = new Intl.DateTimeFormat(tag, {
    timeZone,
    day: '2-digit',
    month: 'long',
    year: 'numeric',
  })
  const timeParts = new Intl.DateTimeFormat(tag, {
    timeZone,
    hour: '2-digit',
    minute: '2-digit',
    hour12: true,
  })
  const yearMonthParts = new Intl.DateTimeFormat('en-US-u-nu-latn', {
    timeZone,
    year: 'numeric',
    month: '2-digit',
  })
  const relative = new Intl.RelativeTimeFormat(tag, { numeric: 'auto' })

  /** `dd-MM-yyyy` built from parts, because the locale's own separator is a slash. */
  function formatShortDate(value: DateInput): string {
    const parts = shortDateParts.formatToParts(toDate(value))
    const find = (type: Intl.DateTimeFormatPartTypes): string =>
      parts.find((part) => part.type === type)?.value ?? ''
    return `${find('day')}-${find('month')}-${find('year')}`
  }

  /**
   * A 12-hour clock. ICU renders the English day period in lower case; the shop's screens and its
   * printed documents have always shown `PM`, so English is upper-cased and Tamil is left exactly
   * as the locale data supplies it.
   */
  function formatTime(value: DateInput): string {
    const parts = timeParts.formatToParts(toDate(value))
    const rendered = parts
      .map((part) =>
        part.type === 'dayPeriod' && locale.startsWith('en')
          ? part.value.toUpperCase()
          : part.value,
      )
      .join('')
    return normaliseSpaces(rendered)
  }

  function readYearAndMonth(value: DateInput): { year: number; month: number } {
    const parts = yearMonthParts.formatToParts(toDate(value))
    const find = (type: Intl.DateTimeFormatPartTypes): number =>
      Number.parseInt(parts.find((part) => part.type === type)?.value ?? '0', 10)
    return { year: find('year'), month: find('month') }
  }

  return {
    locale,
    timeZone,

    formatNumber(value, fractionDigits) {
      const number = toNumber(value)
      if (fractionDigits === undefined) {
        return normaliseSpaces(decimal.format(number))
      }
      return normaliseSpaces(
        new Intl.NumberFormat(tag, {
          minimumFractionDigits: fractionDigits,
          maximumFractionDigits: fractionDigits,
        }).format(number),
      )
    },

    formatMoney(value) {
      return normaliseSpaces(money.format(toNumber(value)))
    },

    formatMoneyWithCode(value) {
      return normaliseSpaces(moneyWithCode.format(toNumber(value)))
    },

    formatPercent(percentage) {
      // The value is a percentage (conventions section 1.1: "Percentage, not a fraction"), and the
      // percent style expects a fraction, so it is divided here rather than in every caller.
      return normaliseSpaces(percent.format(toNumber(percentage) / 100))
    },

    formatQuantity(value, unitSymbol) {
      return `${normaliseSpaces(quantity.format(toNumber(value)))} ${unitSymbol}`
    },

    formatShortDate,
    formatTime,

    formatLongDate(value) {
      return normaliseSpaces(longDate.format(toDate(value)))
    },

    formatDateTime(value) {
      return `${formatShortDate(value)} ${formatTime(value)}`
    },

    formatRelativeTime(value, now) {
      const target = toDate(value)
      const reference = now === undefined ? new Date() : toDate(now)
      const difference = target.getTime() - reference.getTime()

      const match = RELATIVE_UNITS.find(([, size]) => Math.abs(difference) >= size)
      const unit: Intl.RelativeTimeFormatUnit = match?.[0] ?? 'minute'
      const size = match?.[1] ?? 60 * 1000

      return {
        relative: normaliseSpaces(relative.format(Math.round(difference / size), unit)),
        absolute: `${formatShortDate(target)} ${formatTime(target)}`,
      }
    },

    formatFinancialYear(value) {
      // 1 April to 31 March (conventions section 2.3).
      const { year, month } = readYearAndMonth(value)
      const start = month >= 4 ? year : year - 1
      return `${String(start)}-${String(start + 1).slice(2)}`
    },

    formatMeasurement(millimetres, measurementOptions) {
      const { unit, step, decimals, unitLabel } = measurementOptions
      if (!DISPLAY_UNITS.includes(unit)) {
        throw new Error(`Not a display unit: ${JSON.stringify(unit)}`)
      }
      const symbol = unitLabel ?? unit

      if (unit === 'in') {
        const fraction = millimetresToInchFraction(millimetres, step ?? 8)
        const sign = fraction.negative ? '-' : ''
        if (fraction.numerator === 0) {
          return `${sign}${normaliseSpaces(decimal.format(fraction.whole))} ${symbol}`
        }
        const parts =
          fraction.whole === 0
            ? `${String(fraction.numerator)}/${String(fraction.denominator)}`
            : `${normaliseSpaces(decimal.format(fraction.whole))} ${String(fraction.numerator)}/${String(fraction.denominator)}`
        return `${sign}${parts} ${symbol}`
      }

      if (unit === 'cm') {
        const places = decimals ?? 1
        const formatter = centimetresAt[places] ?? centimetresAt[1]

        if (formatter === undefined) {
          throw new Error(`No centimetre formatter for ${String(places)} decimal places.`)
        }

        return `${normaliseSpaces(formatter.format(millimetresToCentimetres(millimetres, places)))} ${symbol}`
      }

      return `${normaliseSpaces(millimetreFormat.format(millimetres))} ${symbol}`
    },
  }
}

const cache = new Map<string, Formatters>()

/**
 * The cached formatter set for a locale and branch timezone.
 *
 * Components use this rather than constructing their own: a queue of two hundred rows would
 * otherwise build two hundred `Intl.NumberFormat` objects, which is the single most expensive thing
 * a list can do on the reference device.
 */
export function getFormatters(
  locale: SupportedLocale = DEFAULT_LOCALE,
  timeZone: string = DEFAULT_TIME_ZONE,
): Formatters {
  const key = `${locale}|${timeZone}`
  const existing = cache.get(key)
  if (existing !== undefined) {
    return existing
  }
  const created = createFormatters(locale, { timeZone })
  cache.set(key, created)
  return created
}
