import { describe, expect, it } from 'vitest'
import { MemoryRouter } from 'react-router'
import type { ReactNode } from 'react'
import { expectNoAccessibilityViolations } from '../../design-system/testing/axe'
import { renderWithProviders } from '../../design-system/testing/renderWithProviders'
import type { RenderWithProvidersOptions } from '../../design-system/testing/renderWithProviders'
import { HELP_HREF, HelpEntry, SUPPORT_HREF } from './HelpEntry'
import type { HelpEntryPlacement } from './HelpEntry'

function renderAt(path: string, ui: ReactNode, options?: RenderWithProvidersOptions) {
  return renderWithProviders(<MemoryRouter initialEntries={[path]}>{ui}</MemoryRouter>, options)
}

const placements: readonly HelpEntryPlacement[] = ['side', 'bar', 'inline']

describe('HelpEntry', () => {
  it.each(placements)(
    'is called Help and goes to the help screen in the %s placement',
    (placement) => {
      const { getByRole } = renderAt('/orders', <HelpEntry placement={placement} />)

      // 3.2.6 Consistent Help asks that help occur in the same relative order on every page that has
      // it; A11Y-10 and A11Y-85 ask whether it is here and whether it is called the same thing as on
      // the previous screen. One component and one string is the only way to guarantee both.
      const link = getByRole('link', { name: 'Help' })
      expect(link).toHaveAttribute('href', HELP_HREF)
    },
  )

  it('is named identically in every placement, which is the whole of 3.2.6', () => {
    const names = placements.map((placement) => {
      const { getByRole, unmount } = renderAt('/orders', <HelpEntry placement={placement} />)
      const name = getByRole('link', { name: 'Help' }).textContent
      unmount()
      return name
    })

    // There is deliberately no `label` prop on this component: a screen that could rename help
    // could break the criterion without anybody noticing until an audit. This is that decision,
    // asserted — three shells, one word.
    expect(new Set(names).size).toBe(1)
  })

  it('renders the support contact beside help when the shell asks for it', () => {
    const { getByRole } = renderAt('/orders', <HelpEntry showSupport />)

    // Section 4.5 names three things that sit together and stay together: help, the support contact
    // and the "how do I…" link.
    expect(getByRole('link', { name: 'Contact support' })).toHaveAttribute('href', SUPPORT_HREF)
  })

  it('omits the support contact unless it is asked for, so the set does not vary per screen', () => {
    const { queryByRole } = renderAt('/orders', <HelpEntry />)

    expect(queryByRole('link', { name: 'Contact support' })).toBeNull()
  })

  it('marks itself current on the help screen', () => {
    const { getByRole } = renderAt(HELP_HREF, <HelpEntry />)

    expect(getByRole('link', { name: 'Help' })).toHaveAttribute('aria-current', 'page')
  })

  it('carries a marker a shell test can find it by', () => {
    const { container } = renderAt('/orders', <HelpEntry placement="bar" />)

    // The layouts assert that this marker appears in the same slot on the phone, tablet and desktop
    // shells; that assertion is what turns "same place" from a convention into a test.
    expect(container.querySelector('[data-help-entry="true"]')).not.toBeNull()
  })

  it('translates its name, which the pseudo-locale makes visible', () => {
    const { container } = renderAt('/orders', <HelpEntry />, { locale: 'en-XA' })

    expect(container.textContent).toMatch(/⟦.+⟧/)
  })

  it('has no accessibility violations', async () => {
    const { container } = renderAt('/orders', <HelpEntry placement="side" showSupport />)

    await expectNoAccessibilityViolations(container)
  })
})
