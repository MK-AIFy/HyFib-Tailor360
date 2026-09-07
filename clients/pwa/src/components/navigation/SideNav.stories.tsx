import type { Meta, StoryObj } from '@storybook/react-vite'
import { MemoryRouter } from 'react-router'
import { HelpEntry } from './HelpEntry'
import { SideNav } from './SideNav'
import type { NavigationSection } from './navigationItems'
import { useDemoText } from '../primitives/demoText'
import './navigationStories.css'

/**
 * The desktop navigation rail.
 *
 * A persistent list rather than a menu that opens: the counter and print-station desktops are
 * keyboard-driven, and a navigation that has to be opened before it can be tabbed puts an extra
 * keystroke in front of every move a Cashier makes all day.
 *
 * The rail scrolls its list and pins its footer, which is what keeps the help entry in the same
 * place on a screen with six groups and on a screen with two — 3.2.6 Consistent Help, made true in
 * practice rather than only in the markup.
 */
const meta = {
  title: 'Navigation/Side navigation',
  component: SideNav,
  parameters: { layout: 'padded' },
  decorators: [
    (Story) => (
      <MemoryRouter initialEntries={['/workboard']}>
        <div className="nav-story-rail">
          <Story />
          <div className="nav-story-rail__content">
            <h1>Workboard</h1>
            <p>The screen beside the rail.</p>
          </div>
        </div>
      </MemoryRouter>
    ),
  ],
} satisfies Meta<typeof SideNav>

export default meta
type Story = StoryObj<typeof meta>

function useSections(): readonly NavigationSection[] {
  const demo = useDemoText()
  return [
    {
      id: 'shop',
      label: demo('Shop floor'),
      items: [
        { id: 'orders', label: demo('Orders'), href: '/orders', icon: 'clipboard' },
        {
          id: 'workboard',
          label: demo('Workboard'),
          href: '/workboard',
          icon: 'layout',
          badgeCount: 12,
        },
        { id: 'production', label: demo('Production'), href: '/production', icon: 'scissors' },
      ],
    },
    {
      id: 'people',
      label: demo('People and stock'),
      items: [
        { id: 'customers', label: demo('Customers'), href: '/customers', icon: 'users' },
        { id: 'inventory', label: demo('Inventory'), href: '/inventory', icon: 'package' },
      ],
    },
    {
      id: 'money',
      label: demo('Money'),
      items: [
        { id: 'billing', label: demo('Billing'), href: '/billing', icon: 'receipt', badgeCount: 3 },
        { id: 'reports', label: demo('Reports'), href: '/reports', icon: 'bar-chart' },
      ],
    },
  ]
}

/** A Tailor Master's rail, with the help entry pinned at the foot. */
export const Grouped: Story = {
  render: function GroupedStory() {
    const demo = useDemoText()
    return (
      <SideNav
        items={[{ id: 'home', label: demo('Home'), href: '/', icon: 'home', end: true }]}
        sections={useSections()}
        footer={<HelpEntry placement="side" showSupport />}
      />
    )
  },
}

/** A short rail with no groups at all — a Cashier has four places to be. */
export const Flat: Story = {
  render: function FlatStory() {
    const demo = useDemoText()
    return (
      <SideNav
        items={[
          { id: 'home', label: demo('Home'), href: '/', icon: 'home', end: true },
          { id: 'billing', label: demo('Billing'), href: '/billing', icon: 'receipt' },
          { id: 'payments', label: demo('Payments'), href: '/payments', icon: 'rupee' },
          { id: 'reports', label: demo('Reports'), href: '/reports', icon: 'bar-chart' },
        ]}
        footer={<HelpEntry placement="side" />}
      />
    )
  },
}

/**
 * The pseudo-locale, at 40% growth.
 *
 * Group headings, destination names and the help entry all grow. The rail keeps its width and the
 * labels wrap; nothing is truncated, because a truncated destination is a destination that has to be
 * guessed at.
 */
export const PseudoLocale: Story = {
  globals: { locale: 'en-XA' },
  render: function PseudoLocaleStory() {
    return <SideNav sections={useSections()} footer={<HelpEntry placement="side" showSupport />} />
  },
}
