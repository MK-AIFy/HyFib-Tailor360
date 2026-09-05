import { useState } from 'react'
import type { Meta, StoryObj } from '@storybook/react-vite'
import { Filters } from './Filters'
import type { AppliedFilter } from './Filters'
import { useDemoText } from './demoText'
import './storybook.css'

/**
 * The filter region of a list screen.
 *
 * It owns three things and no more: somewhere to put the controls, the chips saying what is
 * currently applied, and the result count. The controls themselves are ordinary fields from the
 * forms family — inventing a second kind of select for filtering is how a design system ends up with
 * two of everything.
 *
 * The chips are the part that earns its place. A filter that is on but invisible is why somebody
 * reports that a job has vanished from the queue.
 */
const meta = {
  title: 'Primitives/Filters',
  component: Filters,
  parameters: { layout: 'padded' },
} satisfies Meta<typeof Filters>

export default meta
type Story = StoryObj<typeof meta>

export const NothingApplied: Story = { args: { resultCount: 128 } }

export const WithAppliedFilters: Story = {
  render: function WithAppliedFiltersStory() {
    const demo = useDemoText()
    return (
      <Filters
        resultCount={7}
        applied={[
          { id: 'phase', label: demo('Phase: Finishing') },
          { id: 'due', label: demo('Due: this week') },
          { id: 'branch', label: demo('Branch: Coimbatore 1') },
        ]}
        onRemove={() => undefined}
        onClearAll={() => undefined}
      />
    )
  },
}

/**
 * Removing a chip, live.
 *
 * Click a remove control with a screen reader on: the count below is announced politely, once,
 * because the region owns the announcement rather than each control that changed it (4.1.3).
 */
export const Interactive: Story = {
  render: function InteractiveStory() {
    const demo = useDemoText()
    const all: readonly AppliedFilter[] = [
      { id: 'phase', label: demo('Phase: Finishing') },
      { id: 'due', label: demo('Due: this week') },
      { id: 'status', label: demo('Status: Overdue') },
    ]
    const [applied, setApplied] = useState(all)

    return (
      <Filters
        applied={applied}
        resultCount={applied.length * 9}
        onRemove={(id) => {
          setApplied((current) => current.filter((filter) => filter.id !== id))
        }}
        onClearAll={() => {
          setApplied([])
        }}
      />
    )
  },
}

/** With the controls a screen supplies. The fields themselves belong to the forms family. */
export const WithControls: Story = {
  render: function WithControlsStory() {
    const demo = useDemoText()
    return (
      <Filters
        resultCount={41}
        applied={[{ id: 'due', label: demo('Due: this week') }]}
        onRemove={() => undefined}
      >
        <p>{demo('The selects, date fields and search box the forms family supplies go here.')}</p>
      </Filters>
    )
  },
}

/** At 40% growth in a 320 px pane: the chips wrap, and every applied filter stays on screen. */
export const PseudoLocale: Story = {
  globals: { locale: 'en-XA' },
  render: function PseudoLocaleStory() {
    const demo = useDemoText()
    return (
      <div className="storybook-phone">
        <Filters
          resultCount={7}
          applied={[
            { id: 'phase', label: demo('Phase: Finishing') },
            { id: 'due', label: demo('Due: this week') },
          ]}
          onRemove={() => undefined}
          onClearAll={() => undefined}
        />
      </div>
    )
  },
}
