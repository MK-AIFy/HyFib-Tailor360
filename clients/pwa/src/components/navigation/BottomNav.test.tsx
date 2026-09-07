import { describe, expect, it } from 'vitest'
import { MemoryRouter } from 'react-router'
import type { ReactNode } from 'react'
import { expectNoAccessibilityViolations } from '../../design-system/testing/axe'
import { renderWithProviders } from '../../design-system/testing/renderWithProviders'
import type { RenderWithProvidersOptions } from '../../design-system/testing/renderWithProviders'
import { BottomNav } from './BottomNav'
import { MAX_BOTTOM_NAV_ITEMS } from './navigationItems'
import type { NavigationItem } from './navigationItems'

const items: readonly NavigationItem[] = [
  { id: 'home', label: 'Home', href: '/', icon: 'home', end: true },
  { id: 'scan', label: 'Scan', href: '/scan', icon: 'scan' },
  { id: 'orders', label: 'Orders', href: '/orders', icon: 'clipboard', badgeCount: 4 },
  { id: 'delivery', label: 'Delivery', href: '/delivery', icon: 'truck' },
]

/** The navigation family renders router links, so every test needs a router around it. */
function renderAt(path: string, ui: ReactNode, options?: RenderWithProvidersOptions) {
  return renderWithProviders(<MemoryRouter initialEntries={[path]}>{ui}</MemoryRouter>, options)
}

describe('BottomNav', () => {
  it('is a named navigation landmark', () => {
    const { getByRole } = renderAt('/scan', <BottomNav items={items} />)

    expect(getByRole('navigation', { name: 'Main navigation' })).toBeInTheDocument()
  })

  it('gives every destination a word, not only a glyph', () => {
    const { getByRole } = renderAt('/scan', <BottomNav items={items} />)

    // A glyph a person has not learnt yet is a guess, and a glyph at the end of a shift is a guess
    // for everybody.
    for (const item of items) {
      expect(getByRole('link', { name: new RegExp(item.label) })).toBeInTheDocument()
    }
  })

  it('marks the current destination for assistive technology as well as visually', () => {
    const { getByRole } = renderAt('/orders', <BottomNav items={items} />)

    // The weight, the top rule and the fill all hang off aria-current, so the marking cannot drift
    // apart from what is announced.
    expect(getByRole('link', { name: /Orders/ })).toHaveAttribute('aria-current', 'page')
  })

  it('does not mark the root as current on every screen', () => {
    const { getByRole } = renderAt('/orders', <BottomNav items={items} />)

    expect(getByRole('link', { name: /^Home/ })).not.toHaveAttribute('aria-current')
  })

  it('carries the attention count as a number and as a sentence', () => {
    const { getByRole } = renderAt('/scan', <BottomNav items={items} />)

    expect(getByRole('link', { name: /Orders/ })).toHaveAccessibleName(/4 items need attention/)
  })

  it('gets out of the way while the keyboard is open', () => {
    const { getByRole } = renderAt('/scan', <BottomNav items={items} keyboardOpen />)

    // 2.4.11 Focus Not Obscured: the commonest phone form failure there is. hidden removes the bar
    // from the layout and from the accessibility tree, so nothing is announced that cannot be
    // reached.
    expect(getByRole('navigation', { hidden: true })).toHaveAttribute('hidden')
  })

  it('is present when the keyboard is not', () => {
    const { getByRole } = renderAt('/scan', <BottomNav items={items} keyboardOpen={false} />)

    expect(getByRole('navigation')).not.toHaveAttribute('hidden')
  })

  it('holds at most five destinations, which is what fits at the 320 px reflow floor', () => {
    // Six 44 px targets with legible labels do not fit at 320 CSS px, and shrinking them below the
    // AL-03 standard size to make them fit is the trade section 5 refuses. A role with more
    // destinations puts the rest behind a More screen.
    expect(items.length).toBeLessThanOrEqual(MAX_BOTTOM_NAV_ITEMS)
    expect(MAX_BOTTOM_NAV_ITEMS).toBe(5)
  })

  it('has no accessibility violations', async () => {
    const { container } = renderAt('/scan', <BottomNav items={items} keyboardOpen={false} />)

    await expectNoAccessibilityViolations(container)
  })

  it('translates its own words, which the pseudo-locale makes visible', () => {
    const { getByRole } = renderAt('/scan', <BottomNav items={items} keyboardOpen={false} />, {
      locale: 'en-XA',
    })

    expect(getByRole('navigation').getAttribute('aria-label')).toMatch(/⟦.+⟧/)
  })
})
