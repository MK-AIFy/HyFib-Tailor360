import type { Meta, StoryObj } from '@storybook/react-vite'
import { MemoryRouter } from 'react-router'
import { BottomNav } from './BottomNav'
import type { NavigationItem } from './navigationItems'
import { useDemoText } from '../primitives/demoText'
import './navigationStories.css'

/**
 * The phone navigation bar.
 *
 * Everything about it follows from one sentence in docs/nfr/accessibility-localisation.md section 3:
 * the device is used one-handed, often with a thumb, while the other hand holds a garment. So it is
 * at the bottom, every destination carries a word as well as a glyph, and it pads its own safe area
 * plus the 8 px of gesture-bar clearance that checklist item A11Y-70 measures.
 *
 * Compare `Default` with `KeyboardOpen`: while the on-screen keyboard is up the bar is removed
 * outright, which is the other half of 2.4.11 Focus Not Obscured — the half `scroll-padding-bottom`
 * alone cannot deliver on a screen where the keyboard has taken most of the viewport.
 */
const meta = {
  title: 'Navigation/Bottom navigation',
  component: BottomNav,
  args: { items: [], keyboardOpen: false },
  parameters: { layout: 'centered' },
  decorators: [
    (Story) => (
      <MemoryRouter initialEntries={['/scan']}>
        <div className="nav-story-phone">
          <div className="nav-story-phone__content">
            <h1>Scan</h1>
            <p>
              The screen behind the bar. Scroll it: the bar stays put, and the content reserves the
              bar&rsquo;s height so the last control never ends up underneath it.
            </p>
          </div>
          <Story />
        </div>
      </MemoryRouter>
    ),
  ],
} satisfies Meta<typeof BottomNav>

export default meta
type Story = StoryObj<typeof meta>

function useTailorItems(): readonly NavigationItem[] {
  const demo = useDemoText()
  return [
    { id: 'home', label: demo('Home'), href: '/', icon: 'home', end: true },
    { id: 'scan', label: demo('Scan'), href: '/scan', icon: 'scan' },
    { id: 'queue', label: demo('My queue'), href: '/queue', icon: 'list', badgeCount: 4 },
    { id: 'help', label: demo('Help'), href: '/help', icon: 'help' },
  ]
}

/** A Tailor's bar: four destinations, the current one marked by weight, a rule and a fill. */
export const Default: Story = {
  render: function DefaultStory() {
    return <BottomNav items={useTailorItems()} keyboardOpen={false} />
  },
}

/** The full five. Six would not fit at the 320 px reflow floor without shrinking below 44 px. */
export const FiveDestinations: Story = {
  render: function FiveDestinationsStory() {
    const demo = useDemoText()
    return (
      <BottomNav
        keyboardOpen={false}
        items={[
          { id: 'home', label: demo('Home'), href: '/', icon: 'home', end: true },
          { id: 'orders', label: demo('Orders'), href: '/orders', icon: 'clipboard' },
          { id: 'scan', label: demo('Scan'), href: '/scan', icon: 'scan' },
          {
            id: 'billing',
            label: demo('Billing'),
            href: '/billing',
            icon: 'receipt',
            badgeCount: 12,
          },
          { id: 'delivery', label: demo('Delivery'), href: '/delivery', icon: 'truck' },
        ]}
      />
    )
  },
}

/**
 * The keyboard is up, so the bar is gone.
 *
 * Not merely moved or faded: `hidden`, which removes it from the layout **and** from the
 * accessibility tree, so nothing is announced that cannot be reached.
 */
export const KeyboardOpen: Story = {
  render: function KeyboardOpenStory() {
    return <BottomNav items={useTailorItems()} keyboardOpen />
  },
}

/** At the 320 px reflow floor. Every label still readable, every target still 56 px tall. */
export const ReflowFloor: Story = {
  globals: { viewport: { value: 'reflowFloor' } },
  render: function ReflowFloorStory() {
    return <BottomNav items={useTailorItems()} keyboardOpen={false} />
  },
}

/**
 * The pseudo-locale, at 40% growth.
 *
 * This is the hardest test in the family: five destinations, each with a word, on a 360 px screen.
 * The labels wrap rather than clip — a clipped destination name is a destination nobody can
 * identify, which is why the bar grows taller instead of truncating.
 */
export const PseudoLocale: Story = {
  globals: { locale: 'en-XA' },
  render: function PseudoLocaleStory() {
    const demo = useDemoText()
    return (
      <BottomNav
        keyboardOpen={false}
        items={[
          { id: 'home', label: demo('Home'), href: '/', icon: 'home', end: true },
          { id: 'orders', label: demo('Orders'), href: '/orders', icon: 'clipboard' },
          { id: 'scan', label: demo('Scan'), href: '/scan', icon: 'scan' },
          {
            id: 'billing',
            label: demo('Billing'),
            href: '/billing',
            icon: 'receipt',
            badgeCount: 12,
          },
          { id: 'delivery', label: demo('Delivery'), href: '/delivery', icon: 'truck' },
        ]}
      />
    )
  },
}
