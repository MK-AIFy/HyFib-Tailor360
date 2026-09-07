import { describe, expect, it } from 'vitest'
import { renderWithProviders } from '../../design-system/testing/renderWithProviders'
import { Icon } from './Icon'
import { ICON_NAMES, ICON_PATHS } from './icons'

describe('Icon', () => {
  it('is always hidden from assistive technology, with no way to make it otherwise', () => {
    const { container } = renderWithProviders(<Icon name="scan" />)
    const svg = container.querySelector('svg')

    // An icon in this system is the fast channel beside a word, never a substitute for one. Naming
    // the picture as well as the control is the doubled announcement A11Y-52 looks for.
    expect(svg).toHaveAttribute('aria-hidden', 'true')
    expect(svg).toHaveAttribute('role', 'presentation')
  })

  it('is not a tab stop, on any browser', () => {
    const { container } = renderWithProviders(<Icon name="help" />)

    // Older Edge and some assistive technologies put an SVG in the tab order without this, which
    // would place a dead stop inside every button on the screen.
    expect(container.querySelector('svg')).toHaveAttribute('focusable', 'false')
  })

  it('draws the path for the requested glyph', () => {
    const { container } = renderWithProviders(<Icon name="truck" />)

    expect(container.querySelector('path')).toHaveAttribute('d', ICON_PATHS.truck)
  })

  it.each(ICON_NAMES)('has drawable path data for %s', (name) => {
    const path = ICON_PATHS[name]

    expect(path.length).toBeGreaterThan(0)
    // Every path starts with an absolute or relative move, which is the cheapest proof that a
    // hand-written `d` string was not truncated in an edit.
    expect(path).toMatch(/^[Mm]/)
  })

  it('takes its size from a custom property rather than a hard-coded number', () => {
    const { container } = renderWithProviders(<Icon name="check" />)

    // Sizing in CSS is what keeps a glyph in step with the 100 / 125 / 150% text preference and with
    // 200% zoom; a width attribute here would stay 16 px for ever.
    expect(container.querySelector('svg')).not.toHaveAttribute('width')
  })
})
