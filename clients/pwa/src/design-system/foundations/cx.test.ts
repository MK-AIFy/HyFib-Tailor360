import { describe, expect, it } from 'vitest'
import { cx } from './cx'

describe('cx', () => {
  it('joins the class names it is given', () => {
    expect(cx('field', 'field--invalid')).toBe('field field--invalid')
  })

  it('drops anything falsy, so a conditional class needs no ternary', () => {
    expect(cx('field', false, null, undefined, '', 'field--required')).toBe('field field--required')
  })

  it('returns an empty string rather than undefined when nothing survives', () => {
    expect(cx(false, undefined)).toBe('')
  })
})
