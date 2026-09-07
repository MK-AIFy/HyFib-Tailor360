import type { Meta, StoryObj } from '@storybook/react-vite'
import { OfflineBlockedAction } from './OfflineBlockedAction'
import './statesStories.css'

/**
 * An action that needs a connection and will not be queued.
 *
 * The title is fixed by plan Section 4.6, word for word: **"Needs connection — this will not be
 * queued"**. It carries the product's most important offline promise — billing, payment and
 * inventory reconciliation are online-only, and nothing here silently accepts money into a queue.
 *
 * Checklist item A11Y-OF-02 asks all three questions this state has to answer: does it say it needs
 * a connection, does it say it will **not** be queued, and does the typed input stay on the screen.
 */
const meta = {
  title: 'States/OfflineBlockedAction',
  component: OfflineBlockedAction,
  args: { action: 'Taking a payment', online: false },
  parameters: { layout: 'padded' },
} satisfies Meta<typeof OfflineBlockedAction>

export default meta
type Story = StoryObj<typeof meta>

export const TakingAPayment: Story = {}

export const PostingAnInvoice: Story = {
  args: { action: 'Posting the invoice' },
}

/** A stock movement, with the part of the journey that did survive. */
export const IssuingMaterial: Story = {
  args: {
    action: 'Issuing material',
    children: <p>The job card itself has been saved. Only the stock movement is blocked.</p>,
  },
}

/** Once the connection is back, the retry appears. Before that it would only teach a second press. */
export const ConnectionReturned: Story = {
  args: { online: true, onRetry: () => undefined },
}

/** The pseudo-locale, at 40% growth, at the 320 px reflow floor. */
export const PseudoLocale: Story = {
  globals: { locale: 'en-XA' },
  render: (args) => (
    <div className="states-story-phone">
      <OfflineBlockedAction {...args} />
    </div>
  ),
}
