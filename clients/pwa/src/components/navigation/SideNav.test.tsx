import { describe, expect, it } from 'vitest'
import { MemoryRouter } from 'react-router'
import type { ReactNode } from 'react'
import { expectNoAccessibilityViolations } from '../../design-system/testing/axe'
import { renderWithProviders } from '../../design-system/testing/renderWithProviders'
import { HelpEntry } from './HelpEntry'
import { SideNav } from './SideNav'
import type { NavigationItem, NavigationSection } from './navigationItems'

const primary: readonly NavigationItem[] = [
  { id: 'home', label: 'Home', href: '/', icon: 'home', end: true },
]

const sections: readonly NavigationSection[] = [
  {
    id: 'shop',
    label: 'Shop floor',
    items: [
      { id: 'orders', label: 'Orders', href: '/orders', icon: 'clipboard' },
      { id: 'workboard', label: 'Workboard', href: '/workboard', icon: 'layout', badgeCount: 12 },
    ],
  },
  {
    id: 'money',
    label: 'Money',
    items: [{ id: 'billing', label: 'Billing', href: '/billing', icon: 'receipt' }],
  },
]

function renderAt(path: string, ui: ReactNode) {
  return renderWithProviders(<MemoryRouter initialEntries={[path]}>{ui}</MemoryRouter>)
}

describe('SideNav', () => {
  it('is a named navigation landmark', () => {
    const { getByRole } = renderAt('/orders', <SideNav items={primary} sections={sections} />)

    expect(getByRole('navigation', { name: 'Main navigation' })).toBeInTheDocument()
  })

  it('gives each group a real heading, because a rail is walked by heading', () => {
    const { getByRole } = renderAt('/orders', <SideNav sections={sections} />)

    // 1.3.1: the grouping that is obvious visually exists in the structure too.
    expect(getByRole('heading', { name: 'Shop floor' })).toBeInTheDocument()
    expect(getByRole('heading', { name: 'Money' })).toBeInTheDocument()
  })

  it('names each list by its group, so a screen reader says which one it has entered', () => {
    const { getByRole } = renderAt('/orders', <SideNav sections={sections} />)

    expect(getByRole('list', { name: 'Shop floor' })).toBeInTheDocument()
  })

  it('marks the current destination', () => {
    const { getByRole } = renderAt('/billing', <SideNav items={primary} sections={sections} />)

    expect(getByRole('link', { name: /Billing/ })).toHaveAttribute('aria-current', 'page')
    expect(getByRole('link', { name: /^Home/ })).not.toHaveAttribute('aria-current')
  })

  it('carries the attention count as words as well as a number', () => {
    const { getByRole } = renderAt('/orders', <SideNav sections={sections} />)

    expect(getByRole('link', { name: /Workboard/ })).toHaveAccessibleName(/12 items need attention/)
  })

  it('keeps a reserved slot at the foot for the help entry', () => {
    const { getByRole, container } = renderAt(
      '/orders',
      <SideNav sections={sections} footer={<HelpEntry placement="side" showSupport />} />,
    )

    // 3.2.6 Consistent Help, and checklist items A11Y-10 and A11Y-85: same place, same name, every
    // screen. The rail scrolls its list and pins this, so the position does not move with the
    // number of groups.
    expect(getByRole('link', { name: 'Help' })).toBeInTheDocument()
    expect(container.querySelector('.side-nav__footer')).toContainElement(
      getByRole('link', { name: 'Help' }),
    )
  })

  it('renders nothing extra when it has no ungrouped items', () => {
    const { container } = renderAt('/orders', <SideNav sections={sections} />)

    // Two group lists, and no empty third one above them.
    expect(container.querySelectorAll('.side-nav__list')).toHaveLength(sections.length)
  })

  it('has no accessibility violations', async () => {
    const { container } = renderAt(
      '/orders',
      <SideNav items={primary} sections={sections} footer={<HelpEntry placement="side" />} />,
    )

    await expectNoAccessibilityViolations(container)
  })
})
