import { useState } from 'react'
import type { Meta, StoryObj } from '@storybook/react-vite'
import { Button } from '../primitives/Button'
import { UndoBar } from './UndoBar'
import './dialogsStories.css'

/**
 * Three seconds to take it back.
 *
 * The counterpart to `ConfirmDialog`, and the reason most field edits need no confirmation at all:
 * docs/nfr/accessibility-localisation.md section 8.4 pairs them deliberately. Asking twice for a
 * shoulder measurement is how people learn to dismiss confirmations without reading them, which is
 * what makes the confirmation on the invoice worthless.
 *
 * It is not a toast. It sits in the flow of the page rather than over it, it holds its countdown
 * open while the pointer is over it or focus is inside it, and the announcement is made once rather
 * than on every tick — hover it, or Tab to Undo, and watch the countdown stop.
 */
const meta = {
  title: 'Dialogs/UndoBar',
  component: UndoBar,
  args: {
    action: 'Shoulder set to 16 1/2 in',
    onUndo: () => undefined,
    onExpire: () => undefined,
  },
  parameters: { layout: 'padded' },
} satisfies Meta<typeof UndoBar>

export default meta
type Story = StoryObj<typeof meta>

/**
 * A long window, so the bar can be read and inspected.
 *
 * The shipped default is three seconds; this story lengthens it because a story that vanished
 * before it could be looked at would be no story at all.
 */
export const HeldOpenForReview: Story = {
  args: { durationMs: 60000 },
}

/** The real three seconds, restartable. */
export const ThreeSeconds: Story = {
  render: () => {
    function Field() {
      const [value, setValue] = useState('16 1/2 in')
      const [previous, setPrevious] = useState<string | null>(null)

      return (
        <div className="dialogs-story-stack">
          <p>Shoulder: {value}</p>
          <div>
            <Button
              onClick={() => {
                setPrevious(value)
                setValue('17 in')
              }}
            >
              Set shoulder to 17 in
            </Button>
          </div>
          {previous === null ? null : (
            <UndoBar
              action={`Shoulder set to ${value}`}
              onExpire={() => {
                setPrevious(null)
              }}
              onUndo={() => {
                setValue(previous)
                setPrevious(null)
              }}
            />
          )}
        </div>
      )
    }

    return <Field />
  },
}

/** At the 320 px reflow floor, where the bar wraps rather than pushing the page sideways. */
export const AtTheReflowFloor: Story = {
  args: { durationMs: 60000 },
  render: (args) => (
    <div className="dialogs-story-phone">
      <UndoBar {...args} />
    </div>
  ),
}

/** The pseudo-locale, at 40% growth. */
export const PseudoLocale: Story = {
  args: { durationMs: 60000 },
  globals: { locale: 'en-XA' },
  render: (args) => (
    <div className="dialogs-story-phone">
      <UndoBar {...args} />
    </div>
  ),
}
