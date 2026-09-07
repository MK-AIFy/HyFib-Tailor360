import type { Meta, StoryObj } from '@storybook/react-vite'
import { Alert } from './Alert'
import { Button } from './Button'
import { ALERT_TONES } from './variants'
import './storybook.css'

/**
 * Alerts.
 *
 * There is no toast in this design system, and this component is the reason.
 * docs/nfr/accessibility-localisation.md section 6 forbids one for a scan result, for sync state and
 * for any actionable error, on two grounds that both hold on a shop floor: it disappears before
 * somebody holding a garment has read it, and a screen reader user may never hear it at all. An
 * alert renders in the flow of the page, above the thing it is about, and waits.
 */
const meta = {
  title: 'Primitives/Alert',
  component: Alert,
  args: { tone: 'info', children: 'A new price list version was published this morning.' },
  parameters: { layout: 'padded' },
} satisfies Meta<typeof Alert>

export default meta
type Story = StoryObj<typeof meta>

export const Info: Story = {}

export const Success: Story = { args: { tone: 'success', children: 'Measurements saved.' } }

export const Warning: Story = {
  args: {
    tone: 'warning',
    title: 'Working offline',
    children: 'Scans are being queued and will be sent when the connection returns.',
  },
}

export const Danger: Story = {
  args: {
    tone: 'danger',
    title: 'Scan rejected',
    children: 'This label belongs to another branch. Check the job number and scan again.',
  },
}

/** All four tones. Read them in greyscale: the glyph shape and the hidden severity word both survive. */
export const AllTones: Story = {
  render: () => (
    <div className="storybook-stack">
      {ALERT_TONES.map((tone) => (
        <Alert key={tone} tone={tone}>
          This is the {tone} tone.
        </Alert>
      ))}
    </div>
  ),
}

/**
 * A rejected scan: assertive, because it has stopped what the person was doing, and it names **which**
 * rule failed rather than saying "invalid".
 */
export const RejectedScan: Story = {
  args: {
    tone: 'danger',
    live: 'assertive',
    title: 'Scan rejected — wrong branch',
    children:
      'Job J-CBE02-2627-000188-01 belongs to Coimbatore 2. Hand it to the branch it belongs to.',
    actions: <Button variant="primary">Scan another label</Button>,
  },
}

/**
 * An action that cannot be queued.
 *
 * No dismiss control: section 8.3 requires the network state to be persistent and non-dismissible,
 * and a blocked action must say plainly that it will not be queued.
 */
export const OfflineAndBlocked: Story = {
  args: {
    tone: 'warning',
    live: 'polite',
    title: 'Needs connection',
    children:
      'Taking a payment needs a connection. This will not be queued — what you have typed has been kept.',
  },
}

export const Dismissible: Story = {
  args: { tone: 'info', onDismiss: () => undefined },
}

export const WithActions: Story = {
  args: {
    tone: 'danger',
    live: 'polite',
    title: 'The scan could not be sent',
    children: 'The connection dropped while sending. Nothing has been lost.',
    actions: (
      <>
        <Button variant="primary">Try again</Button>
        <Button variant="subtle">Show the details</Button>
      </>
    ),
  },
}

/** The pseudo-locale, at 40% growth, in a 320 px pane — the reflow floor of 1.4.10. */
export const PseudoLocale: Story = {
  globals: { locale: 'en-XA' },
  render: () => (
    <div className="storybook-phone">
      <Alert
        tone="danger"
        live="assertive"
        title="Scan rejected"
        actions={<Button variant="primary">Scan another label</Button>}
        onDismiss={() => undefined}
      >
        The check character did not match.
      </Alert>
    </div>
  ),
}
