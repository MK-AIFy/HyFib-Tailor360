import { describe, expect, it } from 'vitest'
import { readCssVariable, setCssVariable } from './setCssVariable'

describe('setCssVariable', () => {
  it('sets a custom property through the CSSOM, not through a style attribute the policy would block', () => {
    const target = document.createElement('div')
    setCssVariable(target, '--bottom-bar-height', '72px')

    expect(target.style.getPropertyValue('--bottom-bar-height')).toBe('72px')
  })

  it('accepts a number, because a measured height arrives as one', () => {
    const target = document.createElement('div')
    setCssVariable(target, '--column-count', 3)

    expect(target.style.getPropertyValue('--column-count')).toBe('3')
  })

  it('removes the property on null, falling back to the inherited token', () => {
    const target = document.createElement('div')
    setCssVariable(target, '--bottom-bar-height', '72px')
    setCssVariable(target, '--bottom-bar-height', null)

    expect(target.style.getPropertyValue('--bottom-bar-height')).toBe('')
  })
})

describe('readCssVariable', () => {
  it('reads a property back, trimmed', () => {
    const target = document.createElement('div')
    document.body.append(target)
    setCssVariable(target, '--target-primary', ' 56px ')

    expect(readCssVariable(target, '--target-primary')).toBe('56px')
    target.remove()
  })

  it('returns an empty string for a property nothing has set', () => {
    const target = document.createElement('div')
    document.body.append(target)

    expect(readCssVariable(target, '--not-a-token')).toBe('')
    target.remove()
  })
})
