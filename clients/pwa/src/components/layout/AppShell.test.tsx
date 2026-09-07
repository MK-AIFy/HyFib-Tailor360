import { act, within } from '@testing-library/react'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { MemoryRouter } from 'react-router'
import type { ReactNode } from 'react'
import { expectNoAccessibilityViolations } from '../../design-system/testing/axe'
import { renderWithProviders } from '../../design-system/testing/renderWithProviders'
import type { RenderWithProvidersOptions } from '../../design-system/testing/renderWithProviders'
import { PSEUDO_LOCALE } from '../../i18n/pseudo'
import { DisplayPreferencesProvider } from '../../app/DisplayPreferencesProvider'
import { createInMemoryDisplayPreferencesStore } from '../../app/preferences'
import { DEFAULT_DISPLAY_PREFERENCES } from '../../design-system/foundations/displayPreferences'
import { AppShell } from './AppShell'
import type { ShellKind } from '../../design-system/foundations/types'
import { versionPayload } from '../../app/testing/versionFixture'

/** The visual viewport jsdom does not have. See useVirtualKeyboard.test.ts for the same stub. */
const viewportListeners = new Set<() => void>()

function installVisualViewport(height: number) {
  let current = height
  Object.defineProperty(window, 'visualViewport', {
    configurable: true,
    value: {
      get height() {
        return current
      },
      addEventListener: (_type: string, listener: () => void) => viewportListeners.add(listener),
      removeEventListener: (_type: string, listener: () => void) =>
        viewportListeners.delete(listener),
    },
  })
  return {
    resize(next: number) {
      current = next
      act(() => {
        for (const listener of [...viewportListeners]) {
          listener()
        }
      })
    },
  }
}

/**
 * The preferences go to the provider rather than to `renderWithProviders`, because the provider is
 * the authority: it writes the three attributes onto `<html>` on mount and would otherwise overwrite
 * anything the render helper had set.
 */
function renderShell(
  shellKind: ShellKind,
  children: ReactNode = <h1>Orders</h1>,
  options: Pick<RenderWithProvidersOptions, 'locale' | 'preferences'> = {},
) {
  const preferences = options.preferences ?? DEFAULT_DISPLAY_PREFERENCES

  return renderWithProviders(
    <MemoryRouter initialEntries={['/orders']}>
      <DisplayPreferencesProvider
        store={createInMemoryDisplayPreferencesStore(preferences)}
        initial={preferences}
      >
        <AppShell shellKind={shellKind}>{children}</AppShell>
      </DisplayPreferencesProvider>
    </MemoryRouter>,
    options.locale === undefined ? {} : { locale: options.locale },
  )
}

beforeEach(() => {
  vi.stubGlobal(
    'fetch',
    vi.fn(() =>
      Promise.resolve(
        new Response(JSON.stringify(versionPayload({ environment: 'production' })), {
          status: 200,
          headers: { 'Content-Type': 'application/json' },
        }),
      ),
    ),
  )
})

afterEach(() => {
  vi.unstubAllGlobals()
  viewportListeners.clear()
  Object.defineProperty(window, 'visualViewport', { configurable: true, value: undefined })
  document.documentElement.removeAttribute('data-shell')
  document.documentElement.removeAttribute('data-keyboard')
  document.documentElement.style.removeProperty('--virtual-keyboard-height')
})

describe('AppShell — the landmarks every layout owes', () => {
  it.each(['phone', 'tablet', 'desktop'] as const)(
    'gives the %s layout exactly one navigation landmark',
    (shellKind) => {
      const { container } = renderShell(shellKind)

      // Two named "Main navigation" landmarks is what checklist item A11Y-06 fails a screen for, and
      // it is exactly what rendering all three shells and hiding two with CSS would produce.
      const landmarks = within(container).getAllByRole('navigation', { name: 'Main navigation' })
      expect(landmarks).toHaveLength(1)
    },
  )

  it.each(['phone', 'tablet', 'desktop'] as const)(
    'puts the skip link, main landmark and help entry in the %s layout',
    (shellKind) => {
      const { container } = renderShell(shellKind)
      const shell = within(container)

      expect(shell.getByRole('link', { name: 'Skip to main content' })).toHaveAttribute(
        'href',
        '#main-content',
      )
      expect(shell.getByRole('main')).toHaveAttribute('id', 'main-content')
      // 3.2.6 Consistent Help, checklist items A11Y-10 and A11Y-85: same entry, same name, every
      // layout — only the slot it sits in changes.
      expect(shell.getByRole('link', { name: 'Help' })).toBeInTheDocument()
      expect(shell.getByRole('link', { name: 'Display settings' })).toBeInTheDocument()
    },
  )

  it.each(['phone', 'tablet', 'desktop'] as const)(
    'has no accessibility violations in the %s layout',
    async (shellKind) => {
      const { container } = renderShell(shellKind)

      await expectNoAccessibilityViolations(container)
    },
  )
})

describe('AppShell — the phone layout', () => {
  it('puts navigation at the bottom and the role primary action beside it', () => {
    const { container } = renderShell('phone')

    expect(container.querySelector('.bottom-nav')).not.toBeNull()
    expect(container.querySelector('.side-nav')).toBeNull()
    // Reception is the default role until there is a session; its primary action is New order.
    expect(within(container).getByRole('link', { name: 'New order' })).toBeInTheDocument()
  })

  it('gives every bottom-bar destination a word, not only a glyph', () => {
    const { container } = renderShell('phone')
    const bar = container.querySelector('.bottom-nav')

    expect(bar).not.toBeNull()
    for (const label of ['Home', 'Customers', 'Measurements', 'Orders', 'Billing']) {
      expect(within(bar as HTMLElement).getByRole('link', { name: label })).toBeInTheDocument()
    }
  })
})

describe('AppShell — the tablet and desktop layouts', () => {
  it('gives the tablet a rail rather than a bottom bar', () => {
    // A counter tablet is docked or held in two hands; in landscape its bottom edge is the furthest
    // point from either thumb.
    const { container } = renderShell('tablet')

    expect(container.querySelector('.side-nav')).not.toBeNull()
    expect(container.querySelector('.bottom-nav')).toBeNull()
    expect(container.querySelector('.primary-action-fab')).toBeNull()
  })

  it('groups the desktop rail under real headings', () => {
    const { container } = renderShell('desktop')
    const rail = within(container)

    // Real h2s, so heading navigation works on a rail a Cashier uses all day (1.3.1).
    expect(
      rail.getByRole('heading', { level: 2, name: 'Customers and orders' }),
    ).toBeInTheDocument()
    expect(rail.getByRole('heading', { level: 2, name: 'Money' })).toBeInTheDocument()
  })

  it('puts the support contact beside help on the desktop only', () => {
    // The rail has room for all three; a phone header has room for two. Scoped to each container,
    // because Testing Library's own queries reach the whole document body.
    const desktop = renderShell('desktop')
    expect(
      within(desktop.container).queryByRole('link', { name: 'Contact support' }),
    ).not.toBeNull()

    const phone = renderShell('phone')
    expect(within(phone.container).queryByRole('link', { name: 'Contact support' })).toBeNull()
  })
})

describe('AppShell — Focus Not Obscured (2.4.11)', () => {
  it('names the running layout on the document so html-level tokens follow it', () => {
    renderShell('phone')
    expect(document.documentElement).toHaveAttribute('data-shell', 'phone')
  })

  it('hides the bottom bar and the floating action while the keyboard is open', () => {
    const viewport = installVisualViewport(window.innerHeight)
    const { container } = renderShell('phone')
    const shell = within(container)

    expect(shell.queryByRole('navigation', { name: 'Main navigation' })).not.toBeNull()

    viewport.resize(window.innerHeight - 320)

    // `hidden` removes them from the layout and from the accessibility tree together, so nothing is
    // announced that cannot be reached. The elements are still in the DOM; they are simply gone.
    expect(shell.queryByRole('navigation', { name: 'Main navigation' })).toBeNull()
    expect(container.querySelector('.bottom-nav')).toHaveAttribute('hidden')
    expect(container.querySelector('.primary-action-fab')).toHaveAttribute('hidden')
  })

  it('publishes the keyboard height for the browser that resizes nothing', () => {
    // iOS Safari honours neither interactive-widget=resizes-content nor a layout-viewport resize.
    const viewport = installVisualViewport(window.innerHeight)
    renderShell('phone')

    viewport.resize(window.innerHeight - 300)

    expect(document.documentElement).toHaveAttribute('data-keyboard', 'open')
    expect(document.documentElement.style.getPropertyValue('--virtual-keyboard-height')).toBe(
      '300px',
    )
  })

  it('gives the height and the attribute back when the keyboard closes', () => {
    const viewport = installVisualViewport(window.innerHeight)
    renderShell('phone')

    viewport.resize(window.innerHeight - 300)
    viewport.resize(window.innerHeight)

    expect(document.documentElement).not.toHaveAttribute('data-keyboard')
    expect(document.documentElement.style.getPropertyValue('--virtual-keyboard-height')).toBe('')
  })

  it('leaves the document clean when the shell unmounts', () => {
    const { unmount } = renderShell('phone')

    unmount()

    expect(document.documentElement).not.toHaveAttribute('data-shell')
  })
})

describe('AppShell — text growth', () => {
  it.each(['phone', 'tablet', 'desktop'] as const)(
    'renders the %s layout in the pseudo-locale',
    (shellKind) => {
      // The 40% growth tolerance of accessibility-localisation.md section 4.2, which is what makes
      // the Tamil catalogue safe to switch on. Anything left in plain English here is hard-coded.
      const { container } = renderShell(shellKind, <h1>Orders</h1>, { locale: PSEUDO_LOCALE })

      expect(within(container).getAllByRole('navigation')).toHaveLength(1)
      expect(container.textContent).not.toContain('Display settings')
    },
  )

  it.each(['100', '125', '150'] as const)('renders at the %s per cent text size', (textSize) => {
    const { container } = renderShell('phone', <h1>Orders</h1>, {
      preferences: { theme: 'system', textSize, density: 'comfortable' },
    })

    expect(document.documentElement).toHaveAttribute('data-text-size', textSize)
    expect(within(container).getByRole('heading', { level: 1, name: 'Orders' })).toBeInTheDocument()
  })
})
