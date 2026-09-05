import type { Meta, StoryObj } from '@storybook/react-vite'
import { Tabs } from './Tabs'
import type { TabItem } from './Tabs'
import { useDemoText } from '../primitives/demoText'

/**
 * Tabs.
 *
 * Try this story from the keyboard, which is how the counter desktop is used. `Tab` reaches the
 * strip once — not once per tab — and then moves on to the panel; the arrow keys move between tabs
 * and `Home` and `End` jump to the ends. The panel itself is focusable, because 2.1.1 has no
 * exception for "it is only text".
 *
 * Tabs divide one screen. Moving between screens is the bottom bar and the rail, which are links
 * because they go somewhere.
 */
const meta = {
  title: 'Navigation/Tabs',
  component: Tabs,
  args: { items: [] },
  parameters: { layout: 'padded' },
} satisfies Meta<typeof Tabs>

export default meta
type Story = StoryObj<typeof meta>

function useOrderTabs(): readonly TabItem[] {
  const demo = useDemoText()
  return [
    {
      id: 'details',
      label: demo('Details'),
      icon: 'clipboard',
      panel: <p>{demo('Order 4021 — Lakshmi Narayanan, two garment jobs, due 12-09-2026.')}</p>,
    },
    {
      id: 'measurements',
      label: demo('Measurements'),
      icon: 'ruler',
      panel: <p>{demo('Blouse template, version 3. Captured 02-09-2026 by Devi S.')}</p>,
    },
    {
      id: 'history',
      label: demo('History'),
      icon: 'clock',
      badgeCount: 2,
      panel: <p>{demo('Five custody events, of which two need attention.')}</p>,
    },
    {
      id: 'billing',
      label: demo('Billing'),
      icon: 'receipt',
      panel: <p>{demo('Estimate issued. Balance ₹2,450.00.')}</p>,
    },
  ]
}

export const Default: Story = {
  render: function DefaultStory() {
    return <Tabs items={useOrderTabs()} />
  },
}

export const StartingElsewhere: Story = {
  render: function StartingElsewhereStory() {
    return <Tabs items={useOrderTabs()} defaultTabId="measurements" />
  },
}

/** On a phone the strip scrolls sideways rather than wrapping: the page never scrolls, the strip does. */
export const OnAPhone: Story = {
  globals: { viewport: { value: 'referencePhone' } },
  render: function OnAPhoneStory() {
    return <Tabs items={useOrderTabs()} />
  },
}

/** At the 320 px reflow floor of 1.4.10. */
export const ReflowFloor: Story = {
  globals: { viewport: { value: 'reflowFloor' } },
  render: function ReflowFloorStory() {
    return <Tabs items={useOrderTabs()} />
  },
}

/** The pseudo-locale, at 40% growth: the strip scrolls, no tab name is clipped. */
export const PseudoLocale: Story = {
  globals: { locale: 'en-XA' },
  render: function PseudoLocaleStory() {
    return <Tabs items={useOrderTabs()} />
  },
}
