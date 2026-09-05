import type { Meta, StoryObj } from '@storybook/react-vite'
import { NetworkStatusBanner } from './NetworkStatusBanner'
import type { NetworkState } from './useNetworkState'
import './statesStories.css'

const offline: NetworkState = {
  online: false,
  restored: false,
  acknowledgeRestored: () => undefined,
}
const restored: NetworkState = {
  online: true,
  restored: true,
  acknowledgeRestored: () => undefined,
}
const online: NetworkState = { online: true, restored: false, acknowledgeRestored: () => undefined }

/**
 * The connection, stated and kept there.
 *
 * It is never a toast — docs/nfr/accessibility-localisation.md section 6 forbids one for sync state
 * outright — and the offline message cannot be dismissed, which is why this component has no
 * `onDismiss` prop at all. Checklist item A11Y-OF-01 is the manual instrument: is the banner
 * persistent and announced, and does it stay until the connection returns.
 *
 * The stories pass an explicit state, because a story that waited for the browser to lose its
 * connection would show nothing.
 */
const meta = {
  title: 'States/NetworkStatusBanner',
  component: NetworkStatusBanner,
  parameters: { layout: 'fullscreen' },
} satisfies Meta<typeof NetworkStatusBanner>

export default meta
type Story = StoryObj<typeof meta>

/** The connection has gone, and the banner says which half of the job still works. */
export const Offline: Story = {
  args: { state: offline },
  render: (args) => (
    <div>
      <NetworkStatusBanner {...args} />
      <div className="states-story-page">
        <p>The screen behind carries on. Scanning and looking things up still work.</p>
      </div>
    </div>
  ),
}

/** The connection is back, and says so rather than merely stopping the warning. */
export const Restored: Story = {
  args: { state: restored },
  render: (args) => (
    <div>
      <NetworkStatusBanner {...args} />
      <div className="states-story-page">
        <p>The message stays until the person puts it away. There is no timer on it.</p>
      </div>
    </div>
  ),
}

/**
 * Nothing to say.
 *
 * The region is still on the page. A live region inserted at the moment its content appears is the
 * commonest reason an announcement is missed — the region has to exist before it changes — so it
 * renders empty and takes no space.
 */
export const Connected: Story = {
  args: { state: online },
  render: (args) => (
    <div>
      <NetworkStatusBanner {...args} />
      <div className="states-story-page">
        <p>Inspect the DOM: the status region is there, and empty.</p>
      </div>
    </div>
  ),
}

/** With the detail #51 adds — the queued count, announced inside the same region. */
export const WithQueuedWork: Story = {
  args: { state: offline },
  render: (args) => (
    <NetworkStatusBanner {...args}>
      <p>4 scans are waiting to be sent. Nothing else is being queued.</p>
    </NetworkStatusBanner>
  ),
}

/** The pseudo-locale, at 40% growth, at the 320 px reflow floor. */
export const PseudoLocale: Story = {
  args: { state: offline },
  globals: { locale: 'en-XA' },
  render: (args) => (
    <div className="states-story-phone">
      <NetworkStatusBanner {...args} />
    </div>
  ),
}
