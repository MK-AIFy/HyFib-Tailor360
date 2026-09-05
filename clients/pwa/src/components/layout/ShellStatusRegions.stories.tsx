import type { Meta, StoryObj } from '@storybook/react-vite'
import { Button } from '../primitives/Button'
import { useDemoText } from '../primitives/demoText'
import { ShellStatusProvider } from './ShellStatusProvider'
import type { ShellStatusProviderProps } from './ShellStatusProvider'
import { ShellStatusRegions } from './ShellStatusRegions'
import { useShellStatus } from './useShellStatus'
import './layoutStories.css'

/**
 * The shell's persistent status regions: scan result, sync state, draft state.
 *
 * None of them is a toast, and that is a rule rather than a preference.
 * docs/nfr/accessibility-localisation.md section 6 forbids a toast for a scan result, for sync state
 * and for any actionable error on two grounds that both hold on a shop floor: it disappears before
 * somebody holding a garment has read it, and a screen-reader user may never hear it at all.
 * Checklist item A11Y-42 is the question asked on a real device — is this message still on the
 * screen?
 *
 * Four regions rather than three: politeness cannot be changed on a live element and be relied upon,
 * so a rejected scan has its own `role="alert"` container and an accepted one its own
 * `role="status"`. All four are mounted from the first paint, empty, and take no space while they
 * are — a live region inserted at the moment its content arrives announces nothing at all.
 *
 * `Interactive` is the one to try with a screen reader running: a rejection interrupts, an
 * acceptance waits its turn, and both stay on the screen until they are dismissed or superseded.
 */
const meta = {
  title: 'Layout/Shell status regions',
  component: ShellStatusRegions,
  parameters: { layout: 'padded' },
} satisfies Meta<typeof ShellStatusRegions>

export default meta
type Story = StoryObj<typeof meta>

function withStatus(props: Omit<ShellStatusProviderProps, 'children'>): Story {
  return {
    render: function StatusStory() {
      return (
        <ShellStatusProvider {...props}>
          <ShellStatusRegions />
        </ShellStatusProvider>
      )
    },
  }
}

/** Nothing to say. Every region is mounted and every one is empty; the shell shows no gap. */
export const Quiet: Story = withStatus({})

/** A scan that worked: the job number and the next expected action, announced politely. */
export const ScanAccepted: Story = withStatus({
  initialScan: {
    outcome: 'accepted',
    message: 'J-CBE01-2627-000512-01 — take custody, then start cutting.',
  },
})

/**
 * A scan that did not: assertive, and it names **which** rule failed.
 *
 * "Invalid" is a dead end. The garment has already moved on, so the message has to say what to do.
 */
export const ScanRejected: Story = withStatus({
  initialScan: {
    outcome: 'rejected',
    message: 'This job belongs to Branch 02. Ask the counter to transfer it before scanning.',
  },
})

/** Queued while offline. No dismiss control: a queued scan dismissed is a queued scan forgotten. */
export const Queued: Story = withStatus({
  initialSync: { tone: 'warning', message: '3 scans are waiting to be sent.' },
})

/** The draft state. Silence and failure sound identical without it (checklist item A11Y-44). */
export const Autosaving: Story = withStatus({ initialAutosave: 'Saved at 3:42 pm.' })

/** All three channels at once, which is what a busy shop floor actually looks like. */
export const Everything: Story = withStatus({
  initialScan: {
    outcome: 'accepted',
    message: 'J-CBE01-2627-000512-01 — take custody, then start cutting.',
  },
  initialSync: { tone: 'warning', message: '3 scans are waiting to be sent.' },
  initialAutosave: 'Saved at 3:42 pm.',
})

/** Publish into the regions the way a scanning screen will. */
export const Interactive: Story = {
  render: function InteractiveStory() {
    return (
      <ShellStatusProvider>
        <ShellStatusRegions />
        <StatusControls />
      </ShellStatusProvider>
    )
  },
}

function StatusControls() {
  const demo = useDemoText()
  const { announceScan, announceSync, announceAutosave } = useShellStatus()

  return (
    <div className="layout-story-card">
      <Button
        onClick={() => {
          announceScan({
            outcome: 'accepted',
            message: demo('J-CBE01-2627-000512-01 — take custody, then start cutting.'),
          })
        }}
      >
        {demo('Accept a scan')}
      </Button>{' '}
      <Button
        variant="danger"
        onClick={() => {
          announceScan({
            outcome: 'rejected',
            message: demo('This job belongs to Branch 02.'),
          })
        }}
      >
        {demo('Reject a scan')}
      </Button>{' '}
      <Button
        onClick={() => {
          announceSync({ tone: 'warning', message: demo('3 scans are waiting to be sent.') })
        }}
      >
        {demo('Queue three scans')}
      </Button>{' '}
      <Button
        onClick={() => {
          announceAutosave(demo('Saved at 3:42 pm.'))
        }}
      >
        {demo('Save the draft')}
      </Button>
    </div>
  )
}
