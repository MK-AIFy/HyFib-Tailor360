import { describe, expect, it } from 'vitest'
import { BREAKPOINTS, LAYOUT_PROOF_WIDTHS, shellKindForWidth } from './breakpoints'
import { readDesignTokens } from '../testing/cssTokens'

describe('BREAKPOINTS', () => {
  it('matches the --breakpoint-* tokens, which are the same numbers in CSS', () => {
    const tokens = readDesignTokens('light')

    for (const [name, width] of Object.entries(BREAKPOINTS)) {
      expect(tokens.get(`--breakpoint-${name}`), `--breakpoint-${name}`).toBe(`${String(width)}px`)
    }
  })

  it('proves the layout at the widths the support matrix names', () => {
    expect(LAYOUT_PROOF_WIDTHS).toEqual([320, 360, 768, 1024, 1280])
  })
})

describe('shellKindForWidth', () => {
  it('gives the phone shell to every phone width, and to a narrowed desktop window', () => {
    expect(shellKindForWidth(320)).toBe('phone')
    expect(shellKindForWidth(360)).toBe('phone')
    expect(shellKindForWidth(767)).toBe('phone')
  })

  it('gives the tablet shell the master-detail range', () => {
    expect(shellKindForWidth(768)).toBe('tablet')
    expect(shellKindForWidth(1023)).toBe('tablet')
  })

  it('gives the desktop shell everything above', () => {
    expect(shellKindForWidth(1024)).toBe('desktop')
    expect(shellKindForWidth(1280)).toBe('desktop')
  })
})
