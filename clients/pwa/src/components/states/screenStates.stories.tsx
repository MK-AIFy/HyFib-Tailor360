import type { Meta, StoryObj } from '@storybook/react-vite'
import { Button } from '../primitives/Button'
import { EmptyState } from './EmptyState'
import { ErrorState } from './ErrorState'
import { Forbidden } from './Forbidden'
import { LoadingState } from './LoadingState'
import './statesStories.css'

/**
 * The screen states.
 *
 * DoD item 7 requires a story for the **loading, empty, error, offline and forbidden** state of
 * every new screen, and section 4.12 of docs/nfr/a11y-checklist.md asks the same three questions of
 * each: does it say **what** happened, does it say **what to do next**, and is there a
 * keyboard-reachable way on. Four of the five are here; offline is `NetworkStatusBanner` and
 * `OfflineBlockedAction`.
 */
const meta = {
  title: 'States/Screen states',
  component: EmptyState,
  parameters: { layout: 'padded' },
} satisfies Meta<typeof EmptyState>

export default meta
type Story = StoryObj<typeof meta>

/** An empty queue that says what would put something in it. */
export const Empty: Story = {
  args: {
    title: 'No jobs in this queue',
    children: 'Jobs appear here when a Tailor Master assigns them to you.',
  },
}

/** An empty search result, with the control that answers it. */
export const EmptyAfterSearch: Story = {
  args: {
    title: 'No customers match “Ravi”',
    children: 'Check the spelling, or search by phone number instead.',
    iconName: 'search',
    actions: <Button variant="primary">Clear the search</Button>,
  },
}

/**
 * Loading, saying what is loading.
 *
 * Turn reduced motion on and the glyph stops. Nothing is lost, because the sentence was always the
 * real channel — checklist item A11Y-73.
 */
export const Loading: Story = {
  render: () => <LoadingState what="the delivery queue" />,
}

/** A region that could not be shown, with a read that is always safe to repeat. */
export const Error: Story = {
  render: () => <ErrorState onRetry={() => undefined} />,
}

/**
 * The forbidden state.
 *
 * Deny-by-default makes this a normal state rather than an error: a Tailor meets one every time
 * they open a billing screen. So it names what was refused, names who can, and leaves a way back —
 * the three things checklist item A11Y-89 asks for.
 */
export const ForbiddenForThisRole: Story = {
  render: () => (
    <Forbidden
      action="Taking a payment"
      allowedRoles={['Cashier', 'Branch Manager']}
      onBack={() => undefined}
    />
  ),
}

/** All four, so the family reads as one thing. Look at them in greyscale and in the contrast theme. */
export const AllStates: Story = {
  render: () => (
    <div className="states-story-stack">
      <LoadingState what="the delivery queue" />
      <EmptyState title="No jobs in this queue">
        Jobs appear here when a Tailor Master assigns them to you.
      </EmptyState>
      <ErrorState onRetry={() => undefined} />
      <Forbidden action="Taking a payment" allowedRoles={['Cashier']} onBack={() => undefined} />
    </div>
  ),
}

/** The pseudo-locale, at 40% growth, in a 320 px pane — the reflow floor of 1.4.10. */
export const PseudoLocale: Story = {
  globals: { locale: 'en-XA' },
  render: () => (
    <div className="states-story-phone">
      <div className="states-story-stack">
        <LoadingState what="the delivery queue" />
        <EmptyState />
        <Forbidden action="Taking a payment" allowedRoles={['Cashier']} onBack={() => undefined} />
      </div>
    </div>
  ),
}
