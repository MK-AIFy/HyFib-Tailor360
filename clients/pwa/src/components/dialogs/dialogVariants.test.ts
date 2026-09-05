import { describe, expect, it } from 'vitest'
import { resolveDialogPresentation } from './dialogVariants'

describe('resolveDialogPresentation', () => {
  it('gives a phone a bottom sheet', () => {
    // The other hand is holding a garment; the thumb reaches the bottom third of the screen.
    expect(resolveDialogPresentation('auto', 'phone')).toBe('sheet')
  })

  it.each(['tablet', 'desktop'] as const)('gives a %s a centred dialog', (shell) => {
    expect(resolveDialogPresentation('auto', shell)).toBe('centre')
  })

  it.each(['sheet', 'centre', 'drawer'] as const)(
    'leaves an explicit %s presentation alone, whatever the shell',
    (presentation) => {
      expect(resolveDialogPresentation(presentation, 'phone')).toBe(presentation)
      expect(resolveDialogPresentation(presentation, 'desktop')).toBe(presentation)
    },
  )
})
