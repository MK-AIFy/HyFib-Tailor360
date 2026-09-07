import type { Meta, StoryObj } from '@storybook/react-vite'
import { NavBadge } from './NavBadge'

/**
 * The attention count on a navigation destination.
 *
 * A number and a sentence, never a coloured dot. A dot says "something here" only to somebody who
 * can see it and already knows what it means, which makes it a status conveyed by colour alone —
 * forbidden outright by docs/nfr/accessibility-localisation.md section 4.1.
 */
const meta = {
  title: 'Navigation/Attention count',
  component: NavBadge,
  args: { count: 4 },
  parameters: { layout: 'centered' },
} satisfies Meta<typeof NavBadge>

export default meta
type Story = StoryObj<typeof meta>

export const Several: Story = {}

export const One: Story = { args: { count: 1 } }

/** Capped on screen, exact when spoken: "a lot" for a glance, "107" for a screen reader. */
export const Many: Story = { args: { count: 107 } }

/** A zero renders nothing at all, rather than a badge that says there is nothing to see. */
export const None: Story = { args: { count: 0 } }

/** The pseudo-locale, at 40% growth. The digits do not grow; the sentence behind them does. */
export const PseudoLocale: Story = { globals: { locale: 'en-XA' }, args: { count: 12 } }
