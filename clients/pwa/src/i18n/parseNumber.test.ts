import { describe, expect, it } from 'vitest'
import {
  parseDecimal,
  parseDecimalString,
  parseInchInput,
  parseMeasurementToMillimetres,
} from './parseNumber'

describe('parseDecimal', () => {
  it('accepts a full stop as the decimal separator', () => {
    expect(parseDecimal('36.5')).toBe(36.5)
  })

  it('accepts a comma as the decimal separator, which is what the keypad often offers', () => {
    expect(parseDecimal('36,5')).toBe(36.5)
    expect(parseDecimal('36,25')).toBe(36.25)
  })

  it('reads Indian grouping', () => {
    expect(parseDecimal('12,34,567.89')).toBe(1234567.89)
    expect(parseDecimal('1,234')).toBe(1234)
  })

  it('reads European grouping, because values are pasted as often as typed', () => {
    expect(parseDecimal('12.34.567,89')).toBe(1234567.89)
  })

  it('strips a pasted rupee symbol and any spacing around it', () => {
    expect(parseDecimal('₹ 1,200.00')).toBe(1200)
  })

  it('reads a signed value', () => {
    expect(parseDecimal('-45,5')).toBe(-45.5)
    expect(parseDecimal('+45.5')).toBe(45.5)
  })

  it('fills in a missing whole part or a missing fraction', () => {
    expect(parseDecimal('.5')).toBe(0.5)
    expect(parseDecimal('5.')).toBe(5)
  })

  it('resolves the one genuine ambiguity towards grouping', () => {
    // A lone comma followed by exactly three digits is a thousands group in Indian usage, and no
    // field in this product takes three decimal places.
    expect(parseDecimal('36,500')).toBe(36500)
    expect(parseDecimal('36,50')).toBe(36.5)
  })

  it('returns null rather than NaN for anything unreadable', () => {
    expect(parseDecimal('')).toBeNull()
    expect(parseDecimal('   ')).toBeNull()
    expect(parseDecimal('thirty six')).toBeNull()
    expect(parseDecimal('36 1/2')).toBeNull()
    expect(parseDecimal('1.2.3,4.5')).toBeNull()
    expect(parseDecimal('-')).toBeNull()
  })
})

describe('parseDecimalString', () => {
  it('returns text, so an amount reaches the API without becoming a floating-point number', () => {
    expect(parseDecimalString('₹12,34,567.89')).toBe('1234567.89')
    expect(parseDecimalString('-1,200.00')).toBe('-1200.00')
  })

  it('keeps the scale the person typed rather than normalising it away', () => {
    expect(parseDecimalString('249.5000')).toBe('249.5000')
  })
})

describe('parseInchInput', () => {
  it('reads the forms staff actually type', () => {
    expect(parseInchInput('36 1/2')).toBe(36.5)
    expect(parseInchInput('36-1/2')).toBe(36.5)
    expect(parseInchInput('36 1/2 in')).toBe(36.5)
    expect(parseInchInput('36 1/2"')).toBe(36.5)
    expect(parseInchInput('1/2')).toBe(0.5)
    expect(parseInchInput('36')).toBe(36)
    expect(parseInchInput('36.5')).toBe(36.5)
    expect(parseInchInput('36,5')).toBe(36.5)
  })

  it('reads a negative allowance', () => {
    expect(parseInchInput('-1 1/4')).toBe(-1.25)
  })

  it('returns null for nonsense and for a zero denominator', () => {
    expect(parseInchInput('')).toBeNull()
    expect(parseInchInput('1/0')).toBeNull()
    expect(parseInchInput('half')).toBeNull()
  })
})

describe('parseMeasurementToMillimetres', () => {
  it('converts every display unit to the one canonical unit', () => {
    expect(parseMeasurementToMillimetres('14 1/2', 'in')).toBe(368.3)
    expect(parseMeasurementToMillimetres('36,8', 'cm')).toBe(368)
    expect(parseMeasurementToMillimetres('368.3', 'mm')).toBe(368.3)
  })

  it('passes a parse failure through as null', () => {
    expect(parseMeasurementToMillimetres('about a foot', 'in')).toBeNull()
  })
})
