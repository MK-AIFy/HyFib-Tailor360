import type { Meta, StoryObj } from '@storybook/react-vite'
import { RetryableError } from './RetryableError'
import './statesStories.css'

/**
 * A request that was sent and did not land.
 *
 * Everything about how this reads comes from docs/nfr/accessibility-localisation.md section 8.2: the
 * failure in plain language, never a code and never a stack; the correlation identifier so that "it
 * did not work" becomes a line a technical reviewer can find; and a retry that is safe to press
 * because it resends the same request with the same `Idempotency-Key`.
 *
 * It is the write-side failure. The read-side one — a tile that did not load — is `ErrorState`, and
 * a connection known to be absent is `OfflineBlockedAction`.
 */
const meta = {
  title: 'States/RetryableError',
  component: RetryableError,
  args: { action: 'Recording the payment', onRetry: () => undefined },
  parameters: { layout: 'padded' },
} satisfies Meta<typeof RetryableError>

export default meta
type Story = StoryObj<typeof meta>

/** Nothing came back at all, so nothing is known about whether the command ran. */
export const ConnectionDropped: Story = {
  args: { cause: 'network' },
}

export const ServerCouldNotFinish: Story = {
  args: { problem: { status: 503, correlationId: '01JAV7Q0YQ8ZK3M2' } },
}

/** The rate-limit policy catalogue of plan Section 4.4, in words a counter can use. */
export const TooManyAtOnce: Story = {
  args: { problem: { status: 429 } },
}

/** A concurrency conflict from the `ETag` / `If-Match` tokens of #53. */
export const SomebodyElseChangedIt: Story = {
  args: {
    action: 'Confirming the order',
    problem: { status: 409, detail: 'This invoice was already posted at 4:31 PM.' },
  },
}

/**
 * A misconfigured server sending an exception message.
 *
 * The person still gets a sentence this application wrote; the machine text is dropped by
 * `plainLanguageDetail`. A rule enforced by review alone ships the first time a server is
 * misconfigured, so it is enforced by a function instead.
 */
export const ServerSentAStackTrace: Story = {
  args: {
    problem: {
      status: 500,
      detail: 'Object reference not set\n   at HyFib.Billing.Post(Invoice invoice)',
      correlationId: '01JAV7Q0YQ8ZK3M2',
    },
  },
}

export const Retrying: Story = {
  args: { problem: { status: 503 }, retrying: true },
}

/** The pseudo-locale, at 40% growth, at the 320 px reflow floor. */
export const PseudoLocale: Story = {
  globals: { locale: 'en-XA' },
  render: (args) => (
    <div className="states-story-phone">
      <RetryableError {...args} problem={{ status: 500, correlationId: '01JAV7Q0YQ8ZK3M2' }} />
    </div>
  ),
}
