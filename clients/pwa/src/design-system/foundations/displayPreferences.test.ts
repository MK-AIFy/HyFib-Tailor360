import { describe, expect, it } from 'vitest'
import {
  DEFAULT_DISPLAY_PREFERENCES,
  applyDisplayPreferences,
  readDisplayPreferences,
} from './displayPreferences'

function element(): HTMLElement {
  return document.createElement('div')
}

describe('applyDisplayPreferences', () => {
  it('hands the decision back to the operating system for the system theme', () => {
    const target = element()
    applyDisplayPreferences(target, {
      theme: 'dark',
      textSize: '100',
      density: 'comfortable',
      reducedMotion: false,
    })
    applyDisplayPreferences(target, DEFAULT_DISPLAY_PREFERENCES)

    expect(target.hasAttribute('data-theme')).toBe(false)
    expect(target.getAttribute('data-text-size')).toBe('100')
    expect(target.getAttribute('data-density')).toBe('comfortable')
  })

  it('writes an explicit choice, which the token blocks select on', () => {
    const target = element()
    applyDisplayPreferences(target, {
      theme: 'contrast',
      textSize: '150',
      density: 'compact',
      reducedMotion: false,
    })

    expect(target.getAttribute('data-theme')).toBe('contrast')
    expect(target.getAttribute('data-text-size')).toBe('150')
    expect(target.getAttribute('data-density')).toBe('compact')
  })

  it('sets no inline style, because the policy forbids one and the cascade already has the palette', () => {
    const target = element()
    applyDisplayPreferences(target, {
      theme: 'dark',
      textSize: '125',
      density: 'comfortable',
      reducedMotion: false,
    })

    expect(target.getAttribute('style')).toBeNull()
  })

  it('sets the reduced-motion attribute only when it is explicitly asked for', () => {
    const target = element()
    applyDisplayPreferences(target, {
      theme: 'system',
      textSize: '100',
      density: 'comfortable',
      reducedMotion: true,
    })

    expect(target.getAttribute('data-reduced-motion')).toBe('true')
  })

  it('removes the reduced-motion attribute when it is false, leaving the operating system to decide', () => {
    const target = element()
    target.setAttribute('data-reduced-motion', 'true')

    applyDisplayPreferences(target, {
      theme: 'system',
      textSize: '100',
      density: 'comfortable',
      reducedMotion: false,
    })

    expect(target.hasAttribute('data-reduced-motion')).toBe(false)
  })
})

describe('readDisplayPreferences', () => {
  it('round-trips what was applied', () => {
    const target = element()
    const preferences = {
      theme: 'light',
      textSize: '125',
      density: 'compact',
      reducedMotion: true,
    } as const
    applyDisplayPreferences(target, preferences)

    expect(readDisplayPreferences(target)).toEqual(preferences)
  })

  it('falls back to the defaults for anything it does not recognise', () => {
    const target = element()
    target.setAttribute('data-theme', 'neon')
    target.setAttribute('data-text-size', '400')

    expect(readDisplayPreferences(target)).toEqual(DEFAULT_DISPLAY_PREFERENCES)
  })

  it('reads reduced motion as false when the attribute is absent', () => {
    const target = element()

    expect(readDisplayPreferences(target).reducedMotion).toBe(false)
  })
})
