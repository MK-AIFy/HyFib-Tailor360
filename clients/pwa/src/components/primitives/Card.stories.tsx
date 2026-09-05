import type { Meta, StoryObj } from '@storybook/react-vite'
import { Button } from './Button'
import { ButtonGroup } from './ButtonGroup'
import { Card } from './Card'
import { StatusBadge } from './StatusBadge'
import { useDemoText } from './demoText'
import './storybook.css'

/**
 * Cards.
 *
 * A card is a container, never a control. A card that were a button could not hold a link, a status
 * badge and three row actions without nesting interactive elements — which no screen reader
 * announces sensibly and no keyboard user can tab past.
 */
const meta = {
  title: 'Primitives/Card',
  component: Card,
  args: { children: 'Blouse, Aari work. Due 12-09-2026.' },
  parameters: { layout: 'padded' },
} satisfies Meta<typeof Card>

export default meta
type Story = StoryObj<typeof meta>

export const Plain: Story = {}

export const Titled: Story = {
  args: { title: 'J-CBE01-2627-000512-01', headingLevel: 2 },
}

/** A job card as a queue renders it: identity, status, and the actions at the foot. */
export const JobCard: Story = {
  render: function JobCardStory() {
    const demo = useDemoText()
    return (
      <div className="storybook-narrow">
        <Card
          title="J-CBE01-2627-000512-01"
          headingLevel={2}
          meta={<StatusBadge status="overdue" detail={demo('4 days')} />}
          raised
          actions={
            <ButtonGroup>
              <Button variant="primary" iconName="scan">
                {demo('Scan in')}
              </Button>
              <Button iconName="receipt">{demo('Print label')}</Button>
            </ButtonGroup>
          }
        >
          <p>{demo('Blouse, Aari work — Lakshmi Narayanan')}</p>
          <p>{demo('Due 12-09-2026 · Finishing · Kavitha R')}</p>
        </Card>
      </div>
    )
  },
}

/** The selected card in a master-detail pane: a background *and* an inline rule, never a tint alone. */
export const Selected: Story = {
  args: { title: 'Order 4021', headingLevel: 3, selected: true, children: 'Two garment jobs' },
}

export const Raised: Story = {
  args: {
    title: 'Payment summary',
    headingLevel: 3,
    raised: true,
    children: 'Balance due ₹2,450.00',
  },
}

/**
 * The pseudo-locale, at 40% growth, in a pane the width of a phone.
 *
 * Everything on this card — heading, status, body and both actions — is running through the growth
 * transformation, which is as close as a story gets to what Tamil will do to it.
 */
export const PseudoLocale: Story = {
  globals: { locale: 'en-XA' },
  render: function PseudoLocaleStory() {
    const demo = useDemoText()
    return (
      <div className="storybook-phone">
        <Card
          title={demo('J-CBE01-2627-000512-01')}
          headingLevel={2}
          meta={<StatusBadge status="rework" />}
          actions={
            <ButtonGroup>
              <Button variant="primary">{demo('Complete the phase')}</Button>
              <Button variant="subtle">{demo('Open the job card')}</Button>
            </ButtonGroup>
          }
        >
          <p>{demo('Blouse, Aari work — Lakshmi Narayanan')}</p>
        </Card>
      </div>
    )
  },
}
