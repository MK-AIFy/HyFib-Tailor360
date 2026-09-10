import { describe, expect, it } from 'vitest'
import { createFormatters, getFormatters } from './formatters'
import type { SupportedLocale } from './locales'

/** 04 September 2026, 16:30 in Asia/Kolkata. */
const AFTERNOON = new Date(Date.UTC(2026, 8, 4, 11, 0, 0))

const LOCALES: readonly SupportedLocale[] = ['en-IN', 'ta-IN']

describe.each(LOCALES)('%s', (locale) => {
  const formatters = createFormatters(locale)

  describe('money and numbers', () => {
    it('groups in lakh and crore, never in thousands', () => {
      expect(formatters.formatNumber(1234567.89)).toBe('12,34,567.89')
    })

    it('uses Latin digits, because Tamil digits are not used in daily commerce', () => {
      expect(formatters.formatNumber(1234567.89)).toMatch(/^[0-9,.]+$/)
    })

    it('renders an amount with the rupee symbol and two decimal places', () => {
      expect(formatters.formatMoney('1234567.89')).toBe('₹12,34,567.89')
    })

    it('renders a negative amount with a leading minus, never a parenthesis', () => {
      expect(formatters.formatMoney('-1200')).toBe('-₹1,200.00')
    })

    it('renders the ISO code for exports and integration payloads', () => {
      expect(formatters.formatMoneyWithCode('1234567.89')).toBe('INR 12,34,567.89')
    })

    it('takes a percentage as a percentage, not a fraction', () => {
      expect(formatters.formatPercent(2.5)).toBe('2.5%')
      expect(formatters.formatPercent(12)).toBe('12%')
    })

    it('renders a stock quantity in its base unit to four decimal places', () => {
      expect(formatters.formatQuantity('2.75', 'm')).toBe('2.75 m')
    })

    it('refuses a value that is not a number rather than printing NaN', () => {
      expect(() => formatters.formatMoney('not a number')).toThrow(/finite/)
    })
  })

  describe('dates and times', () => {
    it('writes a short date as dd-MM-yyyy in both locales', () => {
      expect(formatters.formatShortDate(AFTERNOON)).toBe('04-09-2026')
    })

    it('reads the branch timezone, not the runner timezone', () => {
      // 20:00 UTC on the 4th is already the 5th in Asia/Kolkata.
      expect(formatters.formatShortDate(new Date(Date.UTC(2026, 8, 4, 20, 0, 0)))).toBe(
        '05-09-2026',
      )
    })

    it('writes a 12-hour clock with a day-period marker', () => {
      expect(formatters.formatTime(AFTERNOON)).toMatch(/^04:30 \S+$/)
    })

    it('leaves no narrow no-break space for a shop device font to fail on', () => {
      expect(formatters.formatTime(AFTERNOON)).not.toMatch(/[\u202F\u00A0]/)
    })

    it('combines the two for a date and time', () => {
      expect(formatters.formatDateTime(AFTERNOON)).toBe(
        `04-09-2026 ${formatters.formatTime(AFTERNOON)}`,
      )
    })

    it('always offers the absolute value beside the relative one', () => {
      const inTwoDays = new Date(AFTERNOON.getTime() + 2 * 24 * 60 * 60 * 1000)
      const result = formatters.formatRelativeTime(inTwoDays, AFTERNOON)

      expect(result.relative).not.toBe('')
      expect(result.absolute).toContain('06-09-2026')
    })

    it('names the April-to-March financial year', () => {
      expect(formatters.formatFinancialYear(AFTERNOON)).toBe('2026-27')
      // 31 March 2026 belongs to 2025-26; 1 April 2026 starts 2026-27.
      expect(formatters.formatFinancialYear('2026-03-31T10:00:00Z')).toBe('2025-26')
      expect(formatters.formatFinancialYear('2026-04-01T10:00:00Z')).toBe('2026-27')
    })

    it('refuses an unreadable date rather than printing Invalid Date', () => {
      expect(() => formatters.formatShortDate('the day before yesterday')).toThrow(/valid date/)
    })
  })

  describe('measurements', () => {
    it('renders inches as a whole number and a reduced fraction', () => {
      // 14 1/2 in is 368.30 mm — the worked example in measurement-templates.md section 2.
      expect(formatters.formatMeasurement(368.3, { unit: 'in', step: 8 })).toBe('14 1/2 in')
    })

    it('drops the fraction when the value is a whole number of inches', () => {
      expect(formatters.formatMeasurement(914.4, { unit: 'in', step: 8 })).toBe('36 in')
    })

    it('renders a value below one inch as a bare fraction', () => {
      expect(formatters.formatMeasurement(12.7, { unit: 'in', step: 8 })).toBe('1/2 in')
    })

    it('honours the finer sixteenth step of the shaping and neckline fields', () => {
      expect(formatters.formatMeasurement(371.48, { unit: 'in', step: 16 })).toBe('14 5/8 in')
    })

    it('renders a coarser inch step, which a field may declare', () => {
      // The server permits halves and quarters as well as eighths and sixteenths; the client's
      // types rejected them until #100.
      expect(formatters.formatMeasurement(368.3, { unit: 'in', step: 2 })).toBe('14 1/2 in')
      expect(formatters.formatMeasurement(374.65, { unit: 'in', step: 4 })).toBe('14 3/4 in')
    })

    it('renders centimetres at the precision the field declares', () => {
      // Nought, one or two decimal places, per FieldPrecision. One is the default, and was fixed
      // here until #100.
      expect(formatters.formatMeasurement(368.35, { unit: 'cm', decimals: 2 })).toBe('36.84 cm')
      expect(formatters.formatMeasurement(368.35, { unit: 'cm', decimals: 0 })).toBe('37 cm')
    })

    it('renders centimetres to one decimal place', () => {
      expect(formatters.formatMeasurement(368.3, { unit: 'cm' })).toBe('36.8 cm')
    })

    it('takes the unit word from the caller, because the word is translated and the symbol is not', () => {
      expect(formatters.formatMeasurement(368.3, { unit: 'cm', unitLabel: 'சென்டிமீட்டர்' })).toBe(
        '36.8 சென்டிமீட்டர்',
      )
    })

    it('refuses a unit it does not know', () => {
      expect(() =>
        // @ts-expect-error — the point of the test is the runtime guard behind the type.
        formatters.formatMeasurement(100, { unit: 'yards' }),
      ).toThrow(/display unit/)
    })
  })
})

describe('the English day period', () => {
  it('is upper case, as the shop screens and printed documents have always shown it', () => {
    expect(createFormatters('en-IN').formatTime(AFTERNOON)).toBe('04:30 PM')
  })
})

describe('getFormatters', () => {
  it('returns the same instance for the same locale and timezone', () => {
    expect(getFormatters('en-IN')).toBe(getFormatters('en-IN'))
  })

  it('keeps branches in different timezones apart', () => {
    expect(getFormatters('en-IN', 'Asia/Dubai')).not.toBe(getFormatters('en-IN'))
    expect(getFormatters('en-IN', 'Asia/Dubai').formatTime(AFTERNOON)).toBe('03:00 PM')
  })

  it('defaults to English (India) in the branch timezone', () => {
    const formatters = getFormatters()
    expect(formatters.locale).toBe('en-IN')
    expect(formatters.timeZone).toBe('Asia/Kolkata')
  })
})
