import { describe, expect, it } from 'vitest'
import { enIN } from './en-IN'
import {
  PSEUDO_GROWTH_FACTOR,
  PSEUDO_LOCALE,
  buildPseudoCatalogue,
  pseudoCatalogue,
  pseudoLocaliseMessage,
} from './pseudo'

describe('pseudoLocaliseMessage', () => {
  it('brackets the string so a clipped label is obvious', () => {
    const result = pseudoLocaliseMessage('Home')
    expect(result.startsWith('⟦')).toBe(true)
    expect(result.endsWith('⟧')).toBe(true)
  })

  it('grows the text to at least 140%, which is what the layout must tolerate', () => {
    const source = 'The shop workspace is being built.'
    const result = pseudoLocaliseMessage(source)
    const inner = result.slice(1, -1)
    expect(inner.length).toBeGreaterThanOrEqual(Math.ceil(source.length * PSEUDO_GROWTH_FACTOR))
  })

  it('accents every letter, so hard-coded English stands out on a story', () => {
    expect(pseudoLocaliseMessage('Home')).toContain('Ĥ')
    expect(pseudoLocaliseMessage('Home')).not.toContain('Home')
  })

  it('leaves a simple ICU argument alone', () => {
    expect(pseudoLocaliseMessage('Version {version}')).toContain('{version}')
  })

  it('leaves a nested ICU argument alone, braces and all', () => {
    const source = '{count, plural, one {# job} other {# jobs}}'
    expect(pseudoLocaliseMessage(source)).toBe(`⟦${source}⟧`)
  })

  it('keeps a message with an argument in the middle compilable', () => {
    const result = pseudoLocaliseMessage('You are working in the {environment} environment.')
    expect(result.match(/\{/g)).toHaveLength(1)
    expect(result.match(/\}/g)).toHaveLength(1)
  })
})

describe('the pseudo catalogue', () => {
  it('covers every key of the English catalogue', () => {
    const pseudo = buildPseudoCatalogue(enIN)
    expect(Object.keys(pseudo)).toEqual(Object.keys(enIN))
  })

  it('is built once and reused', () => {
    expect(pseudoCatalogue()).toBe(pseudoCatalogue())
  })

  it('uses the conventional private-use tag and is not offered to staff', async () => {
    const { SUPPORTED_LOCALES } = await import('./locales')
    expect(PSEUDO_LOCALE).toBe('en-XA')
    expect(SUPPORTED_LOCALES as readonly string[]).not.toContain(PSEUDO_LOCALE)
  })
})
